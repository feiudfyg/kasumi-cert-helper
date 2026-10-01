using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;
using System.Text;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public static class CertificateSummaryBuilder
{
    private const string SanOid = "2.5.29.17";
    private const string SkiOid = "2.5.29.14";
    private const string AkiOid = "2.5.29.35";
    private const string CrlOid = "2.5.29.31";
    private const string AiaOid = "1.3.6.1.5.5.7.1.1";
    private const string PoliciesOid = "2.5.29.32";

    public static CertificateSummary Build(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        X509BasicConstraintsExtension? basicConstraints = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .FirstOrDefault();

        CertificateSummary summary = new()
        {
            Version = certificate.Version,
            SerialNumber = certificate.SerialNumber,
            SignatureAlgorithm = certificate.SignatureAlgorithm?.FriendlyName
                                ?? certificate.SignatureAlgorithm?.Value
                                ?? string.Empty,
            ThumbprintSha1 = FormatHex(certificate.Thumbprint),
            ThumbprintSha256 = FormatHex(TrySha256(certificate)),
            Subject = X500Name.Format(certificate.Subject),
            SubjectCommonName = X500Name.GetCommonName(certificate.Subject),
            Issuer = X500Name.Format(certificate.Issuer),
            IssuerCommonName = X500Name.GetCommonName(certificate.Issuer),
            FriendlyName = TryFriendlyName(certificate),
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            ValidityText = CertificateDetailsBuilder.GetValidityText(certificate),
            RemainingDays = (int)Math.Floor((certificate.NotAfter - DateTime.Now).TotalDays),
            StatusText = StatusOf(certificate),
            PublicKeyAlgorithm = certificate.PublicKey?.Oid?.FriendlyName
                                 ?? certificate.PublicKey?.Oid?.Value
                                 ?? string.Empty,
            KeySize = CertificateDetailsBuilder.GetKeySize(certificate),
            HasPrivateKey = certificate.HasPrivateKey,
            IsCertificateAuthority = basicConstraints?.CertificateAuthority == true,
            PathLengthConstraint = basicConstraints is { HasPathLengthConstraint: true }
                ? basicConstraints.PathLengthConstraint
                : null,
            KeyUsageText = CertificateDetailsBuilder.GetKeyUsageText(certificate),
            EnhancedKeyUsages = CertificateDetailsBuilder.GetEnhancedKeyUsages(certificate),
            SubjectAlternativeNames = CertificateDetailsBuilder.GetSubjectAlternativeNames(certificate),
            SubjectKeyIdentifier = DescribeSki(certificate),
            AuthorityKeyIdentifier = DescribeAki(certificate),
            CrlDistributionPoints = CollectUris(certificate, CrlOid).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            AuthorityInformationAccess = CollectUris(certificate, AiaOid).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CertificatePolicies = CollectOids(certificate, PoliciesOid).Distinct(StringComparer.Ordinal).ToList(),
            OtherExtensions = DescribeRemainingExtensions(certificate),
            RawTextReport = CertificateDetailsBuilder.BuildTextReport(certificate),
        };

        return summary;
    }

    private static string StatusOf(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        if (now < certificate.NotBefore)
        {
            return Loc.Get("Cert_Validity_NotYetValid");
        }
        if (now > certificate.NotAfter)
        {
            return Loc.Get("Cert_Validity_Expired");
        }
        int days = (int)Math.Floor((certificate.NotAfter - now).TotalDays);
        return days <= 30 ? Loc.Format("Cert_Validity_ExpiringSoon", days) : Loc.Get("Cert_Validity_Ok");
    }

    private static string TryFriendlyName(X509Certificate2 certificate)
    {
        try
        {
            return certificate.FriendlyName ?? string.Empty;
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    private static string TrySha256(X509Certificate2 certificate)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(certificate.RawData));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    public static string FormatHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string cleaned = value.Replace(":", string.Empty).Replace(" ", string.Empty).Trim();
        var sb = new StringBuilder(cleaned.Length + (cleaned.Length / 2));
        for (int i = 0; i < cleaned.Length; i += 2)
        {
            if (sb.Length > 0)
            {
                sb.Append(':');
            }
            sb.Append(cleaned.AsSpan(i, Math.Min(2, cleaned.Length - i)));
        }
        return sb.ToString().ToUpperInvariant();
    }

    private static string DescribeSki(X509Certificate2 certificate)
    {
        X509SubjectKeyIdentifierExtension? ski = certificate.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault();
        return ski is null ? string.Empty : FormatHex(ski.SubjectKeyIdentifier);
    }

    private static string DescribeAki(X509Certificate2 certificate)
    {
        X509Extension? extension = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == AkiOid);
        if (extension is null)
        {
            return string.Empty;
        }

        try
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            while (sequence.HasData)
            {
                Asn1Tag tag = sequence.PeekTag();
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 0)
                {
                    return FormatHex(Convert.ToHexString(sequence.ReadOctetString(tag)));
                }

                sequence.ReadEncodedValue();
            }
        }
        catch (AsnContentException)
        {
        }

        return string.Empty;
    }

    private static List<string> CollectUris(X509Certificate2 certificate, string oid)
    {
        var values = new List<string>();
        X509Extension? extension = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == oid);
        if (extension is not null)
        {
            Walk(extension.RawData, values, null);
        }
        return values;
    }

    private static List<string> CollectOids(X509Certificate2 certificate, string oid)
    {
        var values = new List<string>();
        X509Extension? extension = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == oid);
        if (extension is not null)
        {
            Walk(extension.RawData, null, values);
        }
        return values;
    }

    /// <summary>Recursively pulls every URI / OID out of an extension, whatever its shape.</summary>
    private static void Walk(byte[] rawData, List<string>? uris, List<string>? oids)
    {
        try
        {
            var reader = new AsnReader(rawData, AsnEncodingRules.DER);
            WalkReader(reader, uris, oids, 0);
        }
        catch (AsnContentException)
        {
        }
    }

    private static void WalkReader(AsnReader reader, List<string>? uris, List<string>? oids, int depth)
    {
        if (depth > 6)
        {
            return;
        }

        while (reader.HasData)
        {
            Asn1Tag tag = reader.PeekTag();

            if (tag.TagClass == TagClass.Universal)
            {
                switch ((UniversalTagNumber)tag.TagValue)
                {
                    case UniversalTagNumber.ObjectIdentifier:
                    {
                        // Read first, add later: the value must always be consumed or the reader
                        // stops advancing and the loop spins forever.
                        string oid = reader.ReadObjectIdentifier(tag);
                        oids?.Add(oid);
                        continue;
                    }
                    case UniversalTagNumber.IA5String:
                    {
                        string value = reader.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                        uris?.Add(value);
                        continue;
                    }
                    case UniversalTagNumber.Sequence:
                        WalkReader(reader.ReadSequence(tag), uris, oids, depth + 1);
                        continue;
                    case UniversalTagNumber.Set:
                        WalkReader(reader.ReadSetOf(tag), uris, oids, depth + 1);
                        continue;
                }

                reader.ReadEncodedValue();
                continue;
            }

            if (tag.IsConstructed)
            {
                WalkReader(reader.ReadSequence(tag), uris, oids, depth + 1);
                continue;
            }

            if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue is 1 or 2 or 6)
            {
                // GeneralName ::= CHOICE { rfc822Name [1], dNSName [2], uniformResourceIdentifier [6] }
                try
                {
                    string text = reader.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                    uris?.Add(text);
                    continue;
                }
                catch (AsnContentException)
                {
                    // Not actually an IA5String; fall through and skip the raw value.
                }
            }

            reader.ReadEncodedValue();
        }
    }

    private static List<string> DescribeRemainingExtensions(X509Certificate2 certificate)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal)
        {
            "2.5.29.19", SanOid, SkiOid, AkiOid, CrlOid, AiaOid, PoliciesOid, "2.5.29.15", "2.5.29.37",
        };

        var lines = new List<string>();
        foreach (X509Extension extension in certificate.Extensions)
        {
            string oid = extension.Oid?.Value ?? string.Empty;
            if (handled.Contains(oid))
            {
                continue;
            }

            string name = extension.Oid?.FriendlyName ?? oid;
            lines.Add(extension.Critical ? name + Loc.Get("Cert_Suffix_Critical") : name);
        }

        return lines;
    }
}
