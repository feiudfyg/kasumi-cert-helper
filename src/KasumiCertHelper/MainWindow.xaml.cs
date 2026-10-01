using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Services;
using KasumiCertHelper.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace KasumiCertHelper;

public sealed partial class MainWindow : Window
{
    private const string WidthKey = "Window.Width";
    private const string HeightKey = "Window.Height";
    private const string XKey = "Window.X";
    private const string YKey = "Window.Y";

    private string? _currentTag;

    public MainWindow()
    {
        InitializeComponent();
        Title = Loc.Get("App_Title");
        ApplyStartupSize();
        Closed += OnClosed;
        SelectInitialPage();
        Nav.Loaded += (_, _) => LocalizeSettingsItem();
    }

    public string T(string key) => Loc.Get(key);

    /// <summary>
    /// NavigationView renders its own settings label from the OS language, so it is replaced with the
    /// same table the rest of the interface uses.
    /// </summary>
    private void LocalizeSettingsItem()
    {
        if (Nav.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = Loc.Get("Nav_Settings");
        }
    }
    /// <summary>
    /// Navigation tag of the page currently shown, used when the shell is rebuilt. The settings page is
    /// tracked separately because NavigationView reports it through IsSettingsSelected instead of
    /// SelectedItem.
    /// </summary>
    public string? CurrentTag => _currentTag;

    private void SelectInitialPage()
    {
        string? tag = App.CurrentPageTag;

        if (tag == "settings")
        {
            // Assigning SelectedItem = SettingsItem does not navigate: NavigationView reports the
            // settings page through IsSettingsSelected instead of through SelectedItem.
            _currentTag = "settings";
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        foreach (object item in Nav.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string itemTag } && itemTag == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private SettingsService Settings => AppServices.Settings;

    /// <summary>
    /// Opens at a size that fits the actual work area, then remembers whatever the user resizes to.
    /// </summary>
    private void ApplyStartupSize()
    {
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = area.WorkArea;

        int width = (int)Settings.GetLayoutDouble(WidthKey, 0);
        int height = (int)Settings.GetLayoutDouble(HeightKey, 0);

        if (width < 900 || height < 600)
        {
            width = Math.Min(2200, (int)(work.Width * 0.80));
            height = Math.Min(1400, (int)(work.Height * 0.88));
        }

        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);

        AppWindow.Resize(new SizeInt32(width, height));

        int x = (int)Settings.GetLayoutDouble(XKey, int.MinValue);
        int y = (int)Settings.GetLayoutDouble(YKey, int.MinValue);

        bool visible = x != int.MinValue
                       && y != int.MinValue
                       && x + 200 >= work.X
                       && y + 100 >= work.Y
                       && x + 200 <= work.X + work.Width
                       && y + 60 <= work.Y + work.Height;

        if (!visible)
        {
            x = work.X + Math.Max(0, (work.Width - width) / 2);
            y = work.Y + Math.Max(0, (work.Height - height) / 2);
        }

        AppWindow.Move(new PointInt32(x, y));
        AppServices.Log(Loc.Format("App_Log_Window", work.Width, work.Height, width, height, x, y));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Settings.SetLayoutDouble(WidthKey, AppWindow.Size.Width);
        Settings.SetLayoutDouble(HeightKey, AppWindow.Size.Height);
        Settings.SetLayoutDouble(XKey, AppWindow.Position.X);
        Settings.SetLayoutDouble(YKey, AppWindow.Position.Y);
        Settings.Save();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type pageType;
        if (args.IsSettingsSelected)
        {
            pageType = typeof(SettingsPage);
            _currentTag = "settings";
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _currentTag = tag;
            pageType = tag switch
            {
                "x509" => typeof(X509Page),
                "gpg" => typeof(GpgPage),
                _ => typeof(StoresPage),
            };
        }
        else
        {
            return;
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
