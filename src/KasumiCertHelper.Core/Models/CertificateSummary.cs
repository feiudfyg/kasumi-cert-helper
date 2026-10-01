namespace KasumiCertHelper.Core.Models;

/// <summary>
/// Everything a certificate view needs, already parsed into human readable pieces so the UI never
/// has to dump raw ASN.1 or hex at the user.
/// </summary>
public sealed class CertificateSummary
{
    public int Version { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public string SignatureAlgorithm { get; set; } = string.Empty;

    public string ThumbprintSha1 { get; set; } = string.Empty;

    public string ThumbprintSha256 { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string SubjectCommonName { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string IssuerCommonName { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public DateTime NotBefore { get; set; }

    public DateTime NotAfter { get; set; }

    public string ValidityText { get; set; } = string.Empty;

    public int RemainingDays { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string PublicKeyAlgorithm { get; set; } = string.Empty;

    public int KeySize { get; set; }

    public bool HasPrivateKey { get; set; }

    public bool IsCertificateAuthority { get; set; }

    public int? PathLengthConstraint { get; set; }

    public string KeyUsageText { get; set; } = string.Empty;

    public IReadOnlyList<string> EnhancedKeyUsages { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> SubjectAlternativeNames { get; set; } = Array.Empty<string>();

    public string SubjectKeyIdentifier { get; set; } = string.Empty;

    public string AuthorityKeyIdentifier { get; set; } = string.Empty;

    public IReadOnlyList<string> CrlDistributionPoints { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AuthorityInformationAccess { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> CertificatePolicies { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> OtherExtensions { get; set; } = Array.Empty<string>();

    public string RawTextReport { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? SubjectCommonName : FriendlyName;

    public string ValidityRangeText => NotBefore == default
        ? string.Empty
        : $"{NotBefore:yyyy-MM-dd HH:mm} → {NotAfter:yyyy-MM-dd HH:mm}";
}
