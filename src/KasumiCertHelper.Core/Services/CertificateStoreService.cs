using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public sealed class CertificateStoreService
{
    // Resource keys, resolved on demand so the store list follows the active language.
    private static readonly Dictionary<string, string> FriendlyStoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["My"] = "Store_Name_My",
        ["Root"] = "Store_Name_Root",
        ["CA"] = "Store_Name_CA",
        ["AuthRoot"] = "Store_Name_AuthRoot",
        ["Trust"] = "Store_Name_Trust",
        ["Disallowed"] = "Store_Name_Disallowed",
        ["TrustedPeople"] = "Store_Name_TrustedPeople",
        ["TrustedPublisher"] = "Store_Name_TrustedPublisher",
        ["AddressBook"] = "Store_Name_AddressBook",
        ["Remote Desktop"] = "Store_Name_RemoteDesktop",
        ["SmartCard"] = "Store_Name_SmartCard",
        ["ClientAuthIssuer"] = "Store_Name_ClientAuthIssuer",
        ["UserDS"] = "Store_Name_UserDS",
        ["Request"] = "Store_Name_Request",
        ["REQUESTS"] = "Store_Name_Request",
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
        => FriendlyStoreNames.TryGetValue(storeName, out string? friendly) ? Loc.Get(friendly) : storeName;

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
        throw new CryptographicException(Loc.Get("Error_CertificateNotFoundInStore"));
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
            throw new CryptographicException(Loc.Get("Error_NoCertificateInFile"));
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