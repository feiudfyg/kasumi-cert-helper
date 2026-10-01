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

    public MainWindow()
    {
        InitializeComponent();
        ApplyStartupSize();
        Closed += OnClosed;
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
        AppServices.Log($"窗口: workArea={work.Width}x{work.Height} size={width}x{height} pos={x},{y}");
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
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
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
