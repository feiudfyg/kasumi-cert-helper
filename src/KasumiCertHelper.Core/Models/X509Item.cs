using System.Text.Json.Serialization;

namespace KasumiCertHelper.Core.Models;

public enum X509ItemKind
{
    PrivateKey,
    Certificate,
    Csr,
}

public enum X509KeyAlgorithm
{
    Rsa,
    Ecdsa,
}

public sealed class X509Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public X509ItemKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string? KeyId { get; set; }

    public string? CertificatePem { get; set; }

    public string? CsrPem { get; set; }

    public string? EncryptedKeyPem { get; set; }

    public string? Subject { get; set; }

    public string? Issuer { get; set; }

    public string? Serial { get; set; }

    public DateTime? NotBefore { get; set; }

    public DateTime? NotAfter { get; set; }

    public bool IsCa { get; set; }

    public string? KeyAlgorithm { get; set; }

    public int? KeySize { get; set; }

    public string? SignatureAlgorithm { get; set; }

    [JsonIgnore]
    public bool HasPrivateKey => !string.IsNullOrEmpty(EncryptedKeyPem);

    [JsonIgnore]
    public string KindText => Kind switch
    {
        X509ItemKind.PrivateKey => "私钥",
        X509ItemKind.Certificate => "证书",
        X509ItemKind.Csr => "证书请求",
        _ => Kind.ToString(),
    };

    [JsonIgnore]
    public string DisplaySubject
    {
        get
        {
            if (!string.IsNullOrEmpty(Subject))
            {
                return Services.X500Name.GetCommonName(Subject);
            }
            return Name;
        }
    }

    [JsonIgnore]
    public string ValidityText
    {
        get
        {
            if (NotBefore is null || NotAfter is null)
            {
                return string.Empty;
            }
            DateTime now = DateTime.Now;
            if (now < NotBefore.Value) return "尚未生效";
            if (now > NotAfter.Value) return "已过期";
            return "有效";
        }
    }

    public override string ToString() => $"{Name} ({KindText})";
}
