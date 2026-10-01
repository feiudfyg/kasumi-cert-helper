using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Services;

public static class CertificateDetailsBuilder
{
    // Resource keys, resolved on demand so a language change is picked up by the next certificate
    // that is opened instead of freezing the text the first time this type is touched.
    private static readonly Dictionary<string, string> EkuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.3.6.1.5.5.7.3.1"] = "Cert_Eku_ServerAuth",
        ["1.3.6.1.5.5.7.3.2"] = "Cert_Eku_ClientAuth",
        ["1.3.6.1.5.5.7.3.3"] = "Cert_Eku_CodeSigning",
        ["1.3.6.1.5.5.7.3.4"] = "Cert_Eku_EmailProtection",
        ["1.3.6.1.5.5.7.3.5"] = "Cert_Eku_IpsecEndSystem",
        ["1.3.6.1.5.5.7.3.6"] = "Cert_Eku_IpsecTunnel",
        ["1.3.6.1.5.5.7.3.7"] = "Cert_Eku_IpsecUser",
        ["1.3.6.1.5.5.7.3.8"] = "Cert_Eku_TimeStamping",
        ["1.3.6.1.5.5.7.3.9"] = "Cert_Eku_OcspSigning",
        ["1.3.6.1.5.5.7.3.10"] = "DVCS",
        ["2.5.29.37.0"] = "Cert_Eku_Any",
        ["1.3.6.1.4.1.311.10.3.1"] = "Cert_Eku_MsTrustListSigning",
        ["1.3.6.1.4.1.311.10.3.4"] = "Cert_Eku_Efs",
        ["1.3.6.1.4.1.311.10.3.4.1"] = "Cert_Eku_EfsRecovery",
        ["1.3.6.1.4.1.311.10.3.12"] = "Cert_Eku_DocumentSigning",
        ["1.3.6.1.4.1.311.20.2.2"] = "Cert_Eku_SmartCardLogon",
        ["1.3.6.1.4.1.311.21.6"] = "Cert_Eku_KeyRecovery",
        ["1.3.6.1.5.2.3.4"] = "Cert_Eku_PkinitClient",
        ["1.3.6.1.5.2.3.5"] = "Cert_Eku_PkinitKdc",
    };

    public static bool IsCa(X509Certificate2 certificate)
    {
        X509BasicConstraintsExtension? bc = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .FirstOrDefault();
        return bc is not null && bc.CertificateAuthority;
    }

    public static string GetKeyUsageText(X509Certificate2 certificate)
    {
        X509KeyUsageExtension? ku = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (ku is null)
        {
            return string.Empty;
        }

        var names = new List<string>();
        X509KeyUsageFlags flags = ku.KeyUsages;
        void Add(X509KeyUsageFlags f, string name)
        {
            if ((flags & f) != 0) names.Add(name);
        }
        Add(X509KeyUsageFlags.DigitalSignature, Loc.Get("Cert_KeyUsage_DigitalSignature"));
        Add(X509KeyUsageFlags.NonRepudiation, Loc.Get("Cert_KeyUsage_NonRepudiation"));
        Add(X509KeyUsageFlags.KeyEncipherment, Loc.Get("Cert_KeyUsage_KeyEncipherment"));
        Add(X509KeyUsageFlags.DataEncipherment, Loc.Get("Cert_KeyUsage_DataEncipherment"));
        Add(X509KeyUsageFlags.KeyAgreement, Loc.Get("Cert_KeyUsage_KeyAgreement"));
        Add(X509KeyUsageFlags.KeyCertSign, Loc.Get("Cert_KeyUsage_KeyCertSign"));
        Add(X509KeyUsageFlags.CrlSign, Loc.Get("Cert_KeyUsage_CrlSign"));
        Add(X509KeyUsageFlags.EncipherOnly, Loc.Get("Cert_KeyUsage_EncipherOnly"));
        Add(X509KeyUsageFlags.DecipherOnly, Loc.Get("Cert_KeyUsage_DecipherOnly"));
        return string.Join(Loc.Get("Common_ListSeparator"), names);
    }

    public static IReadOnlyList<string> GetEnhancedKeyUsages(X509Certificate2 certificate)
    {
        var result = new List<string>();
        X509EnhancedKeyUsageExtension? eku = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();
        if (eku is null)
        {
            return result;
        }

        foreach (Oid oid in eku.EnhancedKeyUsages)
        {
            string value = oid?.Value ?? string.Empty;
            string name = EkuNames.TryGetValue(value, out string? friendly)
                ? Loc.Get(friendly)
                : (oid?.FriendlyName ?? value);
            result.Add(name);
        }
        return result;
    }

    public static IReadOnlyList<string> GetSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var result = new List<string>();
        X509Extension? san = certificate.Extensions
            .FirstOrDefault(e => string.Equals(e.Oid?.Value, "2.5.29.17", StringComparison.Ordinal));
        if (san is null)
        {
            return result;
        }

        try
        {
            var reader = new AsnReader(san.RawData, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            while (sequence.HasData)
            {
                Asn1Tag tag = sequence.PeekTag();
                if (tag.TagClass != TagClass.ContextSpecific)
                {
                    sequence.ReadEncodedValue();
                    continue;
                }

                switch (tag.TagValue)
                {
                    case 1:
                        result.Add("Email: " + sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag));
                        break;
                    case 2:
                        result.Add("DNS: " + sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag));
                        break;
                    case 6:
                        result.Add("URI: " + sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag));
                        break;
                    case 7:
                        byte[] ip = sequence.ReadOctetString(tag);
                        result.Add("IP: " + FormatIpAddress(ip));
                        break;
                    case 0:
                        result.Add("OtherName: " + Convert.ToHexString(sequence.ReadEncodedValue().Span.ToArray()));
                        break;
                    case 4:
                        result.Add("DirName: " + Convert.ToHexString(sequence.ReadEncodedValue().Span.ToArray()));
                        break;
                    default:
                        sequence.ReadEncodedValue();
                        break;
                }
            }
        }
        catch (AsnContentException)
        {
        }

        return result;
    }

    private static string FormatIpAddress(byte[] bytes)
    {
        try
        {
            return new System.Net.IPAddress(bytes).ToString();
        }
        catch
        {
            return Convert.ToHexString(bytes);
        }
    }

    /// <summary>Readable one-line description of an arbitrary extension, used by the detail panes.</summary>
    public static string DescribeExtensionForDisplay(X509Extension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        try
        {
            return extension switch
            {
                X509BasicConstraintsExtension bc =>
                    Loc.Format(
                        "Cert_Value_BasicConstraints",
                        Loc.Get(bc.CertificateAuthority ? "Common_Yes" : "Common_No"),
                        bc.HasPathLengthConstraint ? bc.PathLengthConstraint.ToString() : Loc.Get("Common_None")),
                X509KeyUsageExtension ku => GetKeyUsageTextFromFlags(ku.KeyUsages),
                X509EnhancedKeyUsageExtension eku => string.Join(
                    Loc.Get("Common_SentenceSeparator"),
                    eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o?.FriendlyName ?? o?.Value ?? string.Empty)),
                X509SubjectKeyIdentifierExtension ski => ski.SubjectKeyIdentifier ?? string.Empty,
                _ => Convert.ToHexString(extension.RawData),
            };
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    public static string GetKeyUsageTextFromFlags(X509KeyUsageFlags flags)
    {
        var names = new List<string>();
        void Add(X509KeyUsageFlags f, string name)
        {
            if ((flags & f) != 0)
            {
                names.Add(name);
            }
        }

        Add(X509KeyUsageFlags.DigitalSignature, Loc.Get("Cert_KeyUsage_DigitalSignature"));
        Add(X509KeyUsageFlags.NonRepudiation, Loc.Get("Cert_KeyUsage_NonRepudiation"));
        Add(X509KeyUsageFlags.KeyEncipherment, Loc.Get("Cert_KeyUsage_KeyEncipherment"));
        Add(X509KeyUsageFlags.DataEncipherment, Loc.Get("Cert_KeyUsage_DataEncipherment"));
        Add(X509KeyUsageFlags.KeyAgreement, Loc.Get("Cert_KeyUsage_KeyAgreement"));
        Add(X509KeyUsageFlags.KeyCertSign, Loc.Get("Cert_KeyUsage_KeyCertSign"));
        Add(X509KeyUsageFlags.CrlSign, Loc.Get("Cert_KeyUsage_CrlSign"));
        Add(X509KeyUsageFlags.EncipherOnly, Loc.Get("Cert_KeyUsage_EncipherOnly"));
        Add(X509KeyUsageFlags.DecipherOnly, Loc.Get("Cert_KeyUsage_DecipherOnly"));
        return names.Count == 0
            ? Loc.Get("Common_None")
            : string.Join(Loc.Get("Common_ListSeparator"), names);
    }

    public static string BuildTextReport(X509Certificate2 certificate)
    {
        var sb = new StringBuilder();
        void Line(string label, string? value)
            => sb.Append("  ").Append(label.PadRight(18)).Append(": ").AppendLine(value ?? string.Empty);

        sb.AppendLine(Loc.Get("Cert_Report_Basic"));
        Line(Loc.Get("Cert_Row_Version"), "V" + certificate.Version);
        Line(Loc.Get("Cert_Row_Serial"), certificate.SerialNumber);
        Line(Loc.Get("Cert_Row_SignatureAlgorithm"), certificate.SignatureAlgorithm?.FriendlyName ?? certificate.SignatureAlgorithm?.Value);
        Line(Loc.Get("Cert_Row_ThumbprintSha1"), certificate.Thumbprint);
        try { Line(Loc.Get("Cert_Row_ThumbprintSha256"), Convert.ToHexString(SHA256.HashData(certificate.RawData))); } catch { }
        Line(Loc.Get("Cert_Row_Subject"), X500Name.Format(certificate.Subject));
        Line(Loc.Get("Cert_Row_Issuer"), X500Name.Format(certificate.Issuer));
        try { Line(Loc.Get("Cert_Row_FriendlyName"), certificate.FriendlyName); } catch { }
        sb.AppendLine();
        sb.AppendLine(Loc.Get("Cert_Report_Validity"));
        Line(Loc.Get("Cert_Row_NotBefore"), certificate.NotBefore.ToString("yyyy-MM-dd HH:mm:ss"));
        Line(Loc.Get("Cert_Row_NotAfter"), certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss"));
        Line(Loc.Get("Cert_Row_Status"), GetValidityText(certificate));
        sb.AppendLine();
        sb.AppendLine(Loc.Get("Cert_Report_PublicKey"));
        Line(Loc.Get("Cert_Row_Algorithm"), certificate.PublicKey?.Oid?.FriendlyName ?? certificate.PublicKey?.Oid?.Value);
        int keySize = GetKeySize(certificate);
        if (keySize > 0)
        {
            Line(Loc.Get("Cert_Row_KeySize"), keySize + " bit");
        }
        try { Line(Loc.Get("Cert_Row_PublicKeyBase64"), Convert.ToBase64String(certificate.PublicKey?.EncodedKeyValue.RawData ?? Array.Empty<byte>())); } catch { }
        Line(Loc.Get("Cert_Row_PrivateKey"), Loc.Get(certificate.HasPrivateKey ? "Cert_Value_Present" : "Cert_Value_Absent"));
        sb.AppendLine();
        sb.AppendLine(Loc.Get("Cert_Report_Extensions"));
        Line(Loc.Get("Cert_Row_IsCa"), Loc.Get(IsCa(certificate) ? "Common_Yes" : "Common_No"));
        X509BasicConstraintsExtension? bc = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        Line(Loc.Get("Cert_Row_PathLength"), bc is not null && bc.HasPathLengthConstraint ? bc.PathLengthConstraint.ToString() : Loc.Get("Common_None"));
        Line(Loc.Get("Cert_Row_KeyUsage"), GetKeyUsageText(certificate));
        IReadOnlyList<string> ekus = GetEnhancedKeyUsages(certificate);
        Line(Loc.Get("Cert_Row_Eku"), ekus.Count == 0 ? Loc.Get("Common_None") : string.Join("; ", ekus));
        IReadOnlyList<string> sans = GetSubjectAlternativeNames(certificate);
        Line(Loc.Get("Cert_Row_San"), sans.Count == 0 ? Loc.Get("Common_None") : string.Join("; ", sans));
        Line("SKI", GetExtensionText(certificate, "2.5.29.14"));
        Line("AKI", GetExtensionText(certificate, "2.5.29.35"));
        Line(Loc.Get("Cert_Row_Crl"), GetExtensionText(certificate, "2.5.29.31"));
        Line(Loc.Get("Cert_Row_Aia"), GetExtensionText(certificate, "1.3.6.1.5.5.7.1.1"));
        Line(Loc.Get("Cert_Row_Policies"), GetExtensionText(certificate, "2.5.29.32"));

        sb.AppendLine();
        sb.AppendLine(Loc.Get("Cert_Report_AllExtensions"));
        foreach (X509Extension ext in certificate.Extensions)
        {
            sb.Append("  ").Append(ext.Oid?.Value)
              .Append(ext.Critical ? Loc.Get("Cert_Extension_Critical") : string.Empty)
              .Append("  =>  ")
              .AppendLine(DescribeExtension(ext));
        }

        return sb.ToString();
    }

    public static string GetValidityText(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        if (now < certificate.NotBefore) return Loc.Get("Cert_Validity_NotYetValid");
        if (now > certificate.NotAfter) return Loc.Get("Cert_Validity_Expired");
        TimeSpan remaining = certificate.NotAfter - now;
        return Loc.Format("Cert_Validity_Valid", remaining.Days);
    }

    public static int GetKeySize(X509Certificate2 certificate)
    {
        try
        {
            using RSA? rsa = certificate.GetRSAPublicKey();
            if (rsa is not null) return rsa.KeySize;
            using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa is not null) return ecdsa.KeySize;
            using DSA? dsa = certificate.GetDSAPublicKey();
            if (dsa is not null) return dsa.KeySize;
        }
        catch { }
        return 0;
    }

    private static string DescribeExtension(X509Extension extension)
    {
        return extension switch
        {
            X509BasicConstraintsExtension bc =>
                $"BasicConstraints CA={bc.CertificateAuthority}, PathLen={(bc.HasPathLengthConstraint ? bc.PathLengthConstraint : -1)}",
            X509KeyUsageExtension ku => $"KeyUsage {ku.KeyUsages}",
            X509EnhancedKeyUsageExtension eku => "EKU " + string.Join(", ", eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o?.Value)),
            X509SubjectKeyIdentifierExtension ski => "SKI " + ski.SubjectKeyIdentifier,
            _ => Convert.ToHexString(extension.RawData),
        };
    }

    public static string GetExtensionText(X509Certificate2 certificate, string oid)
    {
        X509Extension? ext = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == oid);
        if (ext is null)
        {
            return Loc.Get("Common_None");
        }
        try
        {
            return Convert.ToHexString(ext.RawData);
        }
        catch
        {
            return string.Empty;
        }
    }
}
