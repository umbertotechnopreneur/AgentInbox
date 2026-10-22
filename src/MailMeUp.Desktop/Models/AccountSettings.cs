// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailMeUp.Desktop.Models;

/// <summary>The non-secret account contract shared with the Python management API.</summary>
internal record AccountSettings
{
    [JsonPropertyName("id"), JsonRequired] public string Id { get; init; } = "";
    [JsonPropertyName("label"), JsonRequired] public string Label { get; init; } = "";
    [JsonPropertyName("email"), JsonRequired] public string Email { get; init; } = "";
    [JsonPropertyName("preset"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Preset { get; init; }
    [JsonPropertyName("authentication"), JsonRequired] public AccountAuthentication Authentication { get; init; } = new();
    [JsonPropertyName("incoming"), JsonRequired] public MailEndpoint Incoming { get; init; } = new();
    [JsonPropertyName("smtp")] public MailEndpoint? Smtp { get; init; }
    [JsonPropertyName("read_enabled"), JsonRequired] public bool ReadEnabled { get; init; }
    [JsonPropertyName("send_enabled"), JsonRequired] public bool SendEnabled { get; init; }
    [JsonPropertyName("folders"), JsonRequired] public string[] Folders { get; init; } = ["INBOX"];
}

/// <summary>A saved account snapshot without credential references or credential values.</summary>
internal sealed record SavedAccount : AccountSettings
{
    [JsonPropertyName("revision"), JsonRequired] public int Revision { get; init; }
    [JsonPropertyName("microsoft_connected")] public bool MicrosoftConnected { get; init; }
    [JsonPropertyName("cleanup_pending")] public bool CleanupPending { get; init; }
    [JsonPropertyName("cleanup_pending_count")] public int CleanupPendingCount { get; init; }
    [JsonPropertyName("cleanup_error")] public string? CleanupError { get; init; }

    internal static SavedAccount Read(JsonElement value)
    {
        var account = value.Deserialize<SavedAccount>() ?? throw new JsonException("The engine returned no account.");
        if (string.IsNullOrEmpty(account.Id) || account.Revision < 1 || account.Authentication is null || account.Incoming is null
            || account.Authentication.Method is not ("password" or "microsoft_oauth")
            || account.Incoming.Protocol is not ("imap" or "pop3")
            || account.Preset is not (null or "custom" or "gmail" or "icloud" or "outlook" or "microsoft365"))
            throw new JsonException("The engine returned an invalid account contract.");
        return account;
    }
}

/// <summary>A reading or sending endpoint; only reading endpoints carry a protocol.</summary>
internal sealed record MailEndpoint
{
    [JsonPropertyName("protocol"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Protocol { get; init; }
    [JsonPropertyName("host"), JsonRequired] public string Host { get; init; } = "";
    [JsonPropertyName("port"), JsonRequired] public int Port { get; init; }
    [JsonPropertyName("tls"), JsonRequired] public string Tls { get; init; } = "tls";
    [JsonPropertyName("username"), JsonRequired] public string Username { get; init; } = "";
}

/// <summary>Public sign-in settings; passwords and tokens never belong in this object.</summary>
internal sealed record AccountAuthentication
{
    [JsonPropertyName("method"), JsonRequired] public string Method { get; init; } = "password";
    [JsonPropertyName("client_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ClientId { get; init; }
    [JsonPropertyName("tenant"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tenant { get; init; }
}

/// <summary>One captured edit operation; temporary passwords are cleared after use.</summary>
internal sealed record AccountSnapshot(AccountSettings Account, Dictionary<string, string> Passwords, int? Revision)
{
    internal void ClearPasswords() => Passwords.Clear();
}

/// <summary>A field error that can be displayed by any configuration adapter.</summary>
internal sealed record AccountFieldError(string Property, string Message, int Tab);

/// <summary>Provider defaults returned by the same Python presets operation available to the CLI.</summary>
internal sealed record ProviderPreset
{
    [JsonPropertyName("label"), JsonRequired] public string Label { get; init; } = "";
    [JsonPropertyName("incoming"), JsonRequired] public Dictionary<string, EndpointPreset> Incoming { get; init; } = [];
    [JsonPropertyName("smtp")] public EndpointPreset? Smtp { get; init; }
    [JsonPropertyName("authentication_methods"), JsonRequired] public string[] AuthenticationMethods { get; init; } = [];
}

/// <summary>A server preset contains no user's email address or credentials.</summary>
internal sealed record EndpointPreset
{
    [JsonPropertyName("host"), JsonRequired] public string Host { get; init; } = "";
    [JsonPropertyName("port"), JsonRequired] public int Port { get; init; }
    [JsonPropertyName("tls"), JsonRequired] public string Tls { get; init; } = "";
}
