namespace PlaymoxAPI.Extensions;
public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static string IsNull(this string o, string fallback)
    {
        return o != null ? o : fallback;
    }
    public static bool IsNullOrEmpty(this string value)
    {
        return string.IsNullOrEmpty(value);
    }
    public static bool IsNullOrWhiteSpace(this string value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    public static string LeadingZeros(this string source, int length, char symbol = '0')
    {
        return source.PadLeft(length, symbol);
    }
}
