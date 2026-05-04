namespace Shelfwarden.Models;

/// <summary>
/// Shared letter-rail options and client-side matching for title / sort-title prefix rules
/// that mirror server-side book search filtering.
/// </summary>
public static class BookListLetterFilter
{
    public static readonly (string Value, string Label)[] Options =
    [
        (string.Empty, "All"),
        ("#", "#"),
        ("A", "A"), ("B", "B"), ("C", "C"), ("D", "D"), ("E", "E"), ("F", "F"), ("G", "G"),
        ("H", "H"), ("I", "I"), ("J", "J"), ("K", "K"), ("L", "L"), ("M", "M"), ("N", "N"),
        ("O", "O"), ("P", "P"), ("Q", "Q"), ("R", "R"), ("S", "S"), ("T", "T"), ("U", "U"),
        ("V", "V"), ("W", "W"), ("X", "X"), ("Y", "Y"), ("Z", "Z"),
    ];

    /// <summary>
    /// Returns whether the book matches the rail filter: non-empty title or sort title starting
    /// with the letter (case-insensitive), or <c>#</c> for a non-letter first character on either field.
    /// </summary>
    public static bool MatchesTitleOrSortTitle(BookListItemDto book, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        string f = filter.Trim();
        if (f == "#")
        {
            return TitleOrSortStartsWithNonLetter(book);
        }

        if (f.Length == 1 && char.IsLetter(f[0]))
        {
            return StartsWithLetter(book.Title, f) || StartsWithLetter(book.SortTitle, f);
        }

        return false;
    }

    private static bool TitleOrSortStartsWithNonLetter(BookListItemDto book)
    {
        bool titleHit = !string.IsNullOrEmpty(book.Title) && !char.IsLetter(book.Title[0]);
        bool sortHit = !string.IsNullOrEmpty(book.SortTitle) && !char.IsLetter(book.SortTitle![0]);
        return titleHit || sortHit;
    }

    private static bool StartsWithLetter(string? text, string letter)
    {
        return !string.IsNullOrEmpty(text) &&
               text.StartsWith(letter, StringComparison.OrdinalIgnoreCase);
    }
}
