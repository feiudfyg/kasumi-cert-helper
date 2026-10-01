using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;

namespace KasumiCertHelper;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppServices.Log("UnhandledException: " + e.Exception);
    }
}
