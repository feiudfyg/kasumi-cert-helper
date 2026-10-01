using System.Globalization;
using System.Text;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

/// <summary>
/// One <c>[GNUPG:] KEYWORD ...</c> record emitted through <c>--status-fd</c>.
/// Status lines are part of GnuPG's machine interface, so they are never localized.
/// </summary>
public sealed record GpgStatusLine(string Keyword, string Raw, IReadOnlyList<string> Arguments)
{
    public string Arg(int index) => index >= 0 && index < Arguments.Count ? Arguments[index] : string.Empty;
}

public static class GpgOutputInterpreter
{
    private const string StatusPrefix = "[GNUPG:] ";

    private static readonly Dictionary<int, string> PublicKeyAlgorithms = new()
    {
        [1] = "RSA",
        [2] = "RSA (encrypt only)",
        [3] = "RSA (sign only)",
        [16] = "ElGamal",
        [17] = "DSA",
        [18] = "ECDH",
        [19] = "ECDSA",
        [20] = "ElGamal",
        [22] = "EdDSA",
    };

    private static readonly Dictionary<int, string> HashAlgorithms = new()
    {
        [1] = "MD5",
        [2] = "SHA-1",
        [3] = "RIPEMD-160",
        [8] = "SHA-256",
        [9] = "SHA-384",
        [10] = "SHA-512",
        [11] = "SHA-224",
    };

    public static IReadOnlyList<GpgStatusLine> ParseStatusLines(string? text)
    {
        var lines = new List<GpgStatusLine>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r', ' ');
            if (!line.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string payload = line[StatusPrefix.Length..];
            int space = payload.IndexOf(' ');
            string keyword = space < 0 ? payload : payload[..space];
            string rest = space < 0 ? string.Empty : payload[(space + 1)..];
            string[] arguments = rest.Length == 0
                ? Array.Empty<string>()
                : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            lines.Add(new GpgStatusLine(keyword, rest, arguments));
        }

        return lines;
    }

    /// <summary>Human readable gpg messages (everything that is not a status line).</summary>
    public static IReadOnlyList<string> ParseMessages(string? text)
    {
        var messages = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return messages;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            messages.Add(line);
        }

        return messages;
    }

    public static GpgOperationReport Describe(
        GpgResult result,
        string operation,
        string? inputPath = null,
        string? outputPath = null,
        IReadOnlyList<string>? recipients = null)
    {
        IReadOnlyList<GpgStatusLine> status = ParseStatusLines(result.StandardError);
        var report = new GpgOperationReport(operation)
        {
            ExitCode = result.ExitCode,
            Success = result.Success,
            RawOutput = BuildRawOutput(result),
            Severity = result.Success ? GpgReportSeverity.Success : GpgReportSeverity.Error,
        };

        switch (operation)
        {
            case "verify":
                DescribeVerification(report, result, status);
                break;
            case "import":
                DescribeImport(report, result, status);
                break;
            case "generate":
                DescribeGeneration(report, result, status);
                break;
            case "encrypt":
                DescribeEncryption(report, result, status, outputPath, recipients);
                break;
            case "decrypt":
                DescribeDecryption(report, result, status, inputPath, outputPath);
                break;
            case "sign":
                DescribeSignatureCreation(report, result, status, inputPath, outputPath);
                break;
            case "export":
                report.Title = result.Success ? "导出完成" : "导出失败";
                report.With("输出文件", outputPath);
                break;
            case "delete":
                report.Title = result.Success ? "密钥已删除" : "删除失败";
                break;
            default:
                report.Title = result.Success ? "操作完成" : "操作失败";
                break;
        }

        AddNotes(report, result, status);
        return report;
    }

    public static string FormatFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return string.Empty;
        }

        string cleaned = fingerprint.Replace(" ", string.Empty).Trim();
        if (cleaned.Length <= 16)
        {
            return cleaned.ToUpperInvariant();
        }

        var sb = new StringBuilder(cleaned.Length + (cleaned.Length / 4));
        for (int i = 0; i < cleaned.Length; i += 4)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(cleaned.AsSpan(i, Math.Min(4, cleaned.Length - i)));
        }
        return sb.ToString().ToUpperInvariant();
    }

    public static string ShortKeyId(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return string.Empty;
        }
        string cleaned = fingerprint.Replace(" ", string.Empty);
        return cleaned.Length <= 16 ? cleaned.ToUpperInvariant() : cleaned[^16..].ToUpperInvariant();
    }

    public static string DescribeAlgorithm(string algorithm)
        => int.TryParse(algorithm, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            && PublicKeyAlgorithms.TryGetValue(id, out string? name)
                ? name
                : algorithm;

    public static string DescribeHashAlgorithm(string algorithm)
        => int.TryParse(algorithm, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            && HashAlgorithms.TryGetValue(id, out string? name)
                ? name
                : algorithm;

    private static void DescribeVerification(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status)
    {
        GpgStatusLine? good = Find(status, "GOODSIG");
        GpgStatusLine? bad = Find(status, "BADSIG");
        GpgStatusLine? expired = Find(status, "EXPSIG");
        GpgStatusLine? expiredKey = Find(status, "EXPKEYSIG");
        GpgStatusLine? revoked = Find(status, "REVKEYSIG");
        GpgStatusLine? errsig = Find(status, "ERRSIG");
        GpgStatusLine? noPubKey = Find(status, "NO_PUBKEY");
        GpgStatusLine? validSig = Find(status, "VALIDSIG");
        GpgStatusLine? sigId = Find(status, "SIG_ID");
        GpgStatusLine? plaintext = Find(status, "PLAINTEXT");
        GpgStatusLine? plaintextLength = Find(status, "PLAINTEXT_LENGTH");

        GpgStatusLine? signer = good ?? bad ?? expired ?? expiredKey ?? revoked;

        string signerRaw = signer?.Raw ?? string.Empty;
        int split = signerRaw.IndexOf(' ');
        string keyId = split < 0 ? signerRaw : signerRaw[..split];
        string signerName = split < 0 ? string.Empty : signerRaw[(split + 1)..].Trim();

        if (good is not null)
        {
            report.Title = "签名有效";
            report.Severity = GpgReportSeverity.Success;
        }
        else if (bad is not null)
        {
            report.Title = "签名无效（数据可能已被篡改）";
            report.Severity = GpgReportSeverity.Error;
        }
        else if (expired is not null)
        {
            report.Title = "签名已过期";
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (expiredKey is not null)
        {
            report.Title = "签名者密钥已过期";
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (revoked is not null)
        {
            report.Title = "签名者密钥已被吊销";
            report.Severity = GpgReportSeverity.Error;
        }
        else if (noPubKey is not null)
        {
            report.Title = "缺少公钥，无法验证";
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (errsig is not null)
        {
            report.Title = "无法验证签名";
            report.Severity = GpgReportSeverity.Error;
        }
        else
        {
            report.Title = result.Success ? "签名有效" : "验证失败";
        }

        report.With("签名者", signerName);
        report.With("密钥 ID", keyId);
        report.With("指纹", FormatFingerprint(validSig?.Arg(0) ?? string.Empty));

        string signatureDate = validSig?.Arg(1) ?? sigId?.Arg(1) ?? string.Empty;
        if (signatureDate.Length > 0)
        {
            report.With("签名时间", signatureDate);
        }

        if (validSig is not null)
        {
            report.With("公钥算法", DescribeAlgorithm(validSig.Arg(6)));
            report.With("摘要算法", DescribeHashAlgorithm(validSig.Arg(7)));

            string expiry = validSig.Arg(3);
            if (expiry.Length > 0 && expiry != "0")
            {
                report.With("签名过期于", FormatUnixTime(expiry));
            }
        }

        if (noPubKey is not null)
        {
            report.With("缺少的公钥", ShortKeyId(noPubKey.Arg(0)), GpgReportSeverity.Warning);
        }

        if (errsig is not null)
        {
            report.With("错误代码", errsig.Arg(5), GpgReportSeverity.Error);
        }

        GpgStatusLine? trust = status.FirstOrDefault(s => s.Keyword.StartsWith("TRUST_", StringComparison.Ordinal));
        if (trust is not null)
        {
            report.With("信任状态", DescribeTrust(trust.Keyword));
        }

        if (plaintext is not null)
        {
            string fileName = plaintext.Arg(2);
            if (fileName.Length > 0 && fileName != "_CONSOLE")
            {
                report.With("被签名的文件", fileName);
            }
        }

        string length = plaintextLength?.Arg(0) ?? string.Empty;
        if (length.Length > 0)
        {
            report.With("数据长度", length + " 字节");
        }
    }

    private static void DescribeImport(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status)
    {
        IReadOnlyList<GpgStatusLine> ok = status.Where(s => s.Keyword == "IMPORT_OK").ToList();
        GpgStatusLine? summary = Find(status, "IMPORT_RES");

        int imported = 0;
        int unchanged = 0;
        int secretImported = 0;
        int notImported = 0;

        if (summary is not null)
        {
            // IMPORT_RES <count> <no_user_id> <imported> <imported_rsa> <unchanged> <n_uids>
            //            <n_subk> <n_sigs> <n_revoc> <sec_read> <sec_imported> <sec_dups>
            //            [<skipped_new_keys>] <not_imported>
            imported = ToInt(summary.Arg(2));
            unchanged = ToInt(summary.Arg(4));
            secretImported = ToInt(summary.Arg(10));
            notImported = ToInt(summary.Arg(summary.Arguments.Count - 1));
        }

        report.Title = result.Success
            ? imported + secretImported > 0 ? "密钥已导入" : "没有新的密钥"
            : "导入失败";

        if (result.Success && imported == 0 && secretImported == 0 && unchanged > 0)
        {
            report.Title = "密钥已存在，无需导入";
            report.Severity = GpgReportSeverity.Info;
        }

        if (summary is not null)
        {
            report.With("新导入", imported + " 个公钥");
            if (secretImported > 0)
            {
                report.With("新导入私钥", secretImported + " 个");
            }
            if (unchanged > 0)
            {
                report.With("已存在（未变更）", unchanged + " 个");
            }
            if (notImported > 0)
            {
                report.With("未能导入", notImported + " 个", GpgReportSeverity.Warning);
            }
        }

        foreach (GpgStatusLine line in ok.Take(12))
        {
            string fingerprint = FormatFingerprint(line.Arg(1));
            string reason = line.Arg(0);
            string suffix = reason switch
            {
                "0" => string.Empty,
                "1" => "（新密钥）",
                "2" => "（新用户 ID）",
                "3" => "（新签名）",
                "4" => "（新子密钥）",
                _ => string.Empty,
            };
            report.Note(fingerprint + suffix);
        }

        if (ok.Count > 12)
        {
            report.Note($"… 以及另外 {ok.Count - 12} 个密钥");
        }
    }

    private static void DescribeGeneration(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status)
    {
        GpgStatusLine? created = Find(status, "KEY_CREATED");
        report.Title = result.Success ? "密钥对已生成" : "生成密钥失败";

        if (created is not null)
        {
            string fingerprint = FormatFingerprint(created.Arg(1));
            report.With("新指纹", fingerprint);
            if (fingerprint.Length >= 16)
            {
                report.With("密钥 ID", ShortKeyId(created.Arg(1)));
            }
            report.With("密钥类型", created.Arg(0) switch
            {
                "B" => "主密钥",
                "P" => "主密钥（公开部分）",
                "S" => "子密钥",
                _ => created.Arg(0),
            });
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.Note("密码不符合 gpg-agent 的策略要求。");
        }
    }

    private static void DescribeEncryption(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? outputPath,
        IReadOnlyList<string>? recipients)
    {
        report.Title = result.Success ? "文件已加密" : "加密失败";
        report.With("输出文件", outputPath);

        GpgStatusLine? invalid = Find(status, "INV_RECP");
        if (invalid is not null)
        {
            report.With("无效接收者", invalid.Raw, GpgReportSeverity.Error);
        }

        GpgStatusLine? missing = Find(status, "NO_RECP");
        if (missing is not null)
        {
            report.With("缺少接收者", "没有可用于加密的密钥", GpgReportSeverity.Error);
        }

        foreach (GpgStatusLine line in status.Where(s => s.Keyword == "ENC_TO"))
        {
            report.With("接收者密钥", ShortKeyId(line.Arg(0)));
        }

        if (recipients is not null && recipients.Count > 0)
        {
            report.With("接收者数量", recipients.Count.ToString(CultureInfo.InvariantCulture));
        }

        bool signed = Find(status, "SIG_CREATED") is not null;
        report.With("同时签名", signed ? "是" : "否");
    }

    private static void DescribeDecryption(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? inputPath,
        string? outputPath)
    {
        GpgStatusLine? plaintext = Find(status, "PLAINTEXT");
        report.Title = result.Success ? "文件已解密" : "解密失败";

        report.With("输入文件", inputPath is null ? null : Path.GetFileName(inputPath));
        report.With("输出文件", outputPath);

        if (plaintext is not null)
        {
            string name = plaintext.Arg(2);
            if (name.Length > 0 && name != "_CONSOLE")
            {
                report.With("原始文件名", name);
            }
        }

        GpgStatusLine? length = Find(status, "PLAINTEXT_LENGTH");
        if (length is not null)
        {
            report.With("明文长度", length.Arg(0) + " 字节");
        }

        if (Find(status, "DECRYPTION_FAILED") is not null)
        {
            report.With("解密结果", "解密失败", GpgReportSeverity.Error);
        }

        if (Find(status, "MISSING_PASSPHRASE") is not null)
        {
            report.Note("该私钥需要密码，但未提供密码。");
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.With("密码", "密码错误", GpgReportSeverity.Error);
        }
    }

    private static void DescribeSignatureCreation(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? inputPath,
        string? outputPath)
    {
        report.Title = result.Success ? "文件已签名" : "签名失败";
        report.With("输入文件", inputPath is null ? null : Path.GetFileName(inputPath));
        report.With("输出文件", outputPath);

        GpgStatusLine? created = Find(status, "SIG_CREATED");
        if (created is not null)
        {
            report.With("签名类型", DescribeSignatureClass(created.Arg(0)));
            report.With("公钥算法", DescribeAlgorithm(created.Arg(1)));
            report.With("摘要算法", DescribeHashAlgorithm(created.Arg(2)));
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.With("密码", "密码错误", GpgReportSeverity.Error);
        }
    }

    private static string DescribeSignatureClass(string value)
    {
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
        {
            return value;
        }

        return code switch
        {
            0x00 => "二进制文档",
            0x01 => "规范文本",
            0x10 => "通用认证",
            0x11 => "个人认证",
            0x12 => "随性认证",
            0x13 => "肯定认证",
            _ => "0x" + code.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    private static string DescribeTrust(string keyword) => keyword switch
    {
        "TRUST_UNDEFINED" => "未定义",
        "TRUST_NEVER" => "不可信",
        "TRUST_MARGINAL" => "勉强可信",
        "TRUST_FULLY" => "完全可信",
        "TRUST_ULTIMATE" => "绝对可信（自有密钥）",
        _ => keyword,
    };

    private static void AddNotes(GpgOperationReport report, GpgResult result, IReadOnlyList<GpgStatusLine> status)
    {
        if (!result.Success && report.Notes.Count == 0 && report.Rows.Count == 0)
        {
            IReadOnlyList<string> messages = ParseMessages(result.StandardError);
            foreach (string message in messages
                         .Where(m => m.StartsWith("gpg: ", StringComparison.OrdinalIgnoreCase))
                         .Select(m => m[5..].Trim())
                         .Where(m => m.Length > 0)
                         .Distinct(StringComparer.Ordinal)
                         .TakeLast(8))
            {
                report.Note(message);
            }
        }

        if (status.Any(s => s.Keyword == "FAILURE"))
        {
            report.Severity = GpgReportSeverity.Error;
        }
    }

    private static string BuildRawOutput(GpgResult result)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            sb.AppendLine("--- stdout ---");
            sb.AppendLine(result.StandardOutput.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            sb.AppendLine("--- stderr ---");
            sb.AppendLine(result.StandardError.TrimEnd());
        }
        return sb.ToString().TrimEnd();
    }

    private static GpgStatusLine? Find(IReadOnlyList<GpgStatusLine> status, string keyword)
        => status.FirstOrDefault(s => string.Equals(s.Keyword, keyword, StringComparison.Ordinal));

    private static int ToInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static string FormatUnixTime(string seconds)
    {
        if (!long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value <= 0)
        {
            return string.Empty;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }
}
