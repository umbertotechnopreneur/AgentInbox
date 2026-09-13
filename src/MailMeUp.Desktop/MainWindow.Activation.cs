using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private SetupStep? _pendingStep;

    /// <summary>Requests a page without refreshing accounts, signing in or checking provider access.</summary>
    internal void RequestStep(SetupStep? step)
    {
        // A regular launch should only activate the current window, preserving its current page.
        if (step is null) return;
        _pendingStep = step;
        ApplyPendingStep();
    }

    private void ApplyPendingStep()
    {
        if (!_loaded || _busy || _dialogOpen || _lifetime.IsCancellationRequested || _pendingStep is not { } requested)
            return;

        _pendingStep = null;
        if (_step == (int)requested || !CanLeaveSharing()) return;

        // Direct navigation does not mark earlier setup stages as completed.
        ShowStep((int)requested);
        Steps.SelectedIndex = _step;
    }
}
