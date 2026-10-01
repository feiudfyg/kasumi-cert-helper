using KasumiCertHelper.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KasumiCertHelper.Controls;

public sealed record DetailItem(string Key, string Label, string Value, bool Monospace = false);

public sealed record DetailSection(string Title, IReadOnlyList<DetailItem> Items, bool Expanded = false);

/// <summary>
/// Renders parsed facts as labelled rows instead of one wall of monospaced text. Every value gets
/// a stable automation id (<c>Detail_&lt;key&gt;</c>) so the UI tests can assert on real fields.
/// </summary>
public static class DetailPresenter
{
    public static void Render(StackPanel host, IReadOnlyList<DetailSection> sections)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Children.Clear();

        foreach (DetailSection section in sections)
        {
            if (section.Items.Count == 0)
            {
                continue;
            }

            var expander = new Expander
            {
                Header = section.Title,
                IsExpanded = section.Expanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = BuildGrid(section.Items),
            };
            AutomationProperties.SetAutomationId(expander, "Section_" + section.Title);
            host.Children.Add(expander);
        }
    }

    public static IReadOnlyList<DetailSection> ForCertificate(CertificateSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var basic = new List<DetailItem>
        {
            new("Subject", "使用者", summary.Subject, Monospace: false),
            new("SubjectCn", "常用名 (CN)", summary.SubjectCommonName),
            new("Issuer", "颁发者", summary.Issuer),
            new("IssuerCn", "颁发者 CN", summary.IssuerCommonName),
            new("Serial", "序列号", summary.SerialNumber, Monospace: true),
            new("Version", "版本", "V" + summary.Version),
            new("Thumbprint", "指纹 (SHA-1)", summary.ThumbprintSha1, Monospace: true),
            new("Thumbprint256", "指纹 (SHA-256)", summary.ThumbprintSha256, Monospace: true),
            new("FriendlyName", "友好名称", summary.FriendlyName),
        };

        var validity = new List<DetailItem>
        {
            new("NotBefore", "生效时间", summary.NotBefore == default ? string.Empty : summary.NotBefore.ToString("yyyy-MM-dd HH:mm:ss")),
            new("NotAfter", "过期时间", summary.NotAfter == default ? string.Empty : summary.NotAfter.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Validity", "状态", summary.ValidityText),
        };

        var key = new List<DetailItem>
        {
            new("PublicKeyAlgorithm", "公钥算法", summary.PublicKeyAlgorithm),
            new("KeySize", "密钥长度", summary.KeySize > 0 ? summary.KeySize + " bit" : string.Empty),
            new("HasPrivateKey", "私钥", summary.HasPrivateKey ? "存在" : "不存在"),
            new("SignatureAlgorithm", "签名算法", summary.SignatureAlgorithm),
        };

        var extensions = new List<DetailItem>
        {
            new("IsCa", "CA 证书", summary.IsCertificateAuthority ? "是" : "否"),
            new("PathLength", "路径长度约束", summary.PathLengthConstraint?.ToString() ?? "无"),
            new("KeyUsage", "密钥用法", summary.KeyUsageText),
            new("Eku", "增强密钥用法", Join(summary.EnhancedKeyUsages)),
            new("San", "使用者可选名称", Join(summary.SubjectAlternativeNames)),
            new("Ski", "使用者密钥标识 (SKI)", summary.SubjectKeyIdentifier, Monospace: true),
            new("Aki", "颁发者密钥标识 (AKI)", summary.AuthorityKeyIdentifier, Monospace: true),
            new("Crl", "CRL 分发点", Join(summary.CrlDistributionPoints)),
            new("Aia", "颁发机构信息访问", Join(summary.AuthorityInformationAccess)),
            new("Policies", "证书策略", Join(summary.CertificatePolicies)),
            new("OtherExtensions", "其他扩展", Join(summary.OtherExtensions)),
        };

        var raw = new List<DetailItem>
        {
            new("RawReport", "文本报告", summary.RawTextReport, Monospace: true),
        };

        return new[]
        {
            new DetailSection("基本信息", basic, Expanded: true),
            new DetailSection("有效期", validity, Expanded: true),
            new DetailSection("公钥与签名", key),
            new DetailSection("扩展", extensions),
            new DetailSection("原始文本报告", raw),
        };
    }

    private static string Join(IReadOnlyList<string> values)
        => values.Count == 0 ? "无" : string.Join("；", values);

    private static UIElement BuildGrid(IReadOnlyList<DetailItem> items)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        foreach (DetailItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Label,
                Foreground = Resource("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var value = new TextBlock
            {
                Text = item.Value,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            if (item.Monospace)
            {
                value.FontFamily = new FontFamily("Consolas");
                value.FontSize = 12;
            }
            AutomationProperties.SetAutomationId(value, "Detail_" + item.Key);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            row++;
        }

        return grid;
    }

    private static Brush Resource(string key)
    {
        Application? app = Application.Current;
        if (app is not null && app.Resources.TryGetValue(key, out object? value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
