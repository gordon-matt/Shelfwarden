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

        /// <summary>
        /// Reverses "Last, First" author names to natural order (e.g. "Smith, John" → "John Smith").
        /// Names without a comma are returned trimmed and unchanged.
        /// </summary>
        public string NormalizeAuthorName()
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            int commaIndex = trimmed.IndexOf(',');
            if (commaIndex < 0)
            {
                return trimmed;
            }

            string family = trimmed[..commaIndex].Trim();
            string given = trimmed[(commaIndex + 1)..].Trim();
            return given.Length == 0 ? trimmed : $"{given} {family}";
        }
    }
}