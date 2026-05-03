namespace Shelfwarden.Infrastructure;

/// <summary>
/// Builds user-visible strings from <see cref="Result{T}"/> — Ardalis puts validation
/// failures in <see cref="Result{T}.ValidationErrors"/> while other statuses use
/// <see cref="Result{T}.Errors"/>. Using only <c>Errors.FirstOrDefault()</c> misses validation
/// text and can yield null-coalescing pitfalls when the first error is whitespace or empty.
/// </summary>
internal static class ResultMessages
{
    public static string UserFacing<T>(Result<T> result, string fallback)
    {
        string? fromErrors = result.Errors?
            .OfType<string>()
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(fromErrors))
            return fromErrors.Trim();

        string validation = string.Join("; ", result.ValidationErrors
            .Select(v => v.ErrorMessage)
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(validation))
            return validation;

        return fallback;
    }
}
