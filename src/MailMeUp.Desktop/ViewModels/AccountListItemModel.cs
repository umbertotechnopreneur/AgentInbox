// SPDX-License-Identifier: MIT

using MailMeUp.Desktop.Models;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns the transient presentation of a saved connection without changing its contract.</summary>
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
    internal void ResetCheck()
    {
        _passed = false; _failed = false; _cancelled = false; _details = "";
        Notify();
    }
    internal void RecordCheck(bool succeeded, bool cancelled, string details)
    {
        _passed = succeeded; _cancelled = cancelled; _failed = !succeeded && !cancelled;
        _details = details;
        Notify();
    }
}
