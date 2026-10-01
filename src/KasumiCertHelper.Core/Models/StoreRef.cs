using System.Security.Cryptography.X509Certificates;

namespace KasumiCertHelper.Core.Models;

public sealed record StoreRef(StoreLocation Location, string Name)
{
    public string FriendlyName => Services.CertificateStoreService.GetFriendlyStoreName(Name);

    public string LocationText => Location == StoreLocation.CurrentUser ? "当前用户" : "本地计算机";

    public string DisplayName => $"{FriendlyName} ({Name})";

    public override string ToString() => $"{LocationText}\\{Name}";
}

public sealed class StoreSummary
{
    public StoreSummary(StoreRef store, int count)
    {
        Store = store;
        Count = count;
    }

    public StoreRef Store { get; }

    public int Count { get; }

    public string DisplayName => Store.FriendlyName;

    public string CountText => Count < 0 ? "-" : Count.ToString();

    public override string ToString() => Count < 0 ? DisplayName : $"{DisplayName} ({Count})";
}
