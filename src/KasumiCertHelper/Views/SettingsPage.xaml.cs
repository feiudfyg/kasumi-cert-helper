using System.Diagnostics;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using KasumiCertHelper.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>Resolves a resource key for XAML, see <c>{x:Bind T('Key')}</c>.</summary>
    public string T(string key) => Loc.Get(key);

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Culture code behind each entry of <see cref="LanguageBox"/>, <c>null</c> for "follow Windows".</summary>
    private readonly List<string?> _languages = new();

    private bool _languagesReady;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildLanguageList();
        UpdateStatus();
    }

    private void BuildLanguageList()
    {
        _languages.Clear();

        var items = new List<LanguageChoice>
        {
            new(null, Loc.Get("Settings_LanguageSystem")),
        };

        foreach (string culture in Loc.SupportedCultures)
        {
            items.Add(new LanguageChoice(culture, Loc.DisplayName(culture)));
        }

        LanguageBox.ItemsSource = items;
        LanguageBox.DisplayMemberPath = nameof(LanguageChoice.Display);

        string? current = string.IsNullOrWhiteSpace(AppServices.Settings.Language) ? null : Loc.ResolveCulture(AppServices.Settings.Language);
        int index = _languages.Count;
        for (int i = 0; i < items.Count; i++)
        {
            _languages.Add(items[i].Culture);
            if (string.Equals(items[i].Culture, current, StringComparison.Ordinal))
            {
                index = i;
            }
        }

        LanguageBox.SelectedIndex = index;
        _languagesReady = true;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languagesReady || LanguageBox.SelectedIndex < 0)
        {
            return;
        }

        string? culture = _languages[LanguageBox.SelectedIndex];
        string? configured = string.IsNullOrWhiteSpace(AppServices.Settings.Language) ? null : Loc.ResolveCulture(AppServices.Settings.Language);

        if (!string.Equals(culture, configured, StringComparison.Ordinal))
        {
            App.SetLanguage(culture);
        }
    }

    private sealed record LanguageChoice(string? Culture, string Display);

    private void UpdateStatus()
    {
        bool admin = ElevationHelper.IsAdministrator();
        ElevationText.Text = admin
            ? Loc.Get("Settings_AdminStatus")
            : Loc.Get("Settings_NotAdminStatus");

        DatabaseText.Text = string.IsNullOrWhiteSpace(AppServices.Settings.LastDatabasePath)
            ? Loc.Get("Settings_NoDatabaseYet")
            : Loc.Get("Settings_LastDatabasePrefix") + AppServices.Settings.LastDatabasePath;

        AboutText.Text = string.Join('\n',
            Loc.Get("Settings_AboutText"),
            Loc.Format("Settings_Version", ProductInfo.Version),
            Loc.Get("Settings_Copyright"),
            Loc.Get("Settings_LicenseLine"),
            Loc.Get("Settings_LogFilePrefix") + AppServices.LogPath);

        ComponentsText.Text = Loc.Get("Settings_AboutComponents");
        SourceText.Text = Loc.Get("Settings_SourceCodeLabel");
        SourceLinkButton.Content = ProductInfo.RepositoryUrl;

        PgpKeyCountText.Text = Loc.Format("Settings_PgpKeyCount", AppServices.Pgp.List().Count);
        PgpDirectoryText.Text = Loc.Get("Settings_PgpDirectoryPrefix") + AppServices.PgpDirectory;
    }

    private async void OnOpenSourceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProductInfo.RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Settings_OpenSourceFailed"), ex);
        }
    }

    /// <summary>Shows the third party notices that travel next to the executable.</summary>
    private async void OnOpenNoticesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, ProductInfo.NoticesFileName);
            if (!File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(ProductInfo.NoticesUrl) { UseShellExecute = true });
                return;
            }

            string text = await File.ReadAllTextAsync(path);
            await DialogService.ShowMessageAsync(Loc.Get("Settings_OpenNotices"), text);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Settings_OpenNotices"), ex);
        }
    }

    private async void OnOpenPgpFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppServices.PgpDirectory);
            Process.Start(new ProcessStartInfo(AppServices.PgpDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_OpenKeyFolderFailed"), ex);
        }
    }

    private async void OnElevateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Error_RestartElevatedFailed"), ex);
        }
    }

    private async void OnOpenCertMgrClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("certmgr.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Error_OpenCertMgrFailed"), ex);
        }
    }

    private async void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppServices.AppDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppServices.AppDataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Error_OpenLogFolderFailed"), ex);
        }
    }

    private void OnOpenAppDataClick(object sender, RoutedEventArgs e)
    {
        OnOpenLogFolderClick(sender, e);
    }
}
