using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.ViewModels;

/// <summary>
/// Row view models hand every row the same <see cref="TableColumnLayout"/> instance so header
/// drags resize the whole table.
/// </summary>
public sealed class CertRow
{
    public CertRow(CertificateItem item, TableColumnLayout columns)
    {
        Item = item;
        Columns = columns;
    }

    public CertificateItem Item { get; }

    public TableColumnLayout Columns { get; }

    public string DisplayName => Item.DisplayName;

    public string IssuerCommonName => Item.IssuerCommonName;

    public string NotAfterText => Item.NotAfterText;

    public string StatusText => Item.StatusText;

    public override string ToString() => Item.DisplayName;
}

public sealed class X509Row
{
    public X509Row(X509Item item, TableColumnLayout columns)
    {
        Item = item;
        Columns = columns;
    }

    public X509Item Item { get; }

    public TableColumnLayout Columns { get; }

    public string Name => Item.Name;

    public string Subject => Item.DisplaySubject;

    public string KindText => Item.KindText;

    public string ValidityText => Item.ValidityText;

    public string NotAfterText => Item.NotAfter?.ToString("yyyy-MM-dd") ?? string.Empty;

    public string KindGlyph => Item.Kind switch
    {
        X509ItemKind.PrivateKey => "\uE192",
        X509ItemKind.Csr => "\uE8A5",
        _ => "\uE8D7",
    };

    public override string ToString() => Item.ToString();
}

public sealed class GpgRow
{
    public GpgRow(GpgKey key, TableColumnLayout columns)
    {
        Key = key;
        Columns = columns;
    }

    public GpgKey Key { get; }

    public TableColumnLayout Columns { get; }

    public string PrimaryUserId => Key.PrimaryUserId;

    public string ShortFingerprint => Key.ShortFingerprint;

    public string AlgorithmText => Key.AlgorithmText;

    public string ExpiresText => Key.Expires is null ? "永不过期" : Key.Expires.Value.ToString("yyyy-MM-dd");

    public string SecretText => Key.SecretText;

    public string StatusText => Key.StatusText;

    public override string ToString() => Key.ToString();
}
