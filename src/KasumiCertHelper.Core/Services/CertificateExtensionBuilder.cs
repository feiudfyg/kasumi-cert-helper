using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KasumiCertHelper.Core.Services;

public static class CertificateExtensionBuilder
{
    public static byte[] ComputeSubjectKeyIdentifier(PublicKey publicKey)
    {
        var extension = new X509SubjectKeyIdentifierExtension(publicKey, false);
        return Convert.FromHexString(extension.SubjectKeyIdentifier ?? string.Empty);
    }

    public static byte[]? GetKeyIdentifier(X509Certificate2 certificate)
    {
        X509SubjectKeyIdentifierExtension? existing = certificate.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault();
        if (existing?.SubjectKeyIdentifier is string ski)
        {
            try { return Convert.FromHexString(ski); }
            catch (FormatException) { }
        }

        using AsymmetricAlgorithm? key = CertificateKeyIO.GetPublicKey(certificate);
        if (key is not null)
        {
            var computed = new X509SubjectKeyIdentifierExtension(certificate.PublicKey, false);
            return Convert.FromHexString(computed.SubjectKeyIdentifier ?? string.Empty);
        }
        return null;
    }

    public static List<X509Extension> Build(X509CertificateOptions options, PublicKey subjectPublicKey, byte[]? issuerKeyIdentifier)
    {
        var extensions = new List<X509Extension>();
        bool isCa = options.IsCa;

        bool hasPathLength = isCa && options.HasPathLengthConstraint;
        int pathLength = hasPathLength ? Math.Max(0, options.PathLengthConstraint) : 0;
        extensions.Add(new X509BasicConstraintsExtension(isCa, hasPathLength, pathLength, isCa));

        X509KeyUsageFlags usage = options.KeyUsage;
        if (isCa && (usage & (X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign)) == 0)
        {
            usage |= X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
        }
        if (options.IncludeKeyUsage && usage != 0)
        {
            extensions.Add(new X509KeyUsageExtension(usage, true));
        }

        if (options.ExtendedKeyUsageOids.Count > 0)
        {
            var oids = new OidCollection();
            foreach (string oid in options.ExtendedKeyUsageOids)
            {
                if (!string.IsNullOrWhiteSpace(oid))
                {
                    oids.Add(new Oid(oid.Trim()));
                }
            }
            if (oids.Count > 0)
            {
                extensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
            }
        }

        byte[] ownSki = ComputeSubjectKeyIdentifier(subjectPublicKey);
        extensions.Add(new X509SubjectKeyIdentifierExtension(ownSki, false));

        byte[]? authorityKeyId = issuerKeyIdentifier is { Length: > 0 } ? issuerKeyIdentifier : (isCa ? ownSki : null);
        if (authorityKeyId is { Length: > 0 })
        {
            extensions.Add(new X509Extension("2.5.29.35", BuildAuthorityKeyIdentifier(authorityKeyId), false));
        }

        if (options.SubjectAlternativeNames.Count > 0)
        {
            X509Extension? san = BuildSubjectAlternativeName(options.SubjectAlternativeNames);
            if (san is not null)
            {
                extensions.Add(san);
            }
        }

        if (options.CrlDistributionPoints.Count > 0)
        {
            extensions.Add(new X509Extension("2.5.29.31", BuildCrlDistributionPoints(options.CrlDistributionPoints), false));
        }

        if (options.OcspUrls.Count > 0 || options.CaIssuerUrls.Count > 0)
        {
            extensions.Add(new X509Extension("1.3.6.1.5.5.7.1.1", BuildAuthorityInfoAccess(options.OcspUrls, options.CaIssuerUrls), false));
        }

        if (options.CertificatePolicies.Count > 0)
        {
            extensions.Add(new X509Extension("2.5.29.32", BuildCertificatePolicies(options.CertificatePolicies), false));
        }

        return extensions;
    }

    private static byte[] BuildAuthorityKeyIdentifier(byte[] keyIdentifier)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteOctetString(keyIdentifier, new Asn1Tag(TagClass.ContextSpecific, 0));
        writer.PopSequence();
        return writer.Encode();
    }

    private static X509Extension? BuildSubjectAlternativeName(List<SanEntry> entries)
    {
        var builder = new SubjectAlternativeNameBuilder();
        bool any = false;
        foreach (SanEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }
            try
            {
                switch (entry.Kind)
                {
                    case SanKind.Dns:
                        builder.AddDnsName(entry.Value.Trim());
                        break;
                    case SanKind.Ip:
                        builder.AddIpAddress(IPAddress.Parse(entry.Value.Trim()));
                        break;
                    case SanKind.Email:
                        builder.AddEmailAddress(entry.Value.Trim());
                        break;
                    case SanKind.Uri:
                        builder.AddUri(new Uri(entry.Value.Trim()));
                        break;
                    case SanKind.Upn:
                        builder.AddUserPrincipalName(entry.Value.Trim());
                        break;
                }
                any = true;
            }
            catch (Exception)
            {
            }
        }
        return any ? builder.Build(false) : null;
    }

    private static byte[] BuildCrlDistributionPoints(IEnumerable<string> urls)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        // DistributionPoint ::= SEQUENCE { distributionPoint [0] DistributionPointName, ... }
        // DistributionPointName ::= CHOICE { fullName [0] GeneralNames }
        // GeneralName ::= CHOICE { uniformResourceIdentifier [6] IA5String }
        var distributionPointName = new Asn1Tag(TagClass.ContextSpecific, 0);

        writer.PushSequence();
        foreach (string url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            writer.PushSequence();
            writer.PushSequence(distributionPointName);
            writer.PushSequence(distributionPointName);
            writer.WriteCharacterString(UniversalTagNumber.IA5String, url.Trim(), new Asn1Tag(TagClass.ContextSpecific, 6));
            writer.PopSequence(distributionPointName);
            writer.PopSequence(distributionPointName);
            writer.PopSequence();
        }
        writer.PopSequence();
        return writer.Encode();
    }

    private static byte[] BuildAuthorityInfoAccess(IEnumerable<string> ocspUrls, IEnumerable<string> caIssuerUrls)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        WriteAccessDescriptions(writer, "1.3.6.1.5.5.7.48.1", ocspUrls);
        WriteAccessDescriptions(writer, "1.3.6.1.5.5.7.48.2", caIssuerUrls);
        writer.PopSequence();
        return writer.Encode();

        static void WriteAccessDescriptions(AsnWriter writer, string method, IEnumerable<string> urls)
        {
            foreach (string url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                writer.PushSequence();
                writer.WriteObjectIdentifier(method);
                writer.WriteCharacterString(UniversalTagNumber.IA5String, url.Trim(), new Asn1Tag(TagClass.ContextSpecific, 6));
                writer.PopSequence();
            }
        }
    }

    private static byte[] BuildCertificatePolicies(IEnumerable<string> oids)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        foreach (string oid in oids.Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            writer.PushSequence();
            writer.WriteObjectIdentifier(oid.Trim());
            writer.PopSequence();
        }
        writer.PopSequence();
        return writer.Encode();
    }
}
