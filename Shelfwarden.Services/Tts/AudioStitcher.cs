using Xabe.FFmpeg;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Concatenates raw 16-bit PCM chunk files (the format Kokoro emits at 24 kHz mono) and
/// hands the result to FFmpeg for AAC-in-MP4 (.m4a) compression. Streaming throughout —
/// nothing larger than a single buffer of bytes is held in memory at any time, so this
/// scales to multi-hour books without bloat.
/// </summary>
public sealed class AudioStitcher(
    ILogger<AudioStitcher> logger,
    IFFmpegProvider ffmpegProvider)
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

        string workingDir = Path.GetDirectoryName(outputM4aPath)!;
        Directory.CreateDirectory(workingDir);
        string mergedPcmPath = Path.Combine(workingDir, "_merged.pcm");

        await ConcatenatePcmAsync(pcmChunkFiles, mergedPcmPath, cancellationToken);

        try
        {
            var conversion = FFmpeg.Conversions.New()
                // Tell FFmpeg how to interpret the headerless PCM input — sample rate, channel
                // count and sample format must all match what Kokoro emitted.
                .AddParameter($"-f s16le -ar {SampleRateHz} -ac {Channels}", ParameterPosition.PreInput)
                .AddParameter($"-i \"{mergedPcmPath}\"", ParameterPosition.PreInput)
                // 64 kbps AAC is plenty for spoken word — voices stay clear and the file is
                // small enough that an 18-hour novel comes in under ~500 MB.
                .AddParameter("-c:a aac -b:a 64k -movflags +faststart")
                .SetOverwriteOutput(true)
                .SetOutput(outputM4aPath);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Running FFmpeg with arguments: {Args}", conversion.Build());
            }

            var info = await conversion.Start(cancellationToken);

            return new EncodingResult(
                outputM4aPath,
                new FileInfo(outputM4aPath).Length,
                info?.Duration.TotalSeconds ?? 0d);
        }
        finally
        {
            // Always remove the giant intermediate PCM, success or failure — it can run into
            // gigabytes for a long book and there's nothing to recover from it.
            try
            {
                if (File.Exists(mergedPcmPath))
                {
                    File.Delete(mergedPcmPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete intermediate PCM file {Path}", mergedPcmPath);
            }
        }
    }

    private static async Task ConcatenatePcmAsync(
        IReadOnlyList<string> pcmChunkFiles,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        foreach (string chunk in pcmChunkFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var input = new FileStream(
                chunk, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            await input.CopyToAsync(output, 81920, cancellationToken);
        }
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
                buffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
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
            buffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
    }

}

/// <summary>Result of a successful audiobook encoding pass.</summary>
public sealed record EncodingResult(string FilePath, long FileSizeBytes, double DurationSeconds);
