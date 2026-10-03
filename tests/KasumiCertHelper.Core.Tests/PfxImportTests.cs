using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// A password protected PFX holds both halves of the key. Importing it used to fail with
/// "the private key cannot be unpacked" even though the passphrase was right: the file was imported
/// with the default key storage flags, which hands back a Windows CNG key that can decrypt but not
/// be exported, and the database stores keys by exporting them.
/// </summary>
public sealed class PfxImportTests
{
    [Theory]
    [InlineData(X509KeyAlgorithm.Rsa)]
    [InlineData(X509KeyAlgorithm.Ecdsa)]
    public void PasswordProtectedPfxKeepsAnExportablePrivateKey(X509KeyAlgorithm algorithm)
    {
        using AsymmetricAlgorithm key = X509Factory.CreateKey(new X509KeyOptions
        {
            Algorithm = algorithm,
            KeySize = 2048,
        });
        using X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
        {
            Subject = "CN=pfx.example.com",
            ValidDays = 10,
        });

        byte[] pfx = certificate.Export(X509ContentType.Pkcs12, "pfx-password")!;
        Assert.True(pfx.Length > 0);

        IReadOnlyList<X509Certificate2> loaded = CertificateFileIO.Load(pfx, "pfx-password");
        X509Certificate2 imported = loaded[0];
        Assert.True(imported.HasPrivateKey, "导入后没有私钥。");

        using AsymmetricAlgorithm? privateKey = CertificateKeyIO.GetPrivateKey(imported);
        Assert.NotNull(privateKey);

        // Storing the key is what re-exports it; this threw before the flags were fixed.
        string directory = Path.Combine(Path.GetTempPath(), "kasumi-pfx-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string file = Path.Combine(directory, "pfx.kdb");
            X509Database database = X509Database.Create(file, "db-password");
            X509Item keyItem = database.AddKey("导入的密钥", privateKey!);
            database.AddCertificate("导入的证书", imported, keyItem.Id);

            X509Database reopened = X509Database.Open(file, "db-password");
            X509Item reloadedCert = reopened.Items.First(i => i.Kind == X509ItemKind.Certificate);
            using X509Certificate2? roundTripped = reopened.GetCertificateWithKey(reloadedCert);

            Assert.NotNull(roundTripped);
            Assert.True(roundTripped!.HasPrivateKey, "重新打开数据库后证书丢失了私钥。");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WrongPfxPasswordIsRejected()
    {
        using AsymmetricAlgorithm key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        using X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
        {
            Subject = "CN=pfx-wrong.example.com",
            ValidDays = 10,
        });

        byte[] pfx = certificate.Export(X509ContentType.Pkcs12, "right-password")!;

        Assert.Throws<CryptographicException>(() => CertificateFileIO.Load(pfx, "wrong-password"));
    }
}
