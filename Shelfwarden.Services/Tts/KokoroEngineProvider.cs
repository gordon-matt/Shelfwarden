using KokoroSharp;
using KokoroSharp.Core;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Default <see cref="IKokoroEngineProvider"/>. Downloads the float32 ONNX model into the
/// configured TTS cache directory on first use, then constructs <see cref="KokoroTTS"/>
/// against that path. Voices are loaded from the application's base directory once — the
/// KokoroSharp.CPU NuGet package copies them there at build time.
/// </summary>
public sealed class KokoroEngineProvider : IKokoroEngineProvider, IDisposable
{
    private const string ModelFileName = "kokoro.onnx";
    private const string ModelDownloadUrl = "https://github.com/taylorchu/kokoro-onnx/releases/download/v0.2.0/kokoro.onnx";

    private readonly ILogger<KokoroEngineProvider> logger;
    private readonly IStoragePathProvider storage;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly object voicesLock = new();

    private KokoroTTS? engine;
    private bool voicesLoaded;

    public KokoroEngineProvider(
        ILogger<KokoroEngineProvider> logger,
        IStoragePathProvider storage,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.storage = storage;
        this.httpClientFactory = httpClientFactory;
    }

    public async Task<KokoroTTS> GetEngineAsync(CancellationToken cancellationToken = default)
    {
        if (engine is not null)
        {
            return engine;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (engine is not null)
            {
                return engine;
            }

            string modelPath = Path.Combine(storage.TtsCacheDirectory, ModelFileName);
            if (!File.Exists(modelPath))
            {
                logger.LogInformation("Kokoro model not present at {ModelPath}; downloading (~320 MB) ...", modelPath);
                await DownloadModelAsync(modelPath, cancellationToken);
                logger.LogInformation("Kokoro model downloaded to {ModelPath}", modelPath);
            }

            // Voice loading must happen on a thread where the working directory contains the
            // bundled "voices" folder; since that folder is copied to the app's base directory
            // by the NuGet package targets, we load by absolute path so we don't depend on
            // wherever the worker happened to start from.
            EnsureVoicesLoaded();

            engine = KokoroTTS.LoadModel(modelPath);
            return engine;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public IReadOnlyList<KokoroVoice> GetVoices()
    {
        EnsureVoicesLoaded();
        return KokoroVoiceManager.Voices;
    }

    public KokoroVoice? FindVoice(string name)
    {
        EnsureVoicesLoaded();
        return KokoroVoiceManager.Voices
            .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureVoicesLoaded()
    {
        if (voicesLoaded)
        {
            return;
        }

        lock (voicesLock)
        {
            if (voicesLoaded)
            {
                return;
            }

            string voicesDir = Path.Combine(AppContext.BaseDirectory, "voices");
            if (!Directory.Exists(voicesDir))
            {
                logger.LogWarning(
                    "Kokoro voices directory '{VoicesDir}' does not exist. Voice list will be empty until the KokoroSharp.CPU package's content is restored.",
                    voicesDir);
                voicesLoaded = true;
                return;
            }

            KokoroVoiceManager.LoadVoicesFromPath(voicesDir);
            voicesLoaded = true;

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Loaded {Count} Kokoro voices from {VoicesDir}",
                    KokoroVoiceManager.Voices.Count, voicesDir);
            }
        }
    }

    private async Task DownloadModelAsync(string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string tempPath = targetPath + ".part";

        var client = httpClientFactory.CreateClient();
        // The model file is ~320 MB; the default 100s timeout is far too short.
        client.Timeout = TimeSpan.FromMinutes(30);

        using (var response = await client.GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await responseStream.CopyToAsync(fileStream, 81920, cancellationToken);
        }

        // Atomic move so a crash during download doesn't leave a half-written model behind
        // that subsequent runs would happily try to load.
        File.Move(tempPath, targetPath, overwrite: true);
    }

    public void Dispose()
    {
        engine?.Dispose();
        loadLock.Dispose();
    }
}