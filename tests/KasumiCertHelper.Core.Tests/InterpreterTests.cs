using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

public class GpgOutputInterpreterTests
{
    /// <summary>The assertions below are written against the Chinese table.</summary>
    public GpgOutputInterpreterTests() => Loc.SetCulture("zh-CN");

    private const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string KeyId = "1234567890ABCDEF";
    private const string ShortId = "89ABCDEF01234567";

    [Fact]
    public void ParseStatusLines_IgnoresHumanReadableText()
    {
        const string stderr =
            "gpg: Signature made 2026-10-01 12:00:00\r\n" +
            "[GNUPG:] GOODSIG 1234567890ABCDEF Kasumi User <a@b.c>\r\n" +
            "gpg: Good signature from \"Kasumi User\"\r\n" +
            "[GNUPG:] VALIDSIG abc 2026-10-01 1759300000 0 4 0 22 10 00\r\n";

        IReadOnlyList<GpgStatusLine> status = GpgOutputInterpreter.ParseStatusLines(stderr);

        Assert.Equal(2, status.Count);
        Assert.Equal("GOODSIG", status[0].Keyword);
        Assert.Equal("1234567890ABCDEF", status[0].Arg(0));
        Assert.Equal("Kasumi User <a@b.c>", status[0].Raw["1234567890ABCDEF ".Length..]);
        Assert.Equal("VALIDSIG", status[1].Keyword);
    }

    [Fact]
    public void Describe_GoodSignature_ReportsSignerAndAlgorithms()
    {
        string stderr = string.Join('\n',
            "[GNUPG:] NEWSIG",
            "[GNUPG:] SIG_ID " + KeyId + " 2026-10-01 1759300000",
            "[GNUPG:] GOODSIG " + KeyId + " Kasumi User <a@b.c>",
            "[GNUPG:] VALIDSIG " + Fingerprint + " 2026-10-01 1759300000 0 4 0 22 10 00",
            "[GNUPG:] TRUST_FULLY 0 pgp",
            "[GNUPG:] PLAINTEXT 62 1759300000 document.txt",
            "[GNUPG:] PLAINTEXT_LENGTH 12345");

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr), "verify");

        Assert.Equal("签名有效", report.Title);
        Assert.Equal(GpgReportSeverity.Success, report.Severity);
        Assert.Equal("Kasumi User <a@b.c>", Value(report, "签名者"));
        Assert.Equal(KeyId, Value(report, "密钥 ID"));
        Assert.Equal("0123 4567 89AB CDEF 0123 4567 89AB CDEF 0123 4567", Value(report, "指纹"));
        Assert.Equal("EdDSA", Value(report, "公钥算法"));
        Assert.Equal("SHA-512", Value(report, "摘要算法"));
        Assert.Equal("完全可信", Value(report, "信任状态"));
        Assert.Equal("document.txt", Value(report, "被签名的文件"));
        Assert.Equal("12345 字节", Value(report, "数据长度"));
    }

    [Fact]
    public void Describe_BadSignature_IsAnError()
    {
        string stderr = "[GNUPG:] BADSIG " + KeyId + " Kasumi User <a@b.c>\n[GNUPG:] VALIDSIG " + Fingerprint + " 2026-10-01 0 0 4 0 1 2 00";

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(1, string.Empty, stderr), "verify");

        Assert.Contains("签名无效", report.Title);
        Assert.Equal(GpgReportSeverity.Error, report.Severity);
    }

    [Fact]
    public void Describe_MissingPublicKey_ExplainsWhatIsMissing()
    {
        string stderr = "[GNUPG:] ERRSIG " + KeyId + " 1 2 00 1759300000 9\n[GNUPG:] NO_PUBKEY " + KeyId;

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(1, string.Empty, stderr), "verify");

        Assert.Equal("缺少公钥，无法验证", report.Title);
        Assert.Equal(GpgReportSeverity.Warning, report.Severity);
        Assert.Equal(KeyId, Value(report, "缺少的公钥"));
    }

    [Fact]
    public void Describe_Import_CountsImportedUnchangedAndRejectedKeys()
    {
        string stderr = string.Join('\n',
            "[GNUPG:] IMPORT_OK 1 " + Fingerprint,
            "[GNUPG:] IMPORT_RES 3 0 2 0 1 2 0 0 0 0 0 0 1");

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr), "import");

        Assert.Equal("密钥已导入", report.Title);
        Assert.Equal("2 个公钥", Value(report, "新导入"));
        Assert.Equal("1 个", Value(report, "已存在（未变更）"));
        Assert.Equal("1 个", Value(report, "未能导入"));
    }

    [Fact]
    public void Describe_Import_AllUnchanged_DoesNotClaimNewKeys()
    {
        string stderr = "[GNUPG:] IMPORT_RES 1 0 0 0 1 1 0 0 0 0 0 0 0";

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr), "import");

        Assert.Equal("密钥已存在，无需导入", report.Title);
        Assert.Equal(GpgReportSeverity.Info, report.Severity);
    }

    [Fact]
    public void Describe_Generate_FindsTheNewFingerprint()
    {
        string stderr = "[GNUPG:] KEY_CREATED B " + Fingerprint;

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr), "generate");

        Assert.Equal("密钥对已生成", report.Title);
        Assert.Equal("0123 4567 89AB CDEF 0123 4567 89AB CDEF 0123 4567", Value(report, "新指纹"));
        Assert.Equal(ShortId, Value(report, "密钥 ID"));
        Assert.Equal("主密钥", Value(report, "密钥类型"));
    }

    [Fact]
    public void Describe_Encrypt_ListsRecipientsAndOutput()
    {
        string stderr = string.Join('\n',
            "[GNUPG:] BEGIN_ENCRYPTION 3 10",
            "[GNUPG:] ENC_TO " + KeyId + " 1 2048",
            "[GNUPG:] END_ENCRYPTION");

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr),
            "encrypt",
            outputPath: @"C:\temp\secret.asc",
            recipients: new[] { Fingerprint });

        Assert.Equal("文件已加密", report.Title);
        Assert.Equal(@"C:\temp\secret.asc", Value(report, "输出文件"));
        Assert.Equal(KeyId, Value(report, "接收者密钥"));
        Assert.Equal("1", Value(report, "接收者数量"));
    }

    [Fact]
    public void Describe_Decrypt_ReportsPlaintextMetadata()
    {
        string stderr = string.Join('\n',
            "[GNUPG:] DECRYPTION_OKAY",
            "[GNUPG:] PLAINTEXT 62 1759300000 report.txt",
            "[GNUPG:] PLAINTEXT_LENGTH 42");

        GpgOperationReport report = GpgOutputInterpreter.Describe(
            new GpgResult(0, string.Empty, stderr),
            "decrypt",
            inputPath: @"C:\temp\secret.asc",
            outputPath: @"C:\temp\report.txt");

        Assert.Equal("文件已解密", report.Title);
        Assert.Equal("report.txt", Value(report, "原始文件名"));
        Assert.Equal("42 字节", Value(report, "明文长度"));
    }

    [Fact]
    public void Describe_FailureWithoutStatusLines_KeepsGpgMessages()
    {
        var result = new GpgResult(2, string.Empty, "gpg: no valid OpenPGP data found.\ngpg: processing message failed: Unknown system error\n");

        GpgOperationReport report = GpgOutputInterpreter.Describe(result, "import");

        Assert.Equal("导入失败", report.Title);
        Assert.Equal(GpgReportSeverity.Error, report.Severity);
        Assert.Contains(report.Notes, n => n.Contains("no valid OpenPGP data", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Notes, n => n.StartsWith("gpg: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Describe_AlwaysKeepsRawOutputForPowerUsers()
    {
        var result = new GpgResult(0, "stdout text", "[GNUPG:] GOODSIG 1 A\n");

        GpgOperationReport report = GpgOutputInterpreter.Describe(result, "verify");

        Assert.True(report.HasRawOutput);
        Assert.Contains("stdout text", report.RawOutput, StringComparison.Ordinal);
        Assert.Contains("--- stderr ---", report.RawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void AlgorithmAndFingerprintHelpers_AreStable()
    {
        Assert.Equal("RSA", GpgOutputInterpreter.DescribeAlgorithm("1"));
        Assert.Equal("EdDSA", GpgOutputInterpreter.DescribeAlgorithm("22"));
        Assert.Equal("SHA-256", GpgOutputInterpreter.DescribeHashAlgorithm("8"));
        Assert.Equal("ABCD1234", GpgOutputInterpreter.FormatFingerprint("abcd1234"));
        Assert.Equal(ShortId, GpgOutputInterpreter.ShortKeyId(Fingerprint));
    }

    private static string Value(GpgOperationReport report, string label)
        => report.Rows.FirstOrDefault(r => r.Label == label)?.Value ?? string.Empty;
}

public class CertificateSummaryBuilderTests
{
    /// <summary>The assertions below are written against the Chinese table.</summary>
    public CertificateSummaryBuilderTests() => Loc.SetCulture("zh-CN");

    [Fact]
    public void Build_ParsesEverythingTheDetailPaneShows()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Rsa, KeySize = 2048 });
        var options = new X509CertificateOptions
        {
            Subject = new X509SubjectOptions { CommonName = "summary.example.com", Organization = "Kasumi" }.BuildDistinguishedName(),
            ValidDays = 30,
            HashAlgorithm = "SHA256",
            IsCa = true,
            HasPathLengthConstraint = true,
            PathLengthConstraint = 1,
            SubjectAlternativeNames =
            {
                new SanEntry { Kind = SanKind.Dns, Value = "summary.example.com" },
                new SanEntry { Kind = SanKind.Dns, Value = "alt.example.com" },
            },
            CrlDistributionPoints = { "http://crl.example.com/root.crl" },
            OcspUrls = { "http://ocsp.example.com/" },
        };

        using System.Security.Cryptography.X509Certificates.X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);
        CertificateSummary summary = CertificateSummaryBuilder.Build(certificate);

        Assert.Equal("summary.example.com", summary.SubjectCommonName);
        Assert.Equal(summary.Subject, summary.Issuer);
        Assert.True(summary.IsCertificateAuthority);
        Assert.Equal(1, summary.PathLengthConstraint);
        Assert.Equal(2048, summary.KeySize);
        Assert.Contains("SHA256", summary.SignatureAlgorithm, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DNS: summary.example.com", summary.SubjectAlternativeNames);
        Assert.Contains("DNS: alt.example.com", summary.SubjectAlternativeNames);
        Assert.Equal(2, summary.SubjectAlternativeNames.Count);
        Assert.True(summary.RemainingDays is > 25 and <= 30);
        Assert.StartsWith("有效", summary.StatusText, StringComparison.Ordinal);
        Assert.Contains("http://crl.example.com/root.crl", summary.CrlDistributionPoints);
        Assert.Contains(summary.AuthorityInformationAccess, uri => uri.Contains("ocsp.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(':', summary.ThumbprintSha1);
        Assert.Equal(95, summary.ThumbprintSha256.Length);
        Assert.False(string.IsNullOrWhiteSpace(summary.SubjectKeyIdentifier));
        Assert.False(string.IsNullOrWhiteSpace(summary.AuthorityKeyIdentifier));
        Assert.Contains("基本信息", summary.RawTextReport);
    }

    [Fact]
    public void Build_ExpiredCertificateIsReportedAsExpired()
    {
        using var key = X509Factory.CreateKey(new X509KeyOptions { Algorithm = X509KeyAlgorithm.Ecdsa, Curve = "nistP256" });
        var options = new X509CertificateOptions
        {
            Subject = new X509SubjectOptions { CommonName = "expired.example.com" }.BuildDistinguishedName(),
            ValidDays = 1,
            HashAlgorithm = "SHA256",
            NotBefore = DateTimeOffset.Now.AddDays(-10),
            NotAfter = DateTimeOffset.Now.AddDays(-1),
        };

        using System.Security.Cryptography.X509Certificates.X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);

        CertificateSummary summary = CertificateSummaryBuilder.Build(certificate);

        Assert.Equal("已过期", summary.StatusText);
        Assert.True(summary.RemainingDays < 0);
    }
}