using Heroicsoft.NetForge.Video;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Concatenates raw 16-bit PCM chunk files (the format Kokoro emits at 24 kHz mono) and
/// hands the result to FFmpeg for AAC-in-MP4 (.m4a) compression. Streaming throughout —
/// nothing larger than a single buffer of bytes is held in memory at any time, so this
/// scales to multi-hour books without bloat.
/// </summary>
public sealed class AudioStitcher(
    ILogger<AudioStitcher> logger,
    IFFmpegProvider ffmpegProvider,
    IPcmAudioEncoder encoder)
{
    /// <summary>Sample rate Kokoro emits at. Mirrors <c>KokoroPlayback.waveFormat</c>.</summary>
    public const int SampleRateHz = 24_000;

    /// <summary>One sample = 16-bit signed (s16le).</summary>
    public const int BitsPerSample = 16;

    /// <summary>Mono.</summary>
    public const int Channels = 1;

    /// <summary>
    /// Concatenate <paramref name="pcmChunkFiles"/> in order into <paramref name="outputM4aPath"/>.
    /// The intermediate PCM file is written next to the output and deleted on success.
    /// </summary>
    public async Task<EncodingResult> EncodeAsync(
        IReadOnlyList<string> pcmChunkFiles,
        string outputM4aPath,
        CancellationToken cancellationToken = default)
    {
        if (pcmChunkFiles.Count == 0)
        {
            throw new InvalidOperationException("Cannot encode an audiobook with zero chunks.");
        }

        await ffmpegProvider.EnsureReadyAsync(cancellationToken);

        logger.LogDebug("Encoding {ChunkCount} PCM chunks to {Path}", pcmChunkFiles.Count, outputM4aPath);

        // 64 kbps AAC is plenty for spoken word. The encoder concatenates the raw chunks and
        // deletes the intermediate PCM, which can run to gigabytes on a long book.
        var encoded = await encoder.EncodeAsync(
            new PcmAudioEncodeRequest
            {
                PcmChunkFiles = pcmChunkFiles,
                OutputPath = outputM4aPath,
                SampleRateHz = SampleRateHz,
                Channels = Channels,
                AudioBitrate = "64k",
            },
            cancellationToken);

        return new EncodingResult(encoded.FilePath, encoded.FileSizeBytes, encoded.DurationSeconds);
    }

    /// <summary>
    /// Writes a single chunk's float samples to <paramref name="outputPath"/> as raw 16-bit
    /// little-endian PCM. The lack of a WAV header makes concatenation a trivial byte-copy.
    /// </summary>
    public static async Task WritePcmChunkAsync(
        float[] samples,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string tempPath = outputPath + ".part";

        await using (var stream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            // Convert float [-1, 1] → 16-bit signed PCM. Kokoro samples can occasionally
            // overshoot the unit interval; clamp before scaling so we don't wrap to -32768.
            byte[] buffer = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Math.Clamp(samples[i], -1f, 1f);
                short pcm = (short)Math.Round(clamped * short.MaxValue);
                buffer[i * 2] = (byte)(pcm & 0xFF);
                buffer[(i * 2) + 1] = (byte)((pcm >> 8) & 0xFF);
            }

            await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    /// <summary>
    /// Writes a self-contained 16-bit PCM WAV file (header + data) for use as a voice
    /// preview. Browsers happily decode 24 kHz mono WAV via the &lt;audio&gt; element.
    /// </summary>
    public static async Task WriteWavAsync(
        float[] samples,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        int byteRate = SampleRateHz * Channels * (BitsPerSample / 8);
        int blockAlign = Channels * (BitsPerSample / 8);
        int dataSize = samples.Length * 2;

        await using var stream = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using var writer = new BinaryWriter(stream);

        // RIFF header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        // fmt chunk
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)Channels);
        writer.Write(SampleRateHz);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)BitsPerSample);

        // data chunk
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        byte[] buffer = new byte[dataSize];
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Clamp(samples[i], -1f, 1f);
            short pcm = (short)Math.Round(clamped * short.MaxValue);
            buffer[i * 2] = (byte)(pcm & 0xFF);
            buffer[(i * 2) + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
    }
}

/// <summary>Result of a successful audiobook encoding pass.</summary>
public sealed record EncodingResult(string FilePath, long FileSizeBytes, double DurationSeconds);