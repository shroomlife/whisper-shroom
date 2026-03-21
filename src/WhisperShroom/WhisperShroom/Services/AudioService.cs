using NAudio.CoreAudioApi;
using NAudio.Wave;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class AudioService : IDisposable
{
    private WasapiCapture? _capture;
    private WasapiCapture? _monitor;
    private MemoryStream? _wavStream;
    private WaveFileWriter? _wavWriter;
    private bool _hasAudio;
    private const float SilenceThreshold = 0.005f;
    private const int TargetSampleRate = 16000;

    public bool IsRecording { get; private set; }
    public event Action<float>? RmsUpdated;
    public event Action<float>? LevelUpdated;

    public IReadOnlyList<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in endpoints)
        {
            devices.Add(new AudioDevice(device.ID, device.FriendlyName));
        }

        return devices;
    }

    public void StartRecording(string? deviceId)
    {
        if (IsRecording) return;

        _hasAudio = false;
        _wavStream = new MemoryStream();

        MMDevice? device = null;
        if (deviceId is not null)
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                device = enumerator.GetDevice(deviceId);
            }
            catch
            {
                device = null;
            }
        }

        if (device is not null)
        {
            _capture = new WasapiCapture(device)
            {
                WaveFormat = new WaveFormat(TargetSampleRate, 16, 1)
            };
        }
        else
        {
            _capture = new WasapiCapture()
            {
                WaveFormat = new WaveFormat(TargetSampleRate, 16, 1)
            };
        }

        _wavWriter = new WaveFileWriter(_wavStream, _capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        IsRecording = true;
    }

    public byte[]? StopRecording()
    {
        if (!IsRecording) return null;

        IsRecording = false;
        _capture?.StopRecording();

        _wavWriter?.Flush();
        var data = _wavStream?.ToArray();

        Cleanup();
        return data;
    }

    public void CancelRecording()
    {
        if (!IsRecording) return;

        IsRecording = false;
        _capture?.StopRecording();
        Cleanup();
    }

    public bool HasAudio => _hasAudio;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);

        // Compute RMS for silence detection (16-bit PCM)
        if (e.BytesRecorded > 0)
        {
            double sum = 0;
            int sampleCount = e.BytesRecorded / 2; // 16-bit = 2 bytes per sample
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float normalized = sample / 32768f;
                sum += normalized * normalized;
            }

            float rms = (float)Math.Sqrt(sum / sampleCount);

            if (rms > SilenceThreshold)
                _hasAudio = true;

            RmsUpdated?.Invoke(rms);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Handled via StopRecording/CancelRecording
    }

    private void Cleanup()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _wavWriter?.Dispose();
        _wavWriter = null;
        _wavStream?.Dispose();
        _wavStream = null;
    }

    // --- Level monitoring (no WAV writing, for live level meter) ---

    public void StartMonitoring(string? deviceId)
    {
        StopMonitoring();

        MMDevice? device = null;
        if (deviceId is not null)
        {
            using var enumerator = new MMDeviceEnumerator();
            try { device = enumerator.GetDevice(deviceId); }
            catch { device = null; }
        }

        _monitor = device is not null
            ? new WasapiCapture(device) { WaveFormat = new WaveFormat(TargetSampleRate, 16, 1) }
            : new WasapiCapture() { WaveFormat = new WaveFormat(TargetSampleRate, 16, 1) };

        _monitor.DataAvailable += OnMonitorDataAvailable;
        _monitor.StartRecording();
    }

    public void StopMonitoring()
    {
        if (_monitor is not null)
        {
            _monitor.DataAvailable -= OnMonitorDataAvailable;
            try { _monitor.StopRecording(); } catch { }
            _monitor.Dispose();
            _monitor = null;
        }
    }

    private void OnMonitorDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        double sum = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            float normalized = sample / 32768f;
            sum += normalized * normalized;
        }

        float rms = (float)Math.Sqrt(sum / sampleCount);
        LevelUpdated?.Invoke(rms);
    }

    public void Dispose()
    {
        StopMonitoring();
        CancelRecording();
        Cleanup();
    }
}
