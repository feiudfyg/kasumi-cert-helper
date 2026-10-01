using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Models;

public sealed class CertificateItem
{
    public CertificateItem(X509Certificate2 certificate, StoreLocation location, string storeName)
    {
        Certificate = certificate;
        Location = location;
        StoreName = storeName ?? string.Empty;
    }

    public X509Certificate2 Certificate { get; }

    public StoreLocation Location { get; }

    public string StoreName { get; }

    public string Subject => Certificate.Subject;

    public string Issuer => Certificate.Issuer;

    public string Thumbprint => Certificate.Thumbprint;

    public string SerialNumber => Certificate.SerialNumber;

    public DateTime NotBefore => Certificate.NotBefore;

    public DateTime NotAfter => Certificate.NotAfter;

    public bool HasPrivateKey => Certificate.HasPrivateKey;

    public string FriendlyName
    {
        get
        {
            try { return Certificate.FriendlyName; }
            catch { return string.Empty; }
        }
    }

    public bool IsExpired => DateTime.Now > NotAfter;

    public bool IsNotYetValid => DateTime.Now < NotBefore;

    public bool IsValidNow => !IsExpired && !IsNotYetValid;

    public int Version => Certificate.Version;

    public string CommonName => Services.X500Name.GetCommonName(Subject);

    public string IssuerCommonName => Services.X500Name.GetCommonName(Issuer);

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName : CommonName;

    public string SignatureAlgorithm
    {
        get
        {
            try { return Certificate.SignatureAlgorithm.FriendlyName ?? Certificate.SignatureAlgorithm.Value ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    public string PublicKeyAlgorithm
    {
        get
        {
            try { return Certificate.PublicKey.Oid.FriendlyName ?? Certificate.PublicKey.Oid.Value ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    public int KeySize
    {
        get
        {
            try
            {
                using RSA? rsa = Certificate.GetRSAPublicKey();
                if (rsa is not null) return rsa.KeySize;
                using ECDsa? ecdsa = Certificate.GetECDsaPublicKey();
                if (ecdsa is not null) return ecdsa.KeySize;
                using DSA? dsa = Certificate.GetDSAPublicKey();
                if (dsa is not null) return dsa.KeySize;
            }
            catch { }
            return 0;
        }
    }

    public string KeyUsageText => CertificateDetailsBuilder.GetKeyUsageText(Certificate);

    public IReadOnlyList<string> EnhancedKeyUsages => CertificateDetailsBuilder.GetEnhancedKeyUsages(Certificate);

    public bool IsCertificateAuthority => CertificateDetailsBuilder.IsCa(Certificate);

    public string NotBeforeText => NotBefore.ToString("yyyy-MM-dd");

    public string NotAfterText => NotAfter.ToString("yyyy-MM-dd HH:mm");

    public string StatusText => IsExpired ? "已过期" : IsNotYetValid ? "尚未生效" : "有效";

    public string HasPrivateKeyText => HasPrivateKey ? "是" : "否";

    public string KeyAlgorithmText
    {
        get
        {
            string algorithm = PublicKeyAlgorithm;
            int size = KeySize;
            return size > 0 ? $"{algorithm} ({size})" : algorithm;
        }
    }

    public override string ToString() => DisplayName;
}
