using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace MailMeUp.Desktop;

/// <summary>Shows product information and user-initiated website, support and GitHub links.</summary>
public sealed partial class AboutDialog : ContentDialog
{
    private readonly string _version;
    private readonly string _buildDetails;

    /// <summary>Creates the About and Support dialog without opening external links.</summary>
    public AboutDialog()
    {
        InitializeComponent();
        _version = GetVersion();
        _buildDetails = GetBuildDetails();
        VersionText.Text = $"Version {_version} · Windows preview{Environment.NewLine}{_buildDetails}";
    }

    private static string GetVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            return (typeof(AboutDialog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0] ?? "Unknown") + " (unpackaged)";
        }
    }

    private void CopyVersionButton_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText($"AgentInbox {_version}{Environment.NewLine}{_buildDetails}{Environment.NewLine}Windows {Environment.OSVersion.Version} · {RuntimeInformation.ProcessArchitecture}");
        try
        {
            Clipboard.SetContent(data);
            CopyStatus.Text = "App version copied. Paste it into your GitHub issue.";
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            CopyStatus.Text = "The clipboard is unavailable. Include the version shown above in your GitHub issue.";
        }

        CopyStatus.Visibility = Visibility.Visible;
    }

    private static string GetBuildDetails()
    {
        var metadata = typeof(AboutDialog).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? string.Empty, StringComparer.Ordinal);
        var builtAt = DateTimeOffset.ParseExact(metadata["BuildDateLocal"], "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None);
        return $"Build: {builtAt:yyyy-MM-dd HH:mm:ss zzz} ({metadata["BuildTimeZone"]}){Environment.NewLine}Git commit: {metadata["BuildGitCommit"]}";
    }
}
