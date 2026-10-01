using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Models;

public sealed record StoreRef(StoreLocation Location, string Name)
{
    public string FriendlyName => Services.CertificateStoreService.GetFriendlyStoreName(Name);

    public string LocationText => Loc.Get(Location == StoreLocation.CurrentUser
        ? "Store_Location_CurrentUser"
        : "Store_Location_LocalMachine");

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
