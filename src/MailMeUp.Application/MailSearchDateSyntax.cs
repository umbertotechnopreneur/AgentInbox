using System.Globalization;

namespace MailMeUp.Application;

internal static class MailSearchDateSyntax
{
    public static bool HasExplicitDate(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        // Tokenize without looking inside quoted phrases, other fields, or escaped literals.
        var position = 0;
        while (position < query.Length)
        {
            while (position < query.Length && IsSeparator(query[position]))
            {
                position++;
            }

            var tokenStart = position;
            var quoted = false;
            while (position < query.Length)
            {
                var current = query[position];
                if (current == '\\' && position + 1 < query.Length)
                {
                    position += 2;
                    continue;
                }

                if (current == '"')
                {
                    quoted = !quoted;
                }
                else if (!quoted && IsSeparator(current))
                {
                    break;
                }

                position++;
            }

            var token = query[tokenStart..position].TrimStart('-', '+');
            var operatorIndex = token.IndexOfAny([':', '<', '>', '=']);
            if (operatorIndex <= 0)
            {
                continue;
            }

            var field = token[..operatorIndex].ToLowerInvariant();
            var usesColon = token[operatorIndex] == ':';
            if (!usesColon && field is not ("received" or "sent"))
            {
                continue;
            }

            var value = token[(operatorIndex + (usesColon ? 1 : 0))..].Trim('"');
            if (field is "newer_than" or "older_than")
            {
                if (value.Length > 1 && value[^1] is 'd' or 'm' or 'y' &&
                    int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0)
                {
                    return true;
                }
            }
            else if (field is "after" or "before" or "newer" or "older" or "received" or "sent")
            {
                // Native Graph date comparisons/ranges and Gmail absolute dates or Unix seconds.
                if (value.Split("..", StringSplitOptions.None).All(IsDateValue))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSeparator(char value) => char.IsWhiteSpace(value) || value is '(' or ')' or '{' or '}';

    private static bool IsDateValue(string value)
    {
        value = value.TrimStart('<', '>', '=');
        return value.Length > 0 &&
            (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 ||
             DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _) ||
             value.ToLowerInvariant() is "today" or "yesterday" or "thisweek" or "lastweek" or "thismonth" or "lastmonth" or "thisyear" or "lastyear");
    }
}
