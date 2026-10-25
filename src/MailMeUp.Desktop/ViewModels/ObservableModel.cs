// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Notifies bindings without depending on WinUI controls or a UI framework.</summary>
internal abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(property);
        Changed(property);
        return true;
    }

    protected void Notify(string property = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    protected virtual void Changed(string property) { }
}
