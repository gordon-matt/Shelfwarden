using System.Text;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Pure helpers for breaking arbitrary book text into TTS-sized chunks. No DI, no IO — this
/// is a stateless utility used by <see cref="TtsJobService"/>.
/// </summary>
public static class TextChunker
{
    /// <summary>Default upper bound on chunk size (in characters). Roughly matches Kokoro's 510-token segment ceiling.</summary>
    public const int DefaultMaxChars = 250;

    /// <summary>
    /// Breaks <paramref name="text"/> on sentence-ending punctuation while preserving the
    /// punctuation in the returned strings. Whitespace is normalised. Empty fragments are
    /// dropped. The behaviour is deliberately simple — full NLP-grade segmentation isn't
    /// worth the dependency for TTS chunking.
    /// </summary>
    public static IEnumerable<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            sb.Append(c);

            if (c is '.' or '!' or '?' or '。' or '！' or '？' or '…')
            {
                // Look ahead for closing quote / parenthesis so we don't slice "Hello!" into
                // ["Hello!", "\""].
                while (i + 1 < text.Length && text[i + 1] is '"' or '\'' or '”' or '’' or ')' or ']')
                {
                    sb.Append(text[++i]);
                }

                string sentence = sb.ToString().Trim();
                if (sentence.Length > 0)
                {
                    yield return sentence;
                }
                sb.Clear();
            }
        }

        // Trailing text without a terminal punctuation — emit it anyway so we don't drop the
        // last paragraph of a chapter.
        string tail = sb.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    /// <summary>
    /// Group sentences into chunks not exceeding <paramref name="maxChars"/>. Sentences are
    /// kept whole; a sentence longer than the limit is force-split on whitespace. Adjacent
    /// short sentences combine into a single chunk so we don't drown the model in trivially
    /// tiny inferences.
    /// </summary>
    public static IEnumerable<string> BuildChunks(IEnumerable<string> sentences, int maxChars = DefaultMaxChars)
    {
        if (maxChars < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Chunks need to be at least 32 chars.");
        }

        var current = new StringBuilder();
        foreach (string sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                continue;
            }

            string s = sentence.Trim();
            if (s.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                foreach (string fragment in HardSplit(s, maxChars))
                {
                    yield return fragment;
                }
                continue;
            }

            // +1 for the joining space that's added when concatenating sentences.
            int projected = current.Length == 0 ? s.Length : current.Length + 1 + s.Length;
            if (projected > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }
            current.Append(s);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>
    /// Convenience: stream the chunks for a sequence of paragraphs / blocks. Each block is
    /// independently sentence-split before chunking, which means a paragraph end always
    /// terminates a chunk — handy because the resulting audio gets a natural pause there.
    /// </summary>
    public static IEnumerable<string> ChunkParagraphs(IEnumerable<string> paragraphs, int maxChars = DefaultMaxChars)
    {
        foreach (string paragraph in paragraphs)
        {
            foreach (string chunk in BuildChunks(SplitIntoSentences(paragraph), maxChars))
            {
                yield return chunk;
            }
        }
    }

    private static IEnumerable<string> HardSplit(string sentence, int maxChars)
    {
        // Walk the sentence word-by-word, packing as many as fit before emitting. The result
        // is preferable to a mid-word cut, which would mangle the prosody on the boundary.
        var sb = new StringBuilder();
        foreach (string word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int projected = sb.Length == 0 ? word.Length : sb.Length + 1 + word.Length;
            if (projected > maxChars && sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(word);

            if (sb.Length >= maxChars)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}