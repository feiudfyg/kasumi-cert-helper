using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace KasumiCertHelper.Core.Services;

public static class CertificateDetailsBuilder
{
    private static readonly Dictionary<string, string> EkuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.3.6.1.5.5.7.3.1"] = "服务器身份验证 (TLS Web Server Authentication)",
        ["1.3.6.1.5.5.7.3.2"] = "客户端身份验证 (TLS Web Client Authentication)",
        ["1.3.6.1.5.5.7.3.3"] = "代码签名 (Code Signing)",
        ["1.3.6.1.5.5.7.3.4"] = "电子邮件保护 (E-mail Protection)",
        ["1.3.6.1.5.5.7.3.5"] = "IPSec 终端系统",
        ["1.3.6.1.5.5.7.3.6"] = "IPSec 隧道终端",
        ["1.3.6.1.5.5.7.3.7"] = "IPSec 用户",
        ["1.3.6.1.5.5.7.3.8"] = "时间戳 (Time Stamping)",
        ["1.3.6.1.5.5.7.3.9"] = "OCSP 签名 (OCSP Signing)",
        ["1.3.6.1.5.5.7.3.10"] = "DVCS",
        ["2.5.29.37.0"] = "任意扩展密钥用法",
        ["1.3.6.1.4.1.311.10.3.1"] = "Microsoft 信任列表签名",
        ["1.3.6.1.4.1.311.10.3.4"] = "加密文件系统 (EFS)",
        ["1.3.6.1.4.1.311.10.3.4.1"] = "EFS 恢复",
        ["1.3.6.1.4.1.311.10.3.12"] = "文档签名",
        ["1.3.6.1.4.1.311.20.2.2"] = "智能卡登录",
        ["1.3.6.1.4.1.311.21.6"] = "密钥恢复",
        ["1.3.6.1.5.2.3.4"] = "PKINIT 客户端身份验证",
        ["1.3.6.1.5.2.3.5"] = "PKINIT KDC",
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
        Add(X509KeyUsageFlags.DigitalSignature, "数字签名");
        Add(X509KeyUsageFlags.NonRepudiation, "不可否认性");
        Add(X509KeyUsageFlags.KeyEncipherment, "密钥加密");
        Add(X509KeyUsageFlags.DataEncipherment, "数据加密");
        Add(X509KeyUsageFlags.KeyAgreement, "密钥协商");
        Add(X509KeyUsageFlags.KeyCertSign, "证书签名");
        Add(X509KeyUsageFlags.CrlSign, "CRL 签名");
        Add(X509KeyUsageFlags.EncipherOnly, "仅加密");
        Add(X509KeyUsageFlags.DecipherOnly, "仅解密");
        return string.Join(", ", names);
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
            string name = EkuNames.TryGetValue(value, out string? friendly) ? friendly : (oid?.FriendlyName ?? value);
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
                    $"CA = {(bc.CertificateAuthority ? "是" : "否")}；路径长度限制 = {(bc.HasPathLengthConstraint ? bc.PathLengthConstraint.ToString() : "无")}",
                X509KeyUsageExtension ku => GetKeyUsageTextFromFlags(ku.KeyUsages),
                X509EnhancedKeyUsageExtension eku => string.Join("；", eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o?.FriendlyName ?? o?.Value ?? string.Empty)),
                X509SubjectKeyIdentifierExtension ski => ski.SubjectKeyIdentifier,
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

        Add(X509KeyUsageFlags.DigitalSignature, "数字签名");
        Add(X509KeyUsageFlags.NonRepudiation, "不可否认性");
        Add(X509KeyUsageFlags.KeyEncipherment, "密钥加密");
        Add(X509KeyUsageFlags.DataEncipherment, "数据加密");
        Add(X509KeyUsageFlags.KeyAgreement, "密钥协商");
        Add(X509KeyUsageFlags.KeyCertSign, "证书签名");
        Add(X509KeyUsageFlags.CrlSign, "CRL 签名");
        Add(X509KeyUsageFlags.EncipherOnly, "仅加密");
        Add(X509KeyUsageFlags.DecipherOnly, "仅解密");
        return names.Count == 0 ? "无" : string.Join("、", names);
    }

    public static string BuildTextReport(X509Certificate2 certificate)
    {
        var sb = new StringBuilder();
        void Line(string label, string? value)
            => sb.Append("  ").Append(label.PadRight(18)).Append(": ").AppendLine(value ?? string.Empty);

        sb.AppendLine("=== 基本信息 ===");
        Line("版本", "V" + certificate.Version);
        Line("序列号", certificate.SerialNumber);
        Line("签名算法", certificate.SignatureAlgorithm?.FriendlyName ?? certificate.SignatureAlgorithm?.Value);
        Line("指纹 (SHA1)", certificate.Thumbprint);
        try { Line("指纹 (SHA256)", Convert.ToHexString(SHA256.HashData(certificate.RawData))); } catch { }
        Line("主题", X500Name.Format(certificate.Subject));
        Line("颁发者", X500Name.Format(certificate.Issuer));
        try { Line("友好名称", certificate.FriendlyName); } catch { }
        sb.AppendLine();
        sb.AppendLine("=== 有效期 ===");
        Line("生效时间", certificate.NotBefore.ToString("yyyy-MM-dd HH:mm:ss"));
        Line("过期时间", certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss"));
        Line("状态", GetValidityText(certificate));
        sb.AppendLine();
        sb.AppendLine("=== 公钥 ===");
        Line("算法", certificate.PublicKey?.Oid?.FriendlyName ?? certificate.PublicKey?.Oid?.Value);
        int keySize = GetKeySize(certificate);
        if (keySize > 0)
        {
            Line("密钥长度", keySize + " bit");
        }
        try { Line("公钥 (Base64)", Convert.ToBase64String(certificate.PublicKey?.EncodedKeyValue.RawData ?? Array.Empty<byte>())); } catch { }
        Line("私钥", certificate.HasPrivateKey ? "存在" : "不存在");
        sb.AppendLine();
        sb.AppendLine("=== 扩展 ===");
        Line("CA 证书", IsCa(certificate) ? "是" : "否");
        X509BasicConstraintsExtension? bc = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        Line("路径长度约束", bc is not null && bc.HasPathLengthConstraint ? bc.PathLengthConstraint.ToString() : "无");
        Line("密钥用法", GetKeyUsageText(certificate));
        IReadOnlyList<string> ekus = GetEnhancedKeyUsages(certificate);
        Line("增强密钥用法", ekus.Count == 0 ? "无" : string.Join("; ", ekus));
        IReadOnlyList<string> sans = GetSubjectAlternativeNames(certificate);
        Line("使用者可选名称", sans.Count == 0 ? "无" : string.Join("; ", sans));
        Line("SKI", GetExtensionText(certificate, "2.5.29.14"));
        Line("AKI", GetExtensionText(certificate, "2.5.29.35"));
        Line("CRL 分发点", GetExtensionText(certificate, "2.5.29.31"));
        Line("颁发机构信息访问", GetExtensionText(certificate, "1.3.6.1.5.5.7.1.1"));
        Line("证书策略", GetExtensionText(certificate, "2.5.29.32"));

        sb.AppendLine();
        sb.AppendLine("=== 所有扩展 ===");
        foreach (X509Extension ext in certificate.Extensions)
        {
            sb.Append("  ").Append(ext.Oid?.Value)
              .Append(ext.Critical ? " [关键]" : string.Empty)
              .Append("  =>  ")
              .AppendLine(DescribeExtension(ext));
        }

        return sb.ToString();
    }

    public static string GetValidityText(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        if (now < certificate.NotBefore) return "尚未生效";
        if (now > certificate.NotAfter) return "已过期";
        TimeSpan remaining = certificate.NotAfter - now;
        return $"有效 (剩余 {remaining.Days} 天)";
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
            return "无";
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
