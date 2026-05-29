using Shelfwarden.Models;
using Shelfwarden.Services.Tts;

namespace Shelfwarden.Tests;

public class SectionClassifierTests
{
    [Theory]
    [InlineData("Copyright", SectionKind.FrontMatter)]
    [InlineData("Table of Contents", SectionKind.FrontMatter)]
    [InlineData("Contents", SectionKind.FrontMatter)]
    [InlineData("Dedication", SectionKind.FrontMatter)]
    [InlineData("Preface", SectionKind.FrontMatter)]
    [InlineData("Acknowledgements", SectionKind.FrontMatter)]
    [InlineData("Also by the Author", SectionKind.FrontMatter)]
    [InlineData("Index", SectionKind.BackMatter)]
    [InlineData("Bibliography", SectionKind.BackMatter)]
    [InlineData("Appendix A", SectionKind.BackMatter)]
    [InlineData("About the Publisher", SectionKind.BackMatter)]
    [InlineData("Chapter 1: The Beginning", SectionKind.Chapter)]
    [InlineData("The Lighthouse", SectionKind.Chapter)]
    [InlineData("", SectionKind.Chapter)]
    [InlineData(null, SectionKind.Chapter)]
    public void Classify_maps_titles_to_kinds(string? title, SectionKind expected)
    {
        Assert.Equal(expected, SectionClassifier.Classify(title));
    }

    [Fact]
    public void Classify_does_not_treat_a_single_keyword_inside_a_longer_word_as_a_match()
    {
        // "indexing" contains "index" but is not an index section.
        Assert.Equal(SectionKind.Chapter, SectionClassifier.Classify("Indexing the Web"));
    }

    [Theory]
    [InlineData("Chapter 1", true)]
    [InlineData("Chapter One", true)]
    [InlineData("CHAPTER XII", true)]
    [InlineData("Part III", true)]
    [InlineData("1. Introduction", true)]
    [InlineData("Prologue", true)]
    [InlineData("Epilogue", true)]
    [InlineData("Just some body text that wraps onto the page", false)]
    [InlineData("", false)]
    public void LooksLikeChapterHeading_detects_headings(string text, bool expected)
    {
        Assert.Equal(expected, SectionClassifier.LooksLikeChapterHeading(text));
    }

    [Fact]
    public void ApplyDefaults_includes_only_body_chapters()
    {
        var sections = new List<BookSection>
        {
            new() { Title = "Copyright" },
            new() { Title = "Chapter 1" },
            new() { Title = "Chapter 2" },
            new() { Title = "Index" },
        };

        SectionClassifier.ApplyDefaults(sections);

        Assert.False(sections[0].IsIncluded);
        Assert.Equal(SectionKind.FrontMatter, sections[0].Kind);
        Assert.True(sections[1].IsIncluded);
        Assert.True(sections[2].IsIncluded);
        Assert.False(sections[3].IsIncluded);
        Assert.Equal(SectionKind.BackMatter, sections[3].Kind);
    }
}
