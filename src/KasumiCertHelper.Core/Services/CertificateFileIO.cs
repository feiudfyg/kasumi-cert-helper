using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;
using System.Text;

namespace KasumiCertHelper.Core.Services;

public enum CertificateFileFormat
{
    Der,
    Pem,
    Pkcs12,
}

public static class CertificateFileIO
{
    public static IReadOnlyList<X509Certificate2> Load(byte[] data, string? password, X509KeyStorageFlags flags = X509KeyStorageFlags.DefaultKeySet)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new CryptographicException(Loc.Get("Error_EmptyFile"));
        }

        string? text = TryGetText(data);
        if (text is not null && text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return LoadPem(text, password);
        }

        var pfxError = default(Exception);
        try
        {
            var collection = new X509Certificate2Collection();
            collection.Import(data, password, flags);
            if (collection.Count > 0)
            {
                return collection.Cast<X509Certificate2>().ToList();
            }
        }
        catch (Exception ex) when (ex is CryptographicException)
        {
            pfxError = ex;
        }

        try
        {
            return new List<X509Certificate2> { new(data, (string?)null, flags) };
        }
        catch (CryptographicException)
        {
            if (pfxError is not null)
            {
                throw pfxError;
            }
            throw;
        }
    }

    public static IReadOnlyList<X509Certificate2> LoadFile(string path, string? password, X509KeyStorageFlags flags = X509KeyStorageFlags.DefaultKeySet)
        => Load(File.ReadAllBytes(path), password, flags);

    private static IReadOnlyList<X509Certificate2> LoadPem(string text, string? password)
    {
        var result = new List<X509Certificate2>();
        var collection = new X509Certificate2Collection();
        collection.ImportFromPem(text);
        foreach (X509Certificate2 cert in collection)
        {
            result.Add(cert);
        }

        bool hasEncryptedKey = text.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        bool hasPlainKey = !hasEncryptedKey && text.Contains("PRIVATE KEY", StringComparison.Ordinal);

        if (result.Count > 0 && (hasEncryptedKey || hasPlainKey))
        {
            string certPem = string.Join(Environment.NewLine, collection.Cast<X509Certificate2>().Select(c => c.ExportCertificatePem()));
            try
            {
                X509Certificate2 withKey;
                if (hasEncryptedKey)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        throw new CryptographicException(Loc.Get("Error_PrivateKeyEncryptedEnterPassword"));
                    }
                    withKey = X509Certificate2.CreateFromEncryptedPem(certPem, text, password);
                }
                else
                {
                    withKey = X509Certificate2.CreateFromPem(certPem, text);
                }
                result[0] = withKey;
            }
            catch (CryptographicException)
            {
                throw;
            }
        }

        if (result.Count == 0)
        {
            throw new CryptographicException(Loc.Get("Error_NoCertificateInPemFile"));
        }

        return result;
    }

    public static string ToPem(X509Certificate2 certificate, bool includePrivateKey, string? password = null)
    {
        var sb = new StringBuilder();
        if (includePrivateKey && certificate.HasPrivateKey)
        {
            sb.AppendLine(CertificateKeyIO.ExportPrivateKeyPem(certificate, password));
        }
        sb.Append(certificate.ExportCertificatePem());
        return sb.ToString();
    }

    public static void Export(X509Certificate2 certificate, string path, CertificateFileFormat format, string? password, bool includePrivateKey)
    {
        switch (format)
        {
            case CertificateFileFormat.Der:
                File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
                break;
            case CertificateFileFormat.Pem:
                File.WriteAllText(path, ToPem(certificate, includePrivateKey, password), new UTF8Encoding(false));
                break;
            case CertificateFileFormat.Pkcs12:
                File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    public static void ExportChain(IEnumerable<X509Certificate2> certificates, string path, string? password, bool includePrivateKey)
    {
        var list = certificates.ToList();
        if (path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
        {
            var collection = new X509Certificate2Collection();
            foreach (X509Certificate2 cert in list)
            {
                collection.Add(cert);
            }
            File.WriteAllBytes(path, collection.Export(X509ContentType.Pkcs12, password)!);
            return;
        }

        var sb = new StringBuilder();
        if (includePrivateKey && list.Count > 0 && list[0].HasPrivateKey)
        {
            sb.AppendLine(CertificateKeyIO.ExportPrivateKeyPem(list[0], password));
        }
        foreach (X509Certificate2 cert in list)
        {
            sb.AppendLine(cert.ExportCertificatePem());
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string? TryGetText(byte[] data)
    {
        try
        {
            var text = Encoding.UTF8.GetString(data);
            return text.Contains('\uFFFD') ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
