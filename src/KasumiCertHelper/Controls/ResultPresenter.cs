using KasumiCertHelper.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KasumiCertHelper.Controls;

/// <summary>
/// Turns a <see cref="GpgOperationReport"/> into a readable panel: a status headline, the parsed
/// fields, warnings and (collapsed by default) the exact command plus raw gpg output.
/// </summary>
public static class ResultPresenter
{
    public static UIElement Create(GpgOperationReport report)
    {
        var host = new StackPanel { Spacing = 12 };
        Render(host, report);
        return host;
    }

    public static void Render(StackPanel host, GpgOperationReport report)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(report);

        host.Children.Clear();
        host.Children.Add(BuildHeader(report));

        if (report.Rows.Count > 0)
        {
            host.Children.Add(BuildRows(report));
        }

        if (report.HasNotes)
        {
            host.Children.Add(BuildNotes(report));
        }

        if (report.HasRawOutput || report.CommandLine.Length > 0)
        {
            host.Children.Add(BuildRawOutput(report));
        }

        if (report.Rows.Count == 0 && !report.HasNotes && !report.HasRawOutput && !report.Success)
        {
            host.Children.Add(new TextBlock
            {
                Text = "GnuPG 没有提供更多信息。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("TextFillColorSecondaryBrush"),
            });
        }
    }

    private static UIElement BuildHeader(GpgOperationReport report)
    {
        var icon = new FontIcon
        {
            Glyph = report.Severity switch
            {
                GpgReportSeverity.Success => "\uE73E",
                GpgReportSeverity.Warning => "\uE7BA",
                GpgReportSeverity.Error => "\uE783",
                _ => "\uE946",
            },
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SeverityBrush(report.Severity),
        };

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(report.Title) ? "操作结果" : report.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(icon);
        row.Children.Add(title);

        var host = new StackPanel { Spacing = 6 };
        host.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            host.Children.Add(new TextBlock
            {
                Text = report.Summary,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("TextFillColorSecondaryBrush"),
            });
        }

        return host;
    }

    private static UIElement BuildRows(GpgOperationReport report)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        foreach (GpgReportRow item in report.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Label,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("TextFillColorSecondaryBrush"),
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var value = new TextBlock
            {
                Text = item.Value,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = item.Severity == GpgReportSeverity.Info
                    ? Resource("TextFillColorPrimaryBrush")
                    : SeverityBrush(item.Severity),
            };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            row++;
        }

        return grid;
    }

    private static UIElement BuildNotes(GpgOperationReport report)
    {
        var host = new StackPanel { Spacing = 4 };
        host.Children.Add(new TextBlock
        {
            Text = "说明",
            FontWeight = FontWeights.SemiBold,
        });

        foreach (string note in report.Notes)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = "•",
                Foreground = Resource("TextFillColorSecondaryBrush"),
            });
            row.Children.Add(new TextBlock
            {
                Text = note,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = Resource("TextFillColorSecondaryBrush"),
            });
            host.Children.Add(row);
        }

        return host;
    }

    private static UIElement BuildRawOutput(GpgOperationReport report)
    {
        var content = new StackPanel { Spacing = 8 };

        if (report.CommandLine.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = report.CommandLine,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = Resource("TextFillColorSecondaryBrush"),
            });
        }

        if (report.HasRawOutput)
        {
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                Content = new TextBlock
                {
                    Text = report.RawOutput,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
            });
        }

        return new Expander
        {
            Header = "命令与原始输出",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content,
        };
    }

    private static Brush SeverityBrush(GpgReportSeverity severity) => Resource(severity switch
    {
        GpgReportSeverity.Success => "SystemFillColorSuccessBrush",
        GpgReportSeverity.Warning => "SystemFillColorCautionBrush",
        GpgReportSeverity.Error => "SystemFillColorCriticalBrush",
        _ => "TextFillColorPrimaryBrush",
    });

    private static Brush Resource(string key)
    {
        Application? app = Application.Current;
        if (app is not null && app.Resources.TryGetValue(key, out object? value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Gray);
    }
}
