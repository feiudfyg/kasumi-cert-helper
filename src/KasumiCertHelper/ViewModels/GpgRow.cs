using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.ViewModels;

public sealed class GpgRow
{
    public GpgRow(OpenPgpStoredKey key, TableColumnLayout columns)
    {
        Key = key;
        Columns = columns;
    }

    public OpenPgpStoredKey Key { get; }

    public TableColumnLayout Columns { get; }

    public string PrimaryUserId => Key.UserId;

    public string ShortFingerprint => Key.ShortFingerprint;

    public string AlgorithmText => Key.AlgorithmText;

    public string ExpiresText => Key.ExpiresText;

    public string SecretText => Key.SecretText;

    public string StatusText => Key.StatusText;

    public override string ToString() => Key.ToString();
}
