namespace Shelfwarden.Extensions;

public static class StringExtensions
{
    extension(string value)
    {
        public string ToSortTitle()
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return value;
            }

            // Ignore leading articles for sort-title (e.g. "The Hobbit" -> "Hobbit").
            string[] articles = ["a ", "an ", "the "];
            string lower = trimmed.ToLowerInvariant();
            foreach (string article in articles)
            {
                if (!lower.StartsWith(article, StringComparison.Ordinal))
                {
                    continue;
                }

                string withoutArticle = trimmed[article.Length..].TrimStart();
                return withoutArticle.Length == 0 ? trimmed : withoutArticle;
            }

            return trimmed;
        }
    }
}