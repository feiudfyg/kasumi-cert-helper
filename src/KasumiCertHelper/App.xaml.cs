using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;

namespace KasumiCertHelper;

public partial class App : Application
{
    private static App? _instance;
    private MainWindow? _shell;

    public App()
    {
        InitializeComponent();
        _instance = this;
        UnhandledException += OnUnhandledException;
    }

    public static Window? MainWindow { get; private set; }

    /// <summary>The page a fresh shell should open, so a language change does not lose the user's place.</summary>
    public static string? CurrentPageTag { get; set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        ShowShell();
    }

    /// <summary>
    /// Switches the interface language. Every XAML string is resolved while a page is parsed, so the
    /// shell is rebuilt instead of trying to refresh the live visual tree.
    /// </summary>
    public static void SetLanguage(string? language)
    {
        AppServices.Settings.Language = string.IsNullOrWhiteSpace(language) ? null : language;
        AppServices.Settings.Save();
        Loc.SetCulture(AppServices.Settings.Language ?? Loc.SystemCulture);

        _instance?.RebuildShell();
    }

    private void ShowShell()
    {
        var window = new MainWindow();
        _shell = window;
        MainWindow = window;
        window.Activate();
    }

    private void RebuildShell()
    {
        MainWindow? previous = _shell;
        CurrentPageTag = previous?.CurrentTag ?? CurrentPageTag;

        // The new window is shown before the old one closes, otherwise the last window closing
        // would shut the application down.
        ShowShell();
        previous?.Close();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppServices.Log("UnhandledException: " + e.Exception);
    }
}
