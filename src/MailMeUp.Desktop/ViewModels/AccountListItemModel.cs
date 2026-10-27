/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Saved-account list presentation and current-session check results.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.Models;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns the transient presentation of a saved connection without changing its contract.</summary>
/// <param name="account">Public saved-account snapshot; null starts an empty editor draft.</param>
internal sealed class AccountListItemModel(SavedAccount account) : ObservableModel
{
    private bool _passed;
    private bool _failed;
    private bool _cancelled;
    private string _details = "";
    public SavedAccount Account { get; } = account;
    public string Label => Account.Label;
    public string Email => Account.Email;
    public string? ProviderLogo => Account.Preset switch
    {
        "gmail" => "https://www.google.com/s2/favicons?domain=gmail.com&sz=64",
        "icloud" => "https://www.google.com/s2/favicons?domain=icloud.com&sz=64",
        "outlook" or "microsoft365" => "https://www.google.com/s2/favicons?domain=outlook.com&sz=64",
        _ => null
    };
    public bool Passed => _passed;
    public bool HasCheckResult => _details.Length > 0;
    public string CheckGlyph => Passed ? "\uE73E" : _failed ? "\uE711" : _cancelled ? "\uE7BA" : "";
    public int CheckTone => Passed ? 1 : _failed ? 3 : _cancelled ? 2 : 0;
    public string CheckDescription => _details.Length > 0 ? _details : "Not checked in this run";
    /// <summary>Clear transient connection-check state without changing saved settings.</summary>
    internal void ResetCheck()
    {
        _passed = false; _failed = false; _cancelled = false; _details = "";
        Notify();
    }
    /// <summary>Retain the current session check outcome and bounded local presentation details.</summary>
    /// <param name="succeeded">Whether the requested operation completed successfully within its stated scope.</param>
    /// <param name="cancelled">Whether the user or configuration-window lifetime cancelled this check.</param>
    /// <param name="details">Bounded local check explanation retained only for this list session.</param>
    internal void RecordCheck(bool succeeded, bool cancelled, string details)
    {
        _passed = succeeded; _cancelled = cancelled; _failed = !succeeded && !cancelled;
        _details = details;
        Notify();
    }
}
