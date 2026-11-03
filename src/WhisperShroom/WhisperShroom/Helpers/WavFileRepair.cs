using System.Buffers.Binary;
using System.Text;

namespace WhisperShroom.Helpers;

/// <summary>
/// Repairs the RIFF header of a WAV file whose writer was terminated before finalizing it
/// (process crash, power loss). The audio bytes after the "data" chunk header are intact;
/// only the RIFF size and the data chunk size may lag behind the real file length.
/// </summary>
internal static class WavFileRepair
{
    private const int MaxHeaderScanBytes = 4096;

    /// <summary>
    /// Rewrites the RIFF and data sizes to match the file length, trimming a trailing partial
    /// sample frame. Returns the number of audio data bytes, or null if the file is not a
    /// RIFF/WAVE file with fmt and data chunks.
    /// </summary>
    public static long? RepairHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var header = new byte[(int)Math.Min(MaxHeaderScanBytes, stream.Length)];
        stream.ReadExactly(header);

        if (header.Length < 12 ||
            Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
            return null;

        int? blockAlign = null;
        var offset = 12;

        while (offset + 8 <= header.Length)
        {
            var chunkId = Encoding.ASCII.GetString(header, offset, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset + 4, 4));

            if (chunkId == "fmt " && offset + 8 + 14 <= header.Length)
            {
                // WAVEFORMATEX: wFormatTag(2) nChannels(2) nSamplesPerSec(4) nAvgBytesPerSec(4) nBlockAlign(2)
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(offset + 8 + 12, 2));
            }
            else if (chunkId == "data")
            {
                if (blockAlign is null or 0)
                    return null;

                var dataStart = offset + 8;
                var dataBytes = stream.Length - dataStart;
                dataBytes -= dataBytes % blockAlign.Value;
                if (dataBytes < 0)
                    return null;

                stream.SetLength(dataStart + dataBytes);

                Span<byte> size = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(dataStart + dataBytes - 8));
                stream.Position = 4;
                stream.Write(size);

                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)dataBytes);
                stream.Position = offset + 4;
                stream.Write(size);

                return dataBytes;
            }

            // Chunks are word-aligned: odd sizes carry one pad byte.
            offset += 8 + (int)chunkSize + (int)(chunkSize % 2);
        }

        return null;
    }
}
