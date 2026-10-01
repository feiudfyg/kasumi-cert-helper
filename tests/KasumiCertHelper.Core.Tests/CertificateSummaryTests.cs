using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

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
