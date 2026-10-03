using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Views;

internal static class DatabasePaths
{
    public static string DefaultDirectory => AppServices.DatabaseDirectory;

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
            Header = Loc.Get("Db_Name"),
            Text = "kasumi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            MinWidth = 420,
        };
        AutomationProperties.SetAutomationId(nameBox, "DbName");

        var directoryBox = new TextBox
        {
            Header = Loc.Get("Db_Directory"),
            Text = DatabasePaths.DefaultDirectory,
            MinWidth = 420,
            IsReadOnly = true,
        };
        AutomationProperties.SetAutomationId(directoryBox, "DbDirectory");

        var browseButton = new Button { Content = Loc.Get("Db_ChooseDirectory") };
        browseButton.Click += async (_, _) =>
        {
            string? picked = await FilePickerHelper.PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                directoryBox.Text = picked;
            }
        };

        var passwordBox = new PasswordBox { Header = Loc.Get("Db_Password"), MinWidth = 420 };
        AutomationProperties.SetAutomationId(passwordBox, "DbPassword");

        var confirmBox = new PasswordBox { Header = Loc.Get("Db_PasswordConfirm"), MinWidth = 420 };
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
            Text = Loc.Get("Db_PasswordHint"),
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
            Title = Loc.Get("Db_NewTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = Loc.Get("Common_Create"),
            CloseButtonText = Loc.Get("Common_Cancel"),
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
            if (string.IsNullOrWhiteSpace(name)) return Loc.Get("Db_ErrorNameRequired");
            if (string.IsNullOrWhiteSpace(directory)) return Loc.Get("Db_ErrorDirectoryRequired");
            if (password.Length == 0) return Loc.Get("Db_ErrorPasswordRequired");
            if (password != confirm) return Loc.Get("Dialog_PasswordMismatch");

            try
            {
                string path = Path.Combine(directory.Trim(), DatabasePaths.Sanitize(name.Trim()) + ".kdb");
                if (File.Exists(path)) return Loc.Get("Db_ErrorAlreadyExists");
            }
            catch (Exception ex)
            {
                return Loc.Format("Db_ErrorInvalidPath", ex.Message);
            }

            return string.Empty;
        }
    }

    public static async Task<(string Path, string Password)?> ShowOpenAsync(IReadOnlyList<string> recentDatabases)
    {
        var recentBox = new ComboBox
        {
            Header = Loc.Get("Db_RecentList"),
            ItemsSource = recentDatabases.ToList(),
            MinWidth = 420,
            SelectedIndex = recentDatabases.Count > 0 ? 0 : -1,
            PlaceholderText = Loc.Get("Db_NoRecent"),
        };
        AutomationProperties.SetAutomationId(recentBox, "DbRecent");

        var pathBox = new TextBox
        {
            Header = Loc.Get("Db_Path"),
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

        var browseButton = new Button { Content = Loc.Get("Common_Browse") };
        browseButton.Click += async (_, _) =>
        {
            string? picked = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Common_Open"), ".kdb");
            if (!string.IsNullOrEmpty(picked))
            {
                pathBox.Text = picked;
            }
        };

        var passwordBox = new PasswordBox { Header = Loc.Get("Db_PasswordOnly"), MinWidth = 420 };
        AutomationProperties.SetAutomationId(passwordBox, "DbPassword");

        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        panel.Children.Add(recentBox);
        panel.Children.Add(pathBox);
        panel.Children.Add(browseButton);
        panel.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Db_OpenTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Common_Open"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(pathBox.Text))
        {
            await DialogService.ShowMessageAsync(Loc.Get("Db_OpenTitle"), Loc.Get("Db_ErrorSelectFile"));
            return null;
        }

        return (pathBox.Text.Trim(), passwordBox.Password);
    }
}
