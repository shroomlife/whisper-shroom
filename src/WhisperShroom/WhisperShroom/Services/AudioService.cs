using NAudio.CoreAudioApi;
using NAudio.Wave;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

/// <summary>Outcome of a finished recording. The WAV at <see cref="FilePath"/> is finalized.</summary>
public sealed record RecordingResult(string FilePath, bool HasAudio, Exception? Error);

/// <summary>
/// Captures 16 kHz mono 16-bit PCM via WASAPI and streams it straight into a WAV file.
///
/// Threading: NAudio raises DataAvailable on its capture thread, and that thread is the
/// only one that touches the <see cref="WaveFileWriter"/> while capturing (the writer is not
/// thread-safe). <see cref="WasapiCapture.StopRecording"/> only signals the capture thread,
/// so stopping awaits RecordingStopped, which NAudio raises after the capture thread has left
/// its loop. Only then is the writer finalized and disposed.
/// </summary>
public sealed class AudioService : IDisposable
{
    private const float SilenceThreshold = 0.005f;
    private const int TargetSampleRate = 16000;
    private const int FlushIntervalSeconds = 2;

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private WaveFileWriter? _writer;
    private string? _filePath;
    private TaskCompletionSource<Exception?>? _stopped;
    private long _bytesSinceFlush;
    private int _flushThresholdBytes;
    private bool _hasAudio;

    private WasapiCapture? _monitor;
    private MMDevice? _monitorDevice;

    public bool IsRecording { get; private set; }

    /// <summary>True while recording or while a stopped recording is still being finalized.</summary>
    public bool IsBusy => _capture is not null;

    /// <summary>True once any captured buffer exceeded the silence threshold.</summary>
    public bool HasAudio => Volatile.Read(ref _hasAudio);

    public event Action<float>? LevelUpdated;

    public IReadOnlyList<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
                devices.Add(new AudioDevice(device.ID, device.FriendlyName));
        }

        return devices;
    }

    /// <summary>Returns the endpoint ID of the active capture device with this friendly name.</summary>
    public string? FindDeviceId(string friendlyName) =>
        GetInputDevices().FirstOrDefault(d => d.Name == friendlyName)?.Id;

    /// <summary>
    /// Starts capturing into a new WAV file at <paramref name="filePath"/>. Throws if the
    /// device cannot be opened; in that case no file is left behind.
    /// </summary>
    public void StartRecording(string? deviceId, string filePath)
    {
        if (IsBusy)
            throw new InvalidOperationException("A recording is already in progress.");

        Volatile.Write(ref _hasAudio, false);
        _bytesSinceFlush = 0;
        _filePath = filePath;
        _stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _device = OpenDevice(deviceId);
            _capture = CreateCapture(_device);
            _flushThresholdBytes = _capture.WaveFormat.AverageBytesPerSecond * FlushIntervalSeconds;
            _writer = new WaveFileWriter(filePath, _capture.WaveFormat);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            IsRecording = true;
        }
        catch
        {
            ReleaseCapture();
            TryDeleteFile(filePath);
            _filePath = null;
            _stopped = null;
            throw;
        }
    }

    /// <summary>
    /// Stops capturing, waits until NAudio's capture thread has finished, then finalizes the
    /// WAV header. Returns null if no recording was running.
    /// </summary>
    public async Task<RecordingResult?> StopRecordingAsync()
    {
        if (!IsRecording || _capture is null || _stopped is null || _filePath is null)
            return null;

        var filePath = _filePath;
        var stopped = _stopped;

        IsRecording = false;
        _capture.StopRecording();
        var error = await stopped.Task;

        ReleaseCapture();
        _filePath = null;
        _stopped = null;

        return new RecordingResult(filePath, HasAudio, error);
    }

    /// <summary>Stops capturing and deletes the partial file.</summary>
    public async Task CancelRecordingAsync()
    {
        var result = await StopRecordingAsync();
        if (result is not null)
            TryDeleteFile(result.FilePath);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _writer is null)
            return;

        _writer.Write(e.Buffer, 0, e.BytesRecorded);

        // Keep the RIFF/data sizes in the header current so a crash mid-recording leaves a
        // valid file behind. Flush runs on this capture thread, the same one that writes.
        _bytesSinceFlush += e.BytesRecorded;
        if (_bytesSinceFlush >= _flushThresholdBytes)
        {
            _writer.Flush();
            _bytesSinceFlush = 0;
        }

        if (!HasAudio && ComputeRms(e.Buffer, e.BytesRecorded) > SilenceThreshold)
            Volatile.Write(ref _hasAudio, true);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped?.TrySetResult(e.Exception);
    }

    /// <summary>Disposes capture and writer. Only call once the capture thread has ended.</summary>
    private void ReleaseCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _writer?.Dispose();
        _writer = null;

        _device?.Dispose();
        _device = null;
    }

    // --- Level monitoring (no WAV writing, for the setup wizard's level meter) ---

    public void StartMonitoring(string? deviceId)
    {
        StopMonitoring();

        _monitorDevice = OpenDevice(deviceId);
        _monitor = CreateCapture(_monitorDevice);
        _monitor.DataAvailable += OnMonitorDataAvailable;
        _monitor.StartRecording();
    }

    public void StopMonitoring()
    {
        if (_monitor is not null)
        {
            _monitor.DataAvailable -= OnMonitorDataAvailable;
            _monitor.Dispose(); // Dispose stops capturing and joins the capture thread.
            _monitor = null;
        }

        _monitorDevice?.Dispose();
        _monitorDevice = null;
    }

    private void OnMonitorDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
            LevelUpdated?.Invoke(ComputeRms(e.Buffer, e.BytesRecorded));
    }

    // --- Shared helpers ---

    private static MMDevice? OpenDevice(string? deviceId)
    {
        if (deviceId is null)
            return null;

        using var enumerator = new MMDeviceEnumerator();
        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Device was unplugged or disabled since it was selected: use the default device.
            return null;
        }
    }

    private static WasapiCapture CreateCapture(MMDevice? device)
    {
        var capture = device is not null ? new WasapiCapture(device) : new WasapiCapture();
        capture.WaveFormat = new WaveFormat(TargetSampleRate, 16, 1);
        return capture;
    }

    /// <summary>RMS of a 16-bit little-endian PCM buffer, normalized to 0..1.</summary>
    private static float ComputeRms(byte[] buffer, int bytesRecorded)
    {
        var samples = AsPcm16Samples(buffer, bytesRecorded);
        if (samples.Length == 0)
            return 0f;

        double sum = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / 32768f;
            sum += normalized * normalized;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static ReadOnlySpan<short> AsPcm16Samples(byte[] buffer, int bytesRecorded) =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(
            buffer.AsSpan(0, bytesRecorded - (bytesRecorded % 2)));

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Synchronous shutdown for app exit: WasapiCapture.Dispose joins the capture thread, then
    /// the writer finalizes the header. A recording in progress stays on disk and is picked up
    /// by <see cref="HistoryService.RecoverOrphanedRecordings"/> on the next start.
    /// </summary>
    public void Dispose()
    {
        StopMonitoring();
        IsRecording = false;
        ReleaseCapture();
        _stopped?.TrySetResult(null);
        _stopped = null;
        _filePath = null;
    }
}
