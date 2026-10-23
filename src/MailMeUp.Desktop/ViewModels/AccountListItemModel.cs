// SPDX-License-Identifier: MIT

using MailMeUp.Desktop.Models;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns the transient presentation of a saved connection without changing its contract.</summary>
internal sealed class AccountListItemModel(SavedAccount account) : ObservableModel
{
    private bool _passed;
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
    public bool Passed { get => _passed; internal set { if (Set(ref _passed, value)) Notify(); } }
    public string CheckGlyph => Passed ? "\uE73E" : "";
    public string CheckDescription => Passed ? "Connection and sign-in passed in this session" : "Not checked in this run";
}
