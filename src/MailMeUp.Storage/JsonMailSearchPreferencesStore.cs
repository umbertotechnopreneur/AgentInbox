// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Text.Json;
using MailMeUp.Core;

namespace MailMeUp.Storage;

/// <summary>Stores non-secret mail search defaults atomically and reloads them on every read.</summary>
public sealed class JsonMailSearchPreferencesStore(string directory) : IMailSearchPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true
    };

    private readonly string _directory = DataDirectory.Resolve(directory);

    /// <inheritdoc />
    public async Task<MailSearchPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try
        {
            stream = new FileStream(Path.Combine(_directory, "search-preferences.json"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (FileNotFoundException)
        {
            return new MailSearchPreferences();
        }
        catch (DirectoryNotFoundException)
        {
            return new MailSearchPreferences();
        }

        await using (stream)
        {
            if (stream.Length > 16_384)
            {
                throw new InvalidOperationException("Mail search preferences are too large.");
            }

            var document = await JsonSerializer.DeserializeAsync<PreferencesDocument>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Mail search preferences are invalid.");
            if (document.SchemaVersion != 1)
            {
                throw new InvalidOperationException("Mail search preferences have an unsupported version.");
            }

            var preferences = new MailSearchPreferences(document.DefaultLookbackDays);
            preferences.Validate();
            return preferences;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_directory);
        }
        else
        {
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporaryPath = Path.Combine(_directory, $"search-preferences-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath,
                JsonSerializer.Serialize(new PreferencesDocument(1, preferences.DefaultLookbackDays), JsonOptions), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, Path.Combine(_directory, "search-preferences.json"), true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PreferencesDocument(int SchemaVersion, int DefaultLookbackDays);
}
