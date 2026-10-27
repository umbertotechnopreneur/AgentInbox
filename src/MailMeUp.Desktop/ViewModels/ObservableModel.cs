/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Shared property-change notifications for UI-independent observable state.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Notifies bindings without depending on WinUI controls or a UI framework.</summary>
internal abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Update a changed field and notify bindings and dependent model behavior.</summary>
    /// <param name="field">Backing field updated only when the value changes.</param>
    /// <param name="value">Model value or text consumed by this control or binding.</param>
    /// <param name="property">Observable property name or native dependency property being updated.</param>
    /// <typeparam name="T">Type of the observable backing field.</typeparam>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(property);
        Changed(property);
        return true;
    }

    /// <summary>Notify observers of one property or, with an empty name, all properties.</summary>
    /// <param name="property">Observable property name or native dependency property being updated.</param>
    protected void Notify(string property = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    /// <summary>React to a model property change while preserving batched update behavior.</summary>
    /// <param name="property">Observable property name or native dependency property being updated.</param>
    protected virtual void Changed(string property) { }
}
