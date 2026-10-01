using KasumiCertHelper.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Services;

public static class DialogService
{
    public static XamlRoot? GetXamlRoot()
        => (App.MainWindow?.Content as FrameworkElement)?.XamlRoot;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = GetXamlRoot();
        return await dialog.ShowAsync();
    }

    public static async Task ShowMessageAsync(string title, string message, string closeText = "确定")
    {
        if (GetXamlRoot() is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = CreateScrollableText(message),
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
        };
        await ShowAsync(dialog);
    }

    public static async Task<bool> ShowConfirmAsync(string title, string message, string primaryText = "确定", string closeText = "取消")
    {
        if (GetXamlRoot() is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = CreateScrollableText(message),
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public static async Task<string?> ShowTextInputAsync(string title, string label, string? initialValue = null, bool multiline = false)
    {
        if (GetXamlRoot() is null)
        {
            return null;
        }

        var input = new TextBox
        {
            Text = initialValue ?? string.Empty,
            PlaceholderText = label,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinWidth = 380,
        };
        if (multiline)
        {
            input.Height = 160;
        }

        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(label))
        {
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(input);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await ShowAsync(dialog) == ContentDialogResult.Primary ? input.Text : null;
    }

    public static async Task<string?> ShowPasswordAsync(string title, string label, bool confirmRequired = false)
    {
        if (GetXamlRoot() is null)
        {
            return null;
        }

        var first = new PasswordBox { PlaceholderText = label, MinWidth = 380 };
        var second = new PasswordBox { PlaceholderText = "再次输入密码", MinWidth = 380, Visibility = confirmRequired ? Visibility.Visible : Visibility.Collapsed };
        var error = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed), Visibility = Visibility.Collapsed };

        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(label))
        {
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (confirmRequired)
        {
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (first.Password != second.Password)
                {
                    args.Cancel = true;
                    error.Text = "两次输入的密码不一致。";
                    error.Visibility = Visibility.Visible;
                }
            };
        }

        return await ShowAsync(dialog) == ContentDialogResult.Primary ? first.Password : null;
    }

    public static async Task ShowErrorAsync(string title, Exception exception)
    {
        string message = DescribeException(exception);
        AppServices.Log(title + ": " + exception);
        await ShowMessageAsync(title, message);
    }

    public static string DescribeException(Exception exception)
    {
        var messages = new List<string>();
        Exception? current = exception;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
            {
                messages.Add(current.Message.Trim());
            }
            current = current.InnerException;
        }

        string text = string.Join(Environment.NewLine, messages);
        if (exception is UnauthorizedAccessException ||
            text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("access denied", StringComparison.OrdinalIgnoreCase))
        {
            text += Environment.NewLine + Environment.NewLine +
                    "提示：该操作需要管理员权限。请以管理员身份重新启动本程序后再试。" +
                    (ElevationHelper.IsAdministrator() ? string.Empty : " (当前进程不是管理员)");
        }
        return text;
    }

    private static ScrollViewer CreateScrollableText(string message) => new()
    {
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
        MaxHeight = 440,
        MaxWidth = 620,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}
