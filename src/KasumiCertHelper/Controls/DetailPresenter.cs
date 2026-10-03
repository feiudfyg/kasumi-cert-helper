using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KasumiCertHelper.Controls;

public sealed record DetailItem(string Key, string Label, string Value, bool Monospace = false);

/// <summary>
/// <paramref name="Key"/> is a stable, language independent identifier: it becomes the automation id
/// so the UI tests do not have to know the localized title.
/// </summary>
public sealed record DetailSection(string Key, string Title, IReadOnlyList<DetailItem> Items, bool Expanded = false);

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
            AutomationProperties.SetAutomationId(expander, "Section_" + section.Key);
            AutomationProperties.SetName(expander, section.Title);
            host.Children.Add(expander);
        }
    }

    public static IReadOnlyList<DetailSection> ForCertificate(CertificateSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var basic = new List<DetailItem>
        {
            new("Subject", Loc.Get("Cert_Row_Subject"), summary.Subject, Monospace: false),
            new("SubjectCn", Loc.Get("Cert_Row_SubjectCn"), summary.SubjectCommonName),
            new("Issuer", Loc.Get("Cert_Row_Issuer"), summary.Issuer),
            new("IssuerCn", Loc.Get("Cert_Row_IssuerCn"), summary.IssuerCommonName),
            new("Serial", Loc.Get("Cert_Row_Serial"), summary.SerialNumber, Monospace: true),
            new("Version", Loc.Get("Cert_Row_Version"), "V" + summary.Version),
            new("Thumbprint", Loc.Get("Cert_Row_ThumbprintSha1"), summary.ThumbprintSha1, Monospace: true),
            new("Thumbprint256", Loc.Get("Cert_Row_ThumbprintSha256"), summary.ThumbprintSha256, Monospace: true),
            new("FriendlyName", Loc.Get("Cert_Row_FriendlyName"), summary.FriendlyName),
        };

        var validity = new List<DetailItem>
        {
            new("NotBefore", Loc.Get("Cert_Row_NotBefore"), summary.NotBefore == default ? string.Empty : summary.NotBefore.ToString("yyyy-MM-dd HH:mm:ss")),
            new("NotAfter", Loc.Get("Cert_Row_NotAfter"), summary.NotAfter == default ? string.Empty : summary.NotAfter.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Validity", Loc.Get("Cert_Row_Status"), summary.ValidityText),
        };

        var key = new List<DetailItem>
        {
            new("PublicKeyAlgorithm", Loc.Get("Gpg_Row_PublicKeyAlgorithm"), summary.PublicKeyAlgorithm),
            new("KeySize", Loc.Get("Cert_Row_KeySize"), summary.KeySize > 0 ? summary.KeySize + " bit" : string.Empty),
            new("HasPrivateKey", Loc.Get("Cert_Row_PrivateKey"), Loc.Get(summary.HasPrivateKey ? "Cert_Value_Present" : "Cert_Value_Absent")),
            new("SignatureAlgorithm", Loc.Get("Cert_Row_SignatureAlgorithm"), summary.SignatureAlgorithm),
        };

        var extensions = new List<DetailItem>
        {
            new("IsCa", Loc.Get("Cert_Row_IsCa"), Loc.Get(summary.IsCertificateAuthority ? "Common_Yes" : "Common_No")),
            new("PathLength", Loc.Get("Cert_Row_PathLength"), summary.PathLengthConstraint?.ToString() ?? Loc.Get("Common_None")),
            new("KeyUsage", Loc.Get("Cert_Row_KeyUsage"), summary.KeyUsageText),
            new("Eku", Loc.Get("Cert_Row_Eku"), Join(summary.EnhancedKeyUsages)),
            new("San", Loc.Get("Cert_Row_San"), Join(summary.SubjectAlternativeNames)),
            new("Ski", Loc.Get("Cert_Row_Ski"), summary.SubjectKeyIdentifier, Monospace: true),
            new("Aki", Loc.Get("Cert_Row_Aki"), summary.AuthorityKeyIdentifier, Monospace: true),
            new("Crl", Loc.Get("Cert_Row_Crl"), Join(summary.CrlDistributionPoints)),
            new("Aia", Loc.Get("Cert_Row_Aia"), Join(summary.AuthorityInformationAccess)),
            new("Policies", Loc.Get("Cert_Row_Policies"), Join(summary.CertificatePolicies)),
            new("OtherExtensions", Loc.Get("Cert_Row_OtherExtensions"), Join(summary.OtherExtensions)),
        };

        var raw = new List<DetailItem>
        {
            new("RawReport", Loc.Get("Cert_Row_RawReport"), summary.RawTextReport, Monospace: true),
        };

        return new[]
        {
            new DetailSection("Basic", Loc.Get("Detail_Section_Basic"), basic, Expanded: true),
            new DetailSection("Validity", Loc.Get("Detail_Section_Validity"), validity, Expanded: true),
            new DetailSection("PublicKey", Loc.Get("Detail_Section_Key"), key),
            new DetailSection("Extensions", Loc.Get("Detail_Section_Extensions"), extensions),
            new DetailSection("Raw", Loc.Get("Detail_Section_Raw"), raw),
        };
    }

    private static string Join(IReadOnlyList<string> values)
        => values.Count == 0 ? Loc.Get("Common_None") : string.Join(Loc.Get("Common_SentenceSeparator"), values);

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
