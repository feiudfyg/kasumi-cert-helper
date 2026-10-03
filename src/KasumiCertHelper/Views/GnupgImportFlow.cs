using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Views;

/// <summary>
/// The "import the system GnuPG keyring" flow, shared by the OpenPGP page and the settings page: it
/// shows what was found, imports the keys and reports the outcome.
/// </summary>
internal static class GnupgImportFlow
{
    /// <summary>
    /// Runs the flow and returns the fingerprints that were imported or refreshed, so the caller can
    /// select them in its list. Empty when the user cancelled or nothing was found.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunAsync(OpenPgpKeyStore store)
    {
        GnupgKeyringInfo info = GnupgKeyring.Inspect();

        var includeSecret = new CheckBox
        {
            Content = Loc.Get("Gpg_IncludeSecretKeys"),
            IsEnabled = info.Executable is not null,
        };
        AutomationProperties.SetAutomationId(includeSecret, "GnupgIncludeSecret");

        var passphrase = new PasswordBox
        {
            Header = Loc.Get("Gpg_GnupgPassphrase"),
            PlaceholderText = Loc.Get("Gpg_GnupgPassphraseHint"),
            IsEnabled = false,
        };
        AutomationProperties.SetAutomationId(passphrase, "GnupgPassphrase");
        includeSecret.Checked += (_, _) => passphrase.IsEnabled = true;
        includeSecret.Unchecked += (_, _) => passphrase.IsEnabled = false;

        var content = new StackPanel { Spacing = 10, MinWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = Loc.Format("Gpg_GnupgKeyringLine", info.Home ?? Loc.Get("Common_None")),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        });
        content.Children.Add(new TextBlock
        {
            Text = info.Executable is null
                ? Loc.Get("Gpg_GnupgExecutableMissing")
                : Loc.Format("Gpg_GnupgExecutableLine", info.Executable + (info.Version is null ? string.Empty : $" ({info.Version})")),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontSize = 12,
        });
        content.Children.Add(includeSecret);
        content.Children.Add(passphrase);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Gpg_ImportGnupgTitle"),
            Content = content,
            PrimaryButtonText = Loc.Get("Common_Ok"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return Array.Empty<string>();
        }

        bool withSecrets = includeSecret.IsChecked == true;
        string? secret = withSecrets ? passphrase.Password : null;

        GnupgImportOutcome outcome = await Task.Run(
            () => GnupgKeyring.Import(store, info.Home, withSecrets, secret));

        await ShowOutcomeAsync(outcome);
        return outcome.Result.Imported.Concat(outcome.Result.Updated).Distinct(StringComparer.Ordinal).ToList();
    }

    private static async Task ShowOutcomeAsync(GnupgImportOutcome outcome)
    {
        int total = outcome.Result.ImportedCount + outcome.Result.UpdatedCount;
        var report = new GpgOperationReport("import-gnupg")
        {
            Title = outcome.FoundSomething
                ? Loc.Format("Gpg_GnupgImported", total)
                : Loc.Get("Gpg_ImportGnupgTitle"),
            Success = outcome.FoundSomething,
            Severity = outcome.Success
                ? outcome.FoundSomething ? GpgReportSeverity.Success : GpgReportSeverity.Warning
                : GpgReportSeverity.Error,
        };

        if (outcome.Home is not null)
        {
            report.With(Loc.Get("Gpg_GnupgLabel"), outcome.Home);
        }

        if (outcome.UsedBridge && outcome.Executable is not null)
        {
            report.With("gpg.exe", outcome.Executable);
        }

        report.With(Loc.Get("Gpg_Row_Imported"), Loc.Format("Gpg_Value_Count", outcome.Result.ImportedCount));
        if (outcome.Result.UpdatedCount > 0)
        {
            report.With(Loc.Get("Gpg_Row_Unchanged"), Loc.Format("Gpg_Value_Count", outcome.Result.UpdatedCount));
        }

        if (outcome.Result.SkippedCount > 0)
        {
            report.With(Loc.Get("Gpg_Row_NotImported"), Loc.Format("Gpg_Value_Count", outcome.Result.SkippedCount), GpgReportSeverity.Warning);
        }

        if (outcome.Failed > 0)
        {
            report.With(Loc.Get("Gpg_GnupgFailedRow"), Loc.Format("Gpg_Value_Count", outcome.Failed), GpgReportSeverity.Warning);
        }

        if (outcome.SecretKeysRequested)
        {
            report.With(
                Loc.Get("Gpg_GnupgSecretRow"),
                Loc.Format("Gpg_Value_Count", outcome.SecretKeysImported),
                outcome.SecretKeysImported > 0 ? GpgReportSeverity.Info : GpgReportSeverity.Warning);
        }

        if (outcome.Error is not null)
        {
            report.Note(outcome.Error);
        }

        if (outcome.Failed > 0)
        {
            report.Note(Loc.Format("Gpg_GnupgKeyTooNew", outcome.Failed));
        }

        if (outcome.SecretKeysFailed > 0)
        {
            report.Note(Loc.Get("Gpg_GnupgSecretFailed"));
        }

        if (!outcome.FoundSomething && outcome.Error is null)
        {
            report.Note(Loc.Get("Gpg_GnupgNothing"));
        }

        var dialog = new ContentDialog
        {
            Title = report.Title,
            Content = ResultPresenter.Create(report),
            CloseButtonText = Loc.Get("Common_Ok"),
        };
        await DialogService.ShowAsync(dialog);
    }
}
