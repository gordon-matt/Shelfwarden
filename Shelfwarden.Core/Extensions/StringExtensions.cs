namespace Shelfwarden.Extensions;

public static class StringExtensions
{
    extension(string? value)
    {
        public string? Or(string? defaultValue) => !string.IsNullOrEmpty(value) ? value : defaultValue;
    }
}