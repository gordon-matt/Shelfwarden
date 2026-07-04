using Shelfwarden.Services.Tts;

namespace Shelfwarden.Tests;

public class TextChunkerTests
{
    [Fact]
    public void ChunkContinuous_joins_sentence_split_across_blocks()
    {
        // Page 1 ends mid-sentence (no terminal punctuation); page 2 completes it.
        var blocks = new[]
        {
            "The morning was cold and the harbour was still. She walked to the edge of the water and",
            "looked out at the grey horizon. Then she turned back home.",
        };

        var chunks = TextChunker.ChunkContinuous(blocks).ToList();

        // The broken sentence must be reassembled into a single chunk, not split at the page boundary.
        Assert.Contains(chunks, c => c.Contains("edge of the water and looked out at the grey horizon."));
        Assert.DoesNotContain(chunks, c => c.TrimEnd().EndsWith("water and"));
    }

    [Fact]
    public void ChunkContinuous_keeps_break_when_block_ends_a_sentence()
    {
        var blocks = new[]
        {
            "First complete sentence.",
            "Second complete sentence.",
        };

        var chunks = TextChunker.ChunkContinuous(blocks).ToList();

        // Each self-contained block terminates its own chunk (natural pause preserved).
        Assert.Equal(2, chunks.Count);
        Assert.Equal("First complete sentence.", chunks[0]);
        Assert.Equal("Second complete sentence.", chunks[1]);
    }

    [Fact]
    public void ChunkContinuous_flushes_trailing_incomplete_sentence()
    {
        var blocks = new[] { "A dangling final line with no period" };

        var chunks = TextChunker.ChunkContinuous(blocks).ToList();

        Assert.Single(chunks);
        Assert.Equal("A dangling final line with no period", chunks[0]);
    }
}
