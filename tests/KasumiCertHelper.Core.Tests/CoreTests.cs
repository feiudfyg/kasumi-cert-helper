using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

public class X509GenerationTests
{
    /// <summary>The assertions below are written against the Chinese table.</summary>
    public X509GenerationTests() => Loc.SetCulture("zh-CN");

    [Fact]
    public void RsaSelfSignedCertificate_HasExpectedProperties()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        var options = new X509CertificateOptions
        {
            Subject = new X509SubjectOptions { CommonName = "test.example.com", Organization = "Kasumi", Country = "CN" }.BuildDistinguishedName(),
            ValidDays = 30,
            HashAlgorithm = "SHA256",
            SubjectAlternativeNames = { new SanEntry { Kind = SanKind.Dns, Value = "test.example.com" } },
            ExtendedKeyUsageOids = { "1.3.6.1.5.5.7.3.1" },
        };

        X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);

        Assert.True(certificate.HasPrivateKey);
        Assert.Contains("test.example.com", certificate.Subject);
        Assert.Equal(certificate.Subject, certificate.Issuer);
        Assert.True(certificate.NotAfter > DateTime.Now);
        Assert.False(CertificateDetailsBuilder.IsCa(certificate));
        Assert.Contains("DNS: test.example.com", CertificateDetailsBuilder.GetSubjectAlternativeNames(certificate));
        Assert.Contains(certificate.Extensions, e => e.Oid?.Value == "2.5.29.14");
        Assert.Contains(certificate.Extensions, e => e.Oid?.Value == "2.5.29.35");
    }

    [Fact]
    public void EcSelfSignedCertificate_CreatesValidChain()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa, Curve = "nistP256" });
        var options = new X509CertificateOptions
        {
            Subject = "CN=ec.example.com",
            IsCa = true,
            HasPathLengthConstraint = true,
            PathLengthConstraint = 0,
            ValidDays = 3650,
        };

        X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);

        Assert.True(CertificateDetailsBuilder.IsCa(certificate));
        X509BasicConstraintsExtension bc = certificate.Extensions.OfType<X509BasicConstraintsExtension>().First();
        Assert.True(bc.CertificateAuthority);
        Assert.True(bc.HasPathLengthConstraint);
        Assert.Equal(0, bc.PathLengthConstraint);
    }

    [Fact]
    public void CaSignsCsr_ProducesVerifiableCertificate()
    {
        using AsymmetricAlgorithm caKey = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        X509Certificate2 caCertificate = X509Factory.CreateSelfSigned(caKey, new X509CertificateOptions
        {
            Subject = "CN=Test Root CA,O=Kasumi",
            IsCa = true,
            ValidDays = 3650,
        });

        using AsymmetricAlgorithm leafKey = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        var csrOptions = new X509CertificateOptions
        {
            Subject = "CN=leaf.example.com",
            SubjectAlternativeNames = { new SanEntry { Kind = SanKind.Dns, Value = "leaf.example.com" } },
        };
        CertificateRequest csr = X509Factory.CreateRequest(leafKey, csrOptions);
        string csrPem = X509Factory.ExportCsrPem(csr);

        CertificateRequest reloaded = X509Factory.LoadCsr(csrPem, "SHA256");
        X509Certificate2 signed = X509Factory.SignCsr(reloaded, caCertificate, new X509CertificateOptions
        {
            Subject = "CN=leaf.example.com",
            ValidDays = 30,
            SubjectAlternativeNames = { new SanEntry { Kind = SanKind.Dns, Value = "leaf.example.com" } },
        });

        Assert.Contains("Test Root CA", signed.Issuer);
        Assert.Contains("leaf.example.com", signed.Subject);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        bool built = chain.Build(signed);
        Assert.True(built, string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }

    [Fact]
    public void SigningClampsValidityToIssuerExpiry()
    {
        using AsymmetricAlgorithm caKey = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        using AsymmetricAlgorithm leafKey = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });

        X509Certificate2 ca = X509Factory.CreateSelfSigned(caKey, new X509CertificateOptions
        {
            Subject = "CN=Short Lived CA",
            IsCa = true,
            ValidDays = 30,
        });

        CertificateRequest csr = X509Factory.CreateRequest(leafKey, new X509CertificateOptions { Subject = "CN=leaf.example.com" });
        CertificateRequest reloaded = X509Factory.LoadCsr(X509Factory.ExportCsrPem(csr), "SHA256");

        X509Certificate2 signed = X509Factory.SignCsr(reloaded, ca, new X509CertificateOptions
        {
            Subject = "CN=leaf.example.com",
            ValidDays = 3650,
        });

        Assert.True(
            signed.NotAfter <= ca.NotAfter,
            $"子证书 NotAfter ({signed.NotAfter:yyyy-MM-dd HH:mm:ss}) 超过了 CA 的 NotAfter ({ca.NotAfter:yyyy-MM-dd HH:mm:ss})。");
        Assert.True(signed.NotBefore >= ca.NotBefore, "子证书 NotBefore 早于 CA 的 NotBefore。");
    }

    [Fact]
    public void Database_RoundTripsItemsAndKeys()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kasumi-db-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "test.kdb");
        try
        {
            X509Database database = X509Database.Create(file, "p@ssw0rd");
            using AsymmetricAlgorithm key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
            X509Item keyItem = database.AddKey("测试密钥", key);
            X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
            {
                Subject = "CN=roundtrip.example.com",
                ValidDays = 10,
            });
            X509Item certItem = database.AddCertificate("测试证书", certificate, keyItem.Id);

            X509Database reopened = X509Database.Open(file, "p@ssw0rd");
            Assert.Equal(2, reopened.Items.Count);

            X509Item reloadedCert = reopened.Items.First(i => i.Kind == X509ItemKind.Certificate);
            X509Certificate2? withKey = reopened.GetCertificateWithKey(reloadedCert);
            Assert.NotNull(withKey);
            Assert.True(withKey!.HasPrivateKey);
            Assert.Contains("roundtrip.example.com", withKey.Subject);

            byte[] pfx = withKey.Export(X509ContentType.Pkcs12)!;
            Assert.True(pfx.Length > 0, "从数据库取出的证书无法导出为 PKCS#12。");
            using var reimported = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
            Assert.True(reimported.HasPrivateKey);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Database_RejectsTheWrongPasswordEvenWithoutAnyPrivateKey()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kasumi-db-pw-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "empty.kdb");
        try
        {
            X509Database created = X509Database.Create(file, "correct-horse");
            Assert.Empty(created.Items);

            Assert.True(X509Database.Open(file, "correct-horse").ValidatePassword("correct-horse"));
            Assert.False(X509Database.Open(file, "correct-horse").ValidatePassword("wrong"));
            Assert.Throws<WrongPasswordException>(() => X509Database.Open(file, "wrong"));
            Assert.False(X509Database.Open(file, "correct-horse").ValidatePassword("Wrong"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Database_RemembersANewPassword()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kasumi-db-pw2-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "change.kdb");
        try
        {
            X509Database database = X509Database.Create(file, "first-password");
            using AsymmetricAlgorithm key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa });
            database.AddKey("测试密钥", key);

            database.ChangePassword("first-password", "second-password");

            Assert.Throws<WrongPasswordException>(() => X509Database.Open(file, "first-password"));
            X509Database reopened = X509Database.Open(file, "second-password");
            Assert.Single(reopened.Items);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("01", "01")]
    [InlineData("00", "01")]
    [InlineData("0000", "01")]
    [InlineData("7F", "7F")]
    [InlineData("FF", "00FF")]
    [InlineData("00FF", "00FF")]
    [InlineData("ABCD", "00ABCD")]
    [InlineData("80AB", "0080AB")]
    public void ParseSerial_AlwaysYieldsAPositiveDerInteger(string input, string expected)
    {
        byte[]? serial = X509Factory.ParseSerial(input);

        Assert.NotNull(serial);
        Assert.Equal(expected, Convert.ToHexString(serial!));
        Assert.False(serial!.All(b => b == 0), "序列号不能为 0。");
        Assert.True((serial[0] & 0x80) == 0, "序列号最高位被置位时会解释为负数。");
    }

    [Fact]
    public void CertificateWithAHighBitSerialCanBeCreated()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa });
        X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
        {
            Subject = "CN=serial.example.com",
            ValidDays = 5,
            SerialNumberHex = "FF0011",
        });

        Assert.True((Convert.FromHexString(certificate.SerialNumber)[0] & 0x80) == 0, "证书序列号不应为负数。");
    }

    /// <summary>
    /// A CSR contains "BEGIN CERTIFICATE", so the naive substring test imported one file as both a CSR
    /// and a certificate; this pins the classification that replaced it.
    /// </summary>
    [Fact]
    public void ClassifyPem_DoesNotTreatACsrAsACertificate()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa });
        string csr = X509Factory.CreateRequest(key, new X509CertificateOptions { Subject = "CN=csr.example.com" })
            .CreateSigningRequestPem();

        X509Factory.PemContentKind kind = X509Factory.ClassifyPem(csr);

        Assert.True(kind.HasFlag(X509Factory.PemContentKind.Csr));
        Assert.False(kind.HasFlag(X509Factory.PemContentKind.Certificate));
    }

    [Fact]
    public void ClassifyPem_RecognisesACertificateAndACombinedKey()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa });
        X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
        {
            Subject = "CN=pem.example.com",
            ValidDays = 5,
        });

        string certificatePem = certificate.ExportCertificatePem();
        X509Factory.PemContentKind onlyCertificate = X509Factory.ClassifyPem(certificatePem);
        Assert.Equal(X509Factory.PemContentKind.Certificate, onlyCertificate);

        string keyPem = key.ExportPkcs8PrivateKeyPem();
        X509Factory.PemContentKind combined = X509Factory.ClassifyPem(keyPem + Environment.NewLine + certificatePem);
        Assert.Equal(
            X509Factory.PemContentKind.PrivateKey | X509Factory.PemContentKind.Certificate,
            combined);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a certificate at all")]
    public void ClassifyPem_IgnoresContentWithoutAnyPemHeader(string? text)
    {
        Assert.Equal(X509Factory.PemContentKind.None, X509Factory.ClassifyPem(text));
    }
}

public class CertificateStoreTests
{
    /// <summary>The assertions below are written against the Chinese table.</summary>
    public CertificateStoreTests() => Loc.SetCulture("zh-CN");

    [Fact]
    public void CanEnumerateCurrentUserStores()
    {
        var service = new CertificateStoreService();
        IReadOnlyList<string> stores = service.GetStoreNames(StoreLocation.CurrentUser);
        Assert.Contains("Root", stores);
        Assert.Contains("My", stores);
    }

    [Fact]
    public void EnumeratesRealCertificatesFromRootStore()
    {
        var service = new CertificateStoreService();
        IReadOnlyList<CertificateItem> items = service.GetCertificates(StoreLocation.CurrentUser, "Root", out string? error);

        Assert.Null(error);
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Subject));
            Assert.False(string.IsNullOrWhiteSpace(item.Thumbprint));
            Assert.True(item.NotAfter > new DateTime(1990, 1, 1));
        });
    }

    [Fact]
    public void StoreSummariesIncludeCounts()
    {
        var service = new CertificateStoreService();
        IReadOnlyList<StoreSummary> summaries = service.GetStoreSummaries(StoreLocation.CurrentUser);

        Assert.Contains(summaries, s => s.Store.Name == "Root" && s.Count > 0);
        Assert.Contains(summaries, s => s.Store.Name == "My");
    }

    [Fact]
    public void CanAddAndRemoveCertificateInPersonalStore()
    {
        var service = new CertificateStoreService();
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, new X509CertificateOptions
        {
            Subject = "CN=kasumi-selftest-" + Guid.NewGuid().ToString("N"),
            ValidDays = 1,
        });

        string thumbprint = certificate.Thumbprint;
        try
        {
            service.ImportCertificateToStore(certificate.Export(X509ContentType.Pkcs12)!, null, StoreLocation.CurrentUser, "My");
            bool added = service.GetCertificates(StoreLocation.CurrentUser, "My").Any(c => c.Thumbprint == thumbprint);
            Assert.True(added, "证书未被添加到当前用户的个人存储区。");
        }
        finally
        {
            try
            {
                service.RemoveCertificateByThumbprint(StoreLocation.CurrentUser, "My", thumbprint);
            }
            catch (CryptographicException)
            {
            }
        }

        bool stillPresent = service.GetCertificates(StoreLocation.CurrentUser, "My").Any(c => c.Thumbprint == thumbprint);
        Assert.False(stillPresent, "证书未被从当前用户的个人存储区删除。");
    }
}
