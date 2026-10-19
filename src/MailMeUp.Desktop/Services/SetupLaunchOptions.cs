// SPDX-License-Identifier: MIT

namespace MailMeUp.Desktop.Services;

/// <summary>Contains a validated optional outgoing-message review request.</summary>
internal sealed record SetupLaunchOptions(string? ReviewRequestId = null)
{
    internal const string UsageError = "Invalid arguments. Open AgentInbox without arguments or use --review REQUEST_ID.";

    internal static bool TryParse(IReadOnlyList<string> arguments, out SetupLaunchOptions options)
    {
        options = new();
        if (arguments.Count == 0) return true;
        if (arguments.Count != 2 || arguments[0] != "--review") return false;
        var id = arguments[1];
        if (id.Length is < 1 or > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            return false;
        options = new(id);
        return true;
    }

    internal static bool TryParseActivation(IReadOnlyList<string> arguments, out SetupLaunchOptions options)
    {
        if (arguments.Count > 0)
        {
            var first = arguments[0];
            var fileName = first[(first.LastIndexOfAny(['\\', '/']) + 1)..];
            if (string.Equals(fileName, "AgentInbox.Desktop.exe", StringComparison.OrdinalIgnoreCase))
                return TryParse(arguments.Skip(1).ToArray(), out options);
        }
        return TryParse(arguments, out options);
    }
}
