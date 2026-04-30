using KokoroSharp;
using KokoroSharp.Core;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Lazy holder for the (expensive) <see cref="KokoroTTS"/> ONNX model. The first call to
/// <see cref="GetEngineAsync(CancellationToken)"/> downloads the model bytes (~320 MB) into
/// the TTS cache directory and instantiates the engine; subsequent calls reuse the same
/// instance. Singleton lifetime — the underlying model takes hundreds of megabytes of RAM
/// and is fully thread-safe through <see cref="KokoroEngine"/>'s background dispatcher.
/// </summary>
public interface IKokoroEngineProvider
{
    /// <summary>
    /// Ensures the model is loaded and returns the engine. Safe to call from multiple jobs;
    /// concurrent callers share the same load task.
    /// </summary>
    Task<KokoroTTS> GetEngineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// All voices currently loaded by <see cref="KokoroVoiceManager"/>. Not async because the
    /// voice list is preloaded by the package targets — but lazy on first use so we
    /// transparently kick off the voice load if it hasn't happened yet.
    /// </summary>
    IReadOnlyList<KokoroVoice> GetVoices();

    /// <summary>Look up a single voice by its canonical name (e.g. <c>"af_heart"</c>); null when not found.</summary>
    KokoroVoice? FindVoice(string name);
}
