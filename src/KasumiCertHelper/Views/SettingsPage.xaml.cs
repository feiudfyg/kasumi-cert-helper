using System.Diagnostics;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        GpgPathBox.Text = AppServices.Settings.GpgExecutablePath;
        GpgHomeBox.Text = AppServices.Settings.GpgHomeDirectory ?? string.Empty;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool admin = ElevationHelper.IsAdministrator();
        ElevationText.Text = admin
            ? "当前以管理员身份运行，可以向「本地计算机」证书存储写入证书。"
            : "当前未以管理员身份运行。向「本地计算机」证书存储写入或删除证书时可能会失败，需要管理员权限。";

        DatabaseText.Text = string.IsNullOrWhiteSpace(AppServices.Settings.LastDatabasePath)
            ? "尚未使用过证书数据库。"
            : "上次使用的数据库：" + AppServices.Settings.LastDatabasePath;

        AboutText.Text = "Kasumi 证书助手 — 证书存储管理、X.509 证书生成与管理、GnuPG 密钥管理。\n" +
                         "日志文件：" + AppServices.LogPath;

        if (AppServices.Gpg.IsAvailable)
        {
            try
            {
                GpgVersionText.Text = AppServices.Gpg.GetVersion();
            }
            catch (Exception ex)
            {
                GpgVersionText.Text = "无法读取版本：" + ex.Message;
            }

            GpgOriginText.Text = AppServices.Gpg.IsBundled
                ? "当前使用随程序一起提供的 GnuPG（" + AppServices.Gpg.ExecutablePath + "），无需在本机安装 Gpg4win。"
                : "当前使用系统安装的 GnuPG（" + AppServices.Gpg.ExecutablePath + "）。";
        }
        else
        {
            GpgVersionText.Text = "未找到 gpg.exe。";
            GpgOriginText.Text = "在本程序目录下放置 gpg\\bin\\gpg.exe 即可使用内置的 GnuPG，也可以安装 Gpg4win 或在此手动指定路径。";
        }
    }

    private async void OnBrowseGpgClick(object sender, RoutedEventArgs e)
    {
        string? path = await FilePickerHelper.PickOpenFileAsync("选择 gpg.exe", ".exe");
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
            _ = DialogService.ShowMessageAsync("自动检测", "未能在常见位置找到 gpg.exe。请手动指定路径。");
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
            await DialogService.ShowMessageAsync("GnuPG 连接测试", "成功。\n\n" + version);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("GnuPG 连接测试失败", ex);
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
            await DialogService.ShowErrorAsync("无法以管理员身份重启", ex);
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
            await DialogService.ShowErrorAsync("无法打开 certmgr.msc", ex);
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
            await DialogService.ShowErrorAsync("无法打开日志目录", ex);
        }
    }

    private void OnOpenAppDataClick(object sender, RoutedEventArgs e)
    {
        OnOpenLogFolderClick(sender, e);
    }
}
