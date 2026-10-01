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
        GpgPathBox.Text = AppServices.Settings.GpgExecutablePath;
        GpgHomeBox.Text = AppServices.Settings.GpgHomeDirectory ?? string.Empty;
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

        AboutText.Text = Loc.Get("Settings_AboutText") + "\n" +
                         Loc.Get("Settings_LogFilePrefix") + AppServices.LogPath;

        if (AppServices.Gpg.IsAvailable)
        {
            try
            {
                GpgVersionText.Text = AppServices.Gpg.GetVersion();
            }
            catch (Exception ex)
            {
                GpgVersionText.Text = Loc.Get("Settings_CannotReadVersionPrefix") + ex.Message;
            }

            GpgOriginText.Text = AppServices.Gpg.IsBundled
                ? Loc.Get("Settings_GpgBundled") + AppServices.Gpg.ExecutablePath + Loc.Get("Settings_GpgBundledSuffix")
                : Loc.Get("Settings_GpgSystem") + AppServices.Gpg.ExecutablePath + Loc.Get("Settings_GpgSystemSuffix");
        }
        else
        {
            GpgVersionText.Text = Loc.Get("Settings_GpgNotFound");
            GpgOriginText.Text = Loc.Get("Settings_GpgNotFoundHint");
        }
    }

    private async void OnBrowseGpgClick(object sender, RoutedEventArgs e)
    {
        string? path = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_ChooseGpgExe"), ".exe");
        if (path is not null)
        {
            GpgPathBox.Text = path;
        }
    }

    private async void OnBrowseHomeClick(object sender, RoutedEventArgs e)
    {
        string? path = await FilePickerHelper.PickFolderAsync();
        if (path is not null)
        {
            GpgHomeBox.Text = path;
        }
    }

    private void OnDetectGpgClick(object sender, RoutedEventArgs e)
    {
        string? detected = GpgService.AutoDetect();
        GpgPathBox.Text = detected ?? string.Empty;
        if (detected is null)
        {
            _ = DialogService.ShowMessageAsync(Loc.Get("Settings_AutoDetect"), Loc.Get("Settings_AutoDetectFailed"));
        }
    }

    private void OnSaveGpgClick(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.GpgExecutablePath = GpgPathBox.Text?.Trim() ?? string.Empty;
        AppServices.Settings.GpgHomeDirectory = string.IsNullOrWhiteSpace(GpgHomeBox.Text) ? null : GpgHomeBox.Text.Trim();
        AppServices.Settings.Save();
        AppServices.ReinitializeGpg();
        UpdateStatus();
    }

    private async void OnCheckGpgClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string version = await Task.Run(() => AppServices.Gpg.GetVersion());
            await DialogService.ShowMessageAsync(Loc.Get("Settings_GpgTestTitle"), Loc.Get("Settings_GpgTestOkPrefix") + version);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Settings_GpgTestFailed"), ex);
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
