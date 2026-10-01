using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Views;

internal static class DatabasePaths
{
    public static string DefaultDirectory => Path.Combine(AppServices.AppDataDirectory, "databases");

    public static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "kasumi-db" : name;
    }
}

internal static class DatabaseDialogs
{
    public static async Task<(string Path, string Password)?> ShowCreateAsync()
    {
        Directory.CreateDirectory(DatabasePaths.DefaultDirectory);

        var nameBox = new TextBox
        {
            Header = "数据库名称",
            Text = "kasumi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            MinWidth = 420,
        };
        AutomationProperties.SetAutomationId(nameBox, "DbName");

        var directoryBox = new TextBox
        {
            Header = "保存目录",
            Text = DatabasePaths.DefaultDirectory,
            MinWidth = 420,
            IsReadOnly = true,
        };
        AutomationProperties.SetAutomationId(directoryBox, "DbDirectory");

        var browseButton = new Button { Content = "选择其他目录..." };
        browseButton.Click += async (_, _) =>
        {
            string? picked = await FilePickerHelper.PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                directoryBox.Text = picked;
            }
        };

        var passwordBox = new PasswordBox { Header = "数据库密码（用于加密私钥）", MinWidth = 420 };
        AutomationProperties.SetAutomationId(passwordBox, "DbPassword");

        var confirmBox = new PasswordBox { Header = "确认密码", MinWidth = 420 };
        AutomationProperties.SetAutomationId(confirmBox, "DbPasswordConfirm");

        var error = new TextBlock
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = "数据库中的所有私钥都会使用该密码加密存储（PKCS#8 / PBES2 / AES-256）。请务必牢记密码，忘记后无法恢复私钥。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(directoryBox);
        panel.Children.Add(browseButton);
        panel.Children.Add(passwordBox);
        panel.Children.Add(confirmBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = "新建证书数据库",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            string message = Validate(directoryBox.Text, nameBox.Text, passwordBox.Password, confirmBox.Password);
            if (message.Length > 0)
            {
                args.Cancel = true;
                error.Text = message;
                error.Visibility = Visibility.Visible;
            }
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        string path = Path.Combine(directoryBox.Text.Trim(), DatabasePaths.Sanitize(nameBox.Text.Trim()) + ".kdb");
        return (path, passwordBox.Password);

        static string Validate(string directory, string name, string password, string confirm)
        {
            if (string.IsNullOrWhiteSpace(name)) return "请输入数据库名称。";
            if (string.IsNullOrWhiteSpace(directory)) return "请选择保存目录。";
            if (password.Length == 0) return "数据库密码不能为空。";
            if (password != confirm) return "两次输入的密码不一致。";

            try
            {
                string path = Path.Combine(directory.Trim(), DatabasePaths.Sanitize(name.Trim()) + ".kdb");
                if (File.Exists(path)) return "该名称的数据库已存在，请更换名称。";
            }
            catch (Exception ex)
            {
                return "路径无效：" + ex.Message;
            }

            return string.Empty;
        }
    }

    public static async Task<(string Path, string Password)?> ShowOpenAsync(IReadOnlyList<string> recentDatabases)
    {
        var recentBox = new ComboBox
        {
            Header = "最近使用的数据库",
            ItemsSource = recentDatabases.ToList(),
            MinWidth = 420,
            SelectedIndex = recentDatabases.Count > 0 ? 0 : -1,
            PlaceholderText = "（无）",
        };
        AutomationProperties.SetAutomationId(recentBox, "DbRecent");

        var pathBox = new TextBox
        {
            Header = "数据库路径",
            Text = recentDatabases.Count > 0 ? recentDatabases[0] : string.Empty,
            MinWidth = 420,
        };
        AutomationProperties.SetAutomationId(pathBox, "DbPath");

        recentBox.SelectionChanged += (_, _) =>
        {
            if (recentBox.SelectedItem is string selected)
            {
                pathBox.Text = selected;
            }
        };

        var browseButton = new Button { Content = "浏览..." };
        browseButton.Click += async (_, _) =>
        {
            string? picked = await FilePickerHelper.PickOpenFileAsync("打开", ".kdb");
            if (!string.IsNullOrEmpty(picked))
            {
                pathBox.Text = picked;
            }
        };

        var passwordBox = new PasswordBox { Header = "数据库密码", MinWidth = 420 };
        AutomationProperties.SetAutomationId(passwordBox, "DbPassword");

        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        panel.Children.Add(recentBox);
        panel.Children.Add(pathBox);
        panel.Children.Add(browseButton);
        panel.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = "打开证书数据库",
            Content = panel,
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(pathBox.Text))
        {
            await DialogService.ShowMessageAsync("打开数据库", "请选择一个数据库文件。");
            return null;
        }

        return (pathBox.Text.Trim(), passwordBox.Password);
    }
}
