using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public sealed class CertificateStoreService
{
    private static readonly Dictionary<string, string> FriendlyStoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["My"] = "个人",
        ["Root"] = "受信任的根证书颁发机构",
        ["CA"] = "中间证书颁发机构",
        ["AuthRoot"] = "第三方根证书颁发机构",
        ["Trust"] = "企业信任",
        ["Disallowed"] = "不信任的证书",
        ["TrustedPeople"] = "受信任人",
        ["TrustedPublisher"] = "受信任的发布者",
        ["AddressBook"] = "其他人",
        ["Remote Desktop"] = "远程桌面",
        ["SmartCard"] = "智能卡受信任的根",
        ["ClientAuthIssuer"] = "客户端身份验证颁发者",
        ["UserDS"] = "Active Directory 用户对象",
        ["Request"] = "证书注册请求",
        ["REQUESTS"] = "证书注册请求",
    };

    public static readonly string[] StandardStores =
    {
        "My",
        "Root",
        "Trust",
        "CA",
        "UserDS",
        "TrustedPublisher",
        "Disallowed",
        "AuthRoot",
        "TrustedPeople",
        "ClientAuthIssuer",
        "Remote Desktop",
        "SmartCard",
        "Request",
    };

    public static string GetFriendlyStoreName(string storeName)
        => FriendlyStoreNames.TryGetValue(storeName, out string? friendly) ? friendly : storeName;

    public static string GetPhysicalStoreRoot(StoreLocation location) => location switch
    {
        StoreLocation.CurrentUser => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "SystemCertificates"),
        StoreLocation.LocalMachine => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "SystemCertificates"),
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };

    public IReadOnlyList<string> GetStoreNames(StoreLocation location)
    {
        var discovered = new List<string>();
        string root = GetPhysicalStoreRoot(location);
        if (Directory.Exists(root))
        {
            try
            {
                discovered = Directory.EnumerateDirectories(root)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList();
            }
            catch (Exception)
            {
            }
        }

        var ordered = new List<string>();
        foreach (string name in StandardStores)
        {
            if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(name);
            }
        }
        foreach (string name in discovered.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(name);
            }
        }
        return ordered;
    }

    public IReadOnlyList<StoreRef> GetStores(StoreLocation location)
        => GetStoreNames(location).Select(n => new StoreRef(location, n)).ToList();

    public IReadOnlyList<CertificateItem> GetCertificates(StoreLocation location, string storeName)
        => GetCertificates(location, storeName, out _);

    public IReadOnlyList<CertificateItem> GetCertificates(StoreLocation location, string storeName, out string? error)
    {
        error = null;
        var result = new List<CertificateItem>();
        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (X509Certificate2 certificate in store.Certificates)
            {
                try
                {
                    result.Add(new CertificateItem(certificate, location, storeName));
                }
                catch (Exception ex)
                {
                    error ??= ex.Message;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return result;
    }

    public int GetCertificateCount(StoreLocation location, string storeName)
    {
        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            return store.Certificates.Count;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public IReadOnlyList<StoreSummary> GetStoreSummaries(StoreLocation location)
    {
        var summaries = new List<StoreSummary>();
        foreach (string name in GetStoreNames(location))
        {
            summaries.Add(new StoreSummary(new StoreRef(location, name), GetCertificateCount(location, name)));
        }
        return summaries;
    }

    public IReadOnlyList<CertificateItem> GetAllCertificates(bool includeCurrentUser = true, bool includeLocalMachine = true)
    {
        var result = new List<CertificateItem>();
        foreach (StoreLocation location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            if (location == StoreLocation.CurrentUser && !includeCurrentUser) continue;
            if (location == StoreLocation.LocalMachine && !includeLocalMachine) continue;

            foreach (string storeName in GetStoreNames(location))
            {
                try
                {
                    result.AddRange(GetCertificates(location, storeName));
                }
                catch (Exception)
                {
                }
            }
        }
        return result;
    }

    public void AddCertificate(StoreLocation location, string storeName, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var store = new X509Store(storeName, location);
        store.Open(OpenFlags.ReadWrite);
        try
        {
            store.Add(certificate);
        }
        finally
        {
            store.Close();
        }
    }

    public void RemoveCertificate(StoreLocation location, string storeName, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var store = new X509Store(storeName, location);
        store.Open(OpenFlags.ReadWrite);
        try
        {
            store.Remove(certificate);
        }
        finally
        {
            store.Close();
        }
    }

    public void RemoveCertificateByThumbprint(StoreLocation location, string storeName, string thumbprint)
    {
        foreach (CertificateItem item in GetCertificates(location, storeName))
        {
            if (string.Equals(item.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                RemoveCertificate(location, storeName, item.Certificate);
                return;
            }
        }
        throw new CryptographicException("在存储中找不到指定的证书。");
    }

    public X509KeyStorageFlags GetStorageFlags(StoreLocation location)
    {
        X509KeyStorageFlags flags = X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable;
        flags |= location == StoreLocation.LocalMachine
            ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.UserKeySet;
        return flags;
    }

    public X509Certificate2 ImportCertificateToStore(byte[] data, string? password, StoreLocation location, string storeName)
    {
        X509KeyStorageFlags loadFlags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.DefaultKeySet;
        IReadOnlyList<X509Certificate2> certificates = CertificateFileIO.Load(data, password, loadFlags);
        if (certificates.Count == 0)
        {
            throw new CryptographicException("文件中未找到任何证书。");
        }

        X509KeyStorageFlags flags = GetStorageFlags(location);
        X509Certificate2 primary = certificates[0];
        X509Certificate2 toAdd = primary;

        if (primary.HasPrivateKey)
        {
            byte[] pfx = primary.Export(X509ContentType.Pkcs12);
            toAdd = new X509Certificate2(pfx, (string?)null, flags);
        }

        AddCertificate(location, storeName, toAdd);

        foreach (X509Certificate2 extra in certificates.Skip(1))
        {
            try
            {
                AddCertificate(location, storeName, extra);
            }
            catch (CryptographicException)
            {
            }
        }

        return toAdd;
    }

    public static bool IsStoreWritable(StoreLocation location, string storeName)
    {
        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            store.Close();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
