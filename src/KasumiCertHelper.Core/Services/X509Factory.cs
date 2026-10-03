using System.Security.Cryptography;
using KasumiCertHelper.Core.Localization;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public static class X509Factory
{
    public static readonly string[] HashAlgorithms = { "SHA256", "SHA384", "SHA512", "SHA1" };

    /// <summary>A signature hash for a combo box: the stored value and the text to show for it.</summary>
    public sealed record HashAlgorithmChoice(string Value, string Display);

    /// <summary>
    /// SHA-1 stays available only for interoperability with systems that cannot do better, so the list
    /// says so instead of offering it as if it were a normal choice.
    /// </summary>
    public static IReadOnlyList<HashAlgorithmChoice> HashAlgorithmChoices() =>
        HashAlgorithms
            .Select(name => new HashAlgorithmChoice(
                name,
                string.Equals(name, "SHA1", StringComparison.OrdinalIgnoreCase)
                    ? Loc.Format("X509_HashAlgorithmLegacy", name)
                    : name))
            .ToList();

    public static readonly string[] EcdsaCurves =
    {
        "nistP256", "nistP384", "nistP521", "secp256k1", "brainpoolP256r1", "brainpoolP384r1", "brainpoolP512r1",
    };

    // Names are resource keys; resolve them with Loc.Get before showing them.
    public static readonly (string NameKey, string Oid)[] ExtendedKeyUsageChoices =
    {
        ("Cert_Eku_ServerAuth", "1.3.6.1.5.5.7.3.1"),
        ("Cert_Eku_ClientAuth", "1.3.6.1.5.5.7.3.2"),
        ("Cert_Eku_CodeSigning", "1.3.6.1.5.5.7.3.3"),
        ("Cert_Eku_EmailProtection", "1.3.6.1.5.5.7.3.4"),
        ("Cert_Eku_TimeStamping", "1.3.6.1.5.5.7.3.8"),
        ("Cert_Eku_OcspSigning", "1.3.6.1.5.5.7.3.9"),
        ("Cert_Eku_Any", "2.5.29.37.0"),
        ("Cert_Eku_Efs", "1.3.6.1.4.1.311.10.3.4"),
        ("Cert_Eku_DocumentSigning", "1.3.6.1.4.1.311.10.3.12"),
        ("Cert_Eku_SmartCardLogon", "1.3.6.1.4.1.311.20.2.2"),
    };

    public static AsymmetricAlgorithm CreateKey(X509KeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (options.Algorithm)
        {
            case X509KeyAlgorithm.Rsa:
                return RSA.Create(options.KeySize <= 0 ? 4096 : options.KeySize);
            case X509KeyAlgorithm.Ecdsa:
                return ECDsa.Create(ResolveCurve(options.Curve));
            default:
                throw new NotSupportedException(Loc.Get("Error_UnsupportedKeyAlgorithm"));
        }
    }

    public static string GetKeyAlgorithmName(AsymmetricAlgorithm key) => key switch
    {
        RSA => "RSA",
        ECDsa => "ECDSA",
        DSA => "DSA",
        _ => key.GetType().Name,
    };

    public static int GetKeySize(AsymmetricAlgorithm key) => key.KeySize;

    public static ECCurve ResolveCurve(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return trimmed switch
        {
            "nistP256" or "P-256" or "prime256v1" or "secp256r1" => ECCurve.NamedCurves.nistP256,
            "nistP384" or "P-384" or "secp384r1" => ECCurve.NamedCurves.nistP384,
            "nistP521" or "P-521" or "secp521r1" => ECCurve.NamedCurves.nistP521,
            "secp256k1" => ECCurve.CreateFromValue("1.3.132.0.10"),
            "brainpoolP256r1" => ECCurve.NamedCurves.brainpoolP256r1,
            "brainpoolP384r1" => ECCurve.NamedCurves.brainpoolP384r1,
            "brainpoolP512r1" => ECCurve.NamedCurves.brainpoolP512r1,
            _ => ECCurve.CreateFromFriendlyName(trimmed),
        };
    }

    public static HashAlgorithmName ResolveHash(string? name) => (name ?? "SHA256").Trim().ToUpperInvariant() switch
    {
        "SHA1" or "SHA-1" => HashAlgorithmName.SHA1,
        "SHA256" or "SHA-256" => HashAlgorithmName.SHA256,
        "SHA384" or "SHA-384" => HashAlgorithmName.SHA384,
        "SHA512" or "SHA-512" => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.SHA256,
    };

    public static byte[] GenerateSerialNumber()
    {
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        if (serial[0] == 0)
        {
            serial[0] = 0x01;
        }
        return serial;
    }

    /// <summary>What a PEM text holds; a file may combine a key with its certificate.</summary>
    [Flags]
    public enum PemContentKind
    {
        None = 0,
        PrivateKey = 1,
        Csr = 2,
        Certificate = 4,
    }

    /// <summary>
    /// Classifies PEM text. "CERTIFICATE REQUEST" contains "BEGIN CERTIFICATE", so the markers have to
    /// be matched in full, otherwise a CSR would also be read as a certificate.
    /// </summary>
    public static PemContentKind ClassifyPem(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return PemContentKind.None;
        }

        PemContentKind kind = PemContentKind.None;
        if (text.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            kind |= PemContentKind.PrivateKey;
        }

        bool isCsr = text.Contains("CERTIFICATE REQUEST", StringComparison.Ordinal);
        if (isCsr)
        {
            kind |= PemContentKind.Csr;
        }
        else if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            kind |= PemContentKind.Certificate;
        }

        return kind;
    }

    public static byte[]? ParseSerial(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }
        string cleaned = new(hex.Where(Uri.IsHexDigit).ToArray());
        if (cleaned.Length == 0)
        {
            return null;
        }
        if (cleaned.Length % 2 != 0)
        {
            cleaned = "0" + cleaned;
        }

        byte[] serial = Convert.FromHexString(cleaned);

        // A certificate serial is a DER INTEGER, so it has to be positive and non-zero: leading zero
        // bytes are dropped, a set sign bit gets a zero byte in front of it, and "0" becomes 1.
        int start = 0;
        while (start < serial.Length - 1 && serial[start] == 0)
        {
            start++;
        }
        serial = serial[start..];

        if (serial.All(b => b == 0))
        {
            return new byte[] { 0x01 };
        }
        if ((serial[0] & 0x80) != 0)
        {
            byte[] positive = new byte[serial.Length + 1];
            serial.CopyTo(positive, 1);
            return positive;
        }
        return serial;
    }

    public static X509SignatureGenerator CreateSignatureGenerator(AsymmetricAlgorithm key, HashAlgorithmName hash)
        => key switch
        {
            RSA rsa => X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => X509SignatureGenerator.CreateForECDsa(ecdsa),
            _ => throw new NotSupportedException(Loc.Get("Error_KeyAlgorithmCannotSign")),
        };

    public static CertificateRequest CreateRequest(AsymmetricAlgorithm key, X509CertificateOptions options)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(options);

        var subject = new X500DistinguishedName(options.Subject);
        HashAlgorithmName hash = ResolveHash(options.HashAlgorithm);

        CertificateRequest request = key switch
        {
            RSA rsa => new CertificateRequest(subject, rsa, hash, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => new CertificateRequest(subject, ecdsa, hash),
            _ => throw new NotSupportedException(Loc.Get("Error_UnsupportedKeyAlgorithm")),
        };

        PublicKey publicKey = request.PublicKey;
        byte[] ski = CertificateExtensionBuilder.ComputeSubjectKeyIdentifier(publicKey);
        foreach (X509Extension extension in CertificateExtensionBuilder.Build(options, publicKey, ski))
        {
            request.CertificateExtensions.Add(extension);
        }

        return request;
    }

    public static X509Certificate2 CreateSelfSigned(AsymmetricAlgorithm key, X509CertificateOptions options, bool attachPrivateKey = true)
    {
        CertificateRequest request = CreateRequest(key, options);
        DateTimeOffset notBefore = options.ResolveNotBefore();
        DateTimeOffset notAfter = options.ResolveNotAfter();
        if (notAfter <= notBefore)
        {
            notAfter = notBefore.AddDays(1);
        }

        byte[]? customSerial = ParseSerial(options.SerialNumberHex);
        X509Certificate2 certificate = customSerial is not null
            ? request.Create(request.SubjectName, CreateSignatureGenerator(key, ResolveHash(options.HashAlgorithm)), notBefore, notAfter, customSerial)
            : request.CreateSelfSigned(notBefore, notAfter);

        if (attachPrivateKey && !certificate.HasPrivateKey)
        {
            certificate = CertificateKeyIO.CopyWithPrivateKey(certificate, key);
        }

        return certificate;
    }

    public static X509Certificate2 SignCsr(CertificateRequest csrRequest, X509Certificate2 issuerCertificate, X509CertificateOptions options)
    {
        ArgumentNullException.ThrowIfNull(csrRequest);
        ArgumentNullException.ThrowIfNull(issuerCertificate);
        ArgumentNullException.ThrowIfNull(options);

        if (!issuerCertificate.HasPrivateKey)
        {
            throw new CryptographicException(Loc.Get("Error_IssuerHasNoPrivateKey"));
        }

        DateTimeOffset notBefore = options.ResolveNotBefore();
        DateTimeOffset notAfter = options.ResolveNotAfter();

        // A certificate can never outlive (or predate) its issuer.
        DateTimeOffset issuerNotBefore = issuerCertificate.NotBefore;
        DateTimeOffset issuerNotAfter = issuerCertificate.NotAfter;
        if (notAfter > issuerNotAfter)
        {
            notAfter = issuerNotAfter;
        }
        if (notBefore < issuerNotBefore)
        {
            notBefore = issuerNotBefore;
        }

        if (notAfter <= notBefore)
        {
            throw new CryptographicException(
                Loc.Format("Error_ValidityExceedsIssuer", issuerCertificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        byte[] serial = ParseSerial(options.SerialNumberHex) ?? GenerateSerialNumber();
        byte[]? issuerSki = CertificateExtensionBuilder.GetKeyIdentifier(issuerCertificate);

        foreach (X509Extension extension in CertificateExtensionBuilder.Build(options, csrRequest.PublicKey, issuerSki))
        {
            X509Extension? existing = csrRequest.CertificateExtensions
                .FirstOrDefault(e => e.Oid?.Value == extension.Oid?.Value);
            if (existing is not null)
            {
                csrRequest.CertificateExtensions.Remove(existing);
            }
            csrRequest.CertificateExtensions.Add(extension);
        }

        return csrRequest.Create(issuerCertificate, notBefore, notAfter, serial);
    }

    public static CertificateRequest LoadCsr(string csrPem, string? hashAlgorithm = null)
        => CertificateRequest.LoadSigningRequestPem(
            csrPem,
            ResolveHash(hashAlgorithm),
            CertificateRequestLoadOptions.Default,
            RSASignaturePadding.Pkcs1);

    public static string ExportCsrPem(CertificateRequest request) => request.CreateSigningRequestPem();

    public static AsymmetricAlgorithm LoadPrivateKeyFromPem(string pem, string? password)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new CryptographicException(Loc.Get("Error_EmptyPrivateKey"));
        }

        IEnumerable<Func<AsymmetricAlgorithm>> factories = new Func<AsymmetricAlgorithm>[]
        {
            () => RSA.Create(),
            () => ECDsa.Create(),
            () => DSA.Create(),
        };

        bool encrypted = pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        if (encrypted && string.IsNullOrEmpty(password))
        {
            throw new CryptographicException(Loc.Get("Error_PrivateKeyEncrypted"));
        }

        Exception? lastError = null;
        foreach (Func<AsymmetricAlgorithm> factory in factories)
        {
            AsymmetricAlgorithm key = factory();
            try
            {
                if (encrypted)
                {
                    key.ImportFromEncryptedPem(pem, password);
                }
                else
                {
                    key.ImportFromPem(pem);
                }
                return key;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                lastError = ex;
                key.Dispose();
            }
        }

        throw new CryptographicException(Loc.Get("Error_UnrecognizedPrivateKeyFormat"), lastError);
    }
}
