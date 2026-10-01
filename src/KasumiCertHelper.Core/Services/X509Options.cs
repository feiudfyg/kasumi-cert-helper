using System.Security.Cryptography.X509Certificates;
using System.Text;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public enum SanKind
{
    Dns,
    Ip,
    Email,
    Uri,
    Upn,
}

public sealed class SanEntry
{
    public SanKind Kind { get; set; } = SanKind.Dns;

    public string Value { get; set; } = string.Empty;

    public override string ToString() => $"{Kind}: {Value}";
}

public sealed class X509KeyOptions
{
    public X509KeyAlgorithm Algorithm { get; set; } = X509KeyAlgorithm.Rsa;

    public int KeySize { get; set; } = 4096;

    public string Curve { get; set; } = "nistP256";
}

public sealed class X509SubjectOptions
{
    public string CommonName { get; set; } = string.Empty;

    public string OrganizationalUnit { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public string Locality { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string BuildDistinguishedName()
    {
        var parts = new List<string>();
        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={EscapeValue(value.Trim())}");
            }
        }

        Add("CN", CommonName);
        Add("OU", OrganizationalUnit);
        Add("O", Organization);
        Add("L", Locality);
        Add("ST", State);
        Add("C", Country);
        Add("E", EmailAddress);
        return string.Join(", ", parts);
    }

    public static string EscapeValue(string value)
    {
        var sb = new StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool needsEscape = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '='
                || (c == '#' && i == 0)
                || (c == ' ' && (i == 0 || i == value.Length - 1));
            if (needsEscape)
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

public sealed class X509CertificateOptions
{
    public string Subject { get; set; } = string.Empty;

    public int ValidDays { get; set; } = 365;

    public DateTimeOffset? NotBefore { get; set; }

    public DateTimeOffset? NotAfter { get; set; }

    public string HashAlgorithm { get; set; } = "SHA256";

    public string? SerialNumberHex { get; set; }

    public bool IsCa { get; set; }

    public bool HasPathLengthConstraint { get; set; }

    public int PathLengthConstraint { get; set; }

    public X509KeyUsageFlags KeyUsage { get; set; } =
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;

    public bool IncludeKeyUsage { get; set; } = true;

    public List<string> ExtendedKeyUsageOids { get; set; } = new();

    public List<SanEntry> SubjectAlternativeNames { get; set; } = new();

    public List<string> CrlDistributionPoints { get; set; } = new();

    public List<string> OcspUrls { get; set; } = new();

    public List<string> CaIssuerUrls { get; set; } = new();

    public List<string> CertificatePolicies { get; set; } = new();

    public DateTimeOffset ResolveNotBefore()
        => NotBefore ?? DateTimeOffset.Now.AddMinutes(-5);

    public DateTimeOffset ResolveNotAfter()
        => NotAfter ?? ResolveNotBefore().AddDays(ValidDays <= 0 ? 365 : ValidDays);
}
