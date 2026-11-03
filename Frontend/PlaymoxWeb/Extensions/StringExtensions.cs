using System.Text.RegularExpressions;

namespace PlaymoxWeb.Extensions;

public static class StringExtensions
{
    // More robust regex for ANSI and OSC sequences
    private static readonly Regex AnsiRegex = new(
        @"\x1B\].*?(?:\x07|\x1B\\)|\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])|[\x00-\x07\x0B\x0C\x0E-\x1F]", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string StripAnsiEscapeCodes(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Map cursor left to backspace to simulate terminal behavior in our simple console
        input = input.Replace("\x1B[D", "\b");

        return AnsiRegex.Replace(input, string.Empty);
    }
}
