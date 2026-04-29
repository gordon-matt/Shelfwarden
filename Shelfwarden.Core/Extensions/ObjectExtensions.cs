namespace Shelfwarden.Extensions;

public static class ObjectExtensions
{
    extension<T>(T? value)
    {
        public T? Or(T? defaultValue) => value is not null ? value : defaultValue;
    }
}