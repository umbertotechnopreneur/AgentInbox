// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace MailMeUp.Desktop.Services;

/// <summary>Loads the public Microsoft application identity shipped with Windows setup.</summary>
internal sealed record DesktopSettings(string MicrosoftClientId)
{
    /// <summary>Rejects missing or invalid configuration before the account editor opens.</summary>
    internal static DesktopSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var clientIdText = document.RootElement.GetProperty("MicrosoftClientId").GetString();
        if (!Guid.TryParseExact(clientIdText, "D", out var clientId) || clientId == Guid.Empty)
            throw new InvalidOperationException("appsettings.json must contain a valid MicrosoftClientId.");

        return new DesktopSettings(clientId.ToString("D"));
    }
}
