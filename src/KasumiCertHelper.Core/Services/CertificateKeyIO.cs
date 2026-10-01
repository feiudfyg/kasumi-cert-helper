using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Services;

public static class CertificateKeyIO
{
    public static readonly PbeParameters DefaultPbe =
        new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);

    public static AsymmetricAlgorithm? GetPrivateKey(X509Certificate2 certificate)
    {
        AsymmetricAlgorithm? key = certificate.GetRSAPrivateKey();
        key ??= certificate.GetECDsaPrivateKey();
        key ??= certificate.GetDSAPrivateKey();
        return key;
    }

    public static AsymmetricAlgorithm? GetPublicKey(X509Certificate2 certificate)
    {
        AsymmetricAlgorithm? key = certificate.GetRSAPublicKey();
        key ??= certificate.GetECDsaPublicKey();
        key ??= certificate.GetDSAPublicKey();
        return key;
    }

    public static X509Certificate2 CopyWithPrivateKey(X509Certificate2 certificate, AsymmetricAlgorithm key)
        => key switch
        {
            RSA rsa => certificate.CopyWithPrivateKey(rsa),
            ECDsa ecdsa => certificate.CopyWithPrivateKey(ecdsa),
            DSA dsa => certificate.CopyWithPrivateKey(dsa),
            _ => throw new NotSupportedException(Loc.Get("Error_UnsupportedKeyAlgorithm")),
        };

    public static string ExportPrivateKeyPem(X509Certificate2 certificate, string? password)
    {
        using AsymmetricAlgorithm? key = GetPrivateKey(certificate);
        if (key is null)
        {
            throw new CryptographicException(Loc.Get("Error_CertificateHasNoPrivateKey"));
        }

        return string.IsNullOrEmpty(password)
            ? key.ExportPkcs8PrivateKeyPem()
            : key.ExportEncryptedPkcs8PrivateKeyPem(password, DefaultPbe);
    }

    public static string ExportPublicKeyPem(X509Certificate2 certificate)
    {
        using AsymmetricAlgorithm? key = GetPublicKey(certificate);
        if (key is null)
        {
            throw new CryptographicException(Loc.Get("Error_CannotReadPublicKey"));
        }
        return key.ExportSubjectPublicKeyInfoPem();
    }
}
