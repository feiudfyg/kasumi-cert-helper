using System.Globalization;
using System.Text;
using KasumiCertHelper.Core.Localization;
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

    // Values are resource keys where the text is translated and plain names where it is not; Get
    // returns an unknown key unchanged, so both kinds can live in the same table.
    private static readonly Dictionary<int, string> PublicKeyAlgorithms = new()
    {
        [1] = "RSA",
        [2] = "Gpg_Alg_RsaEncrypt",
        [3] = "Gpg_Alg_RsaSign",
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
                report.Title = Loc.Get(result.Success ? "Gpg_ExportDone" : "Gpg_ExportFailed");
                report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);
                break;
            case "delete":
                report.Title = Loc.Get(result.Success ? "Gpg_DeleteDone" : "Gpg_DeleteFailed");
                break;
            default:
                report.Title = Loc.Get(result.Success ? "Gpg_Done" : "Gpg_Failed");
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
                ? Loc.Get(name)
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
            report.Title = Loc.Get("Gpg_Verify_Good");
            report.Severity = GpgReportSeverity.Success;
        }
        else if (bad is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_Bad");
            report.Severity = GpgReportSeverity.Error;
        }
        else if (expired is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_Expired");
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (expiredKey is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_ExpiredKey");
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (revoked is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_Revoked");
            report.Severity = GpgReportSeverity.Error;
        }
        else if (noPubKey is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_NoPublicKey");
            report.Severity = GpgReportSeverity.Warning;
        }
        else if (errsig is not null)
        {
            report.Title = Loc.Get("Gpg_Verify_Error");
            report.Severity = GpgReportSeverity.Error;
        }
        else
        {
            report.Title = Loc.Get(result.Success ? "Gpg_Verify_Good" : "Gpg_Verify_Failed");
        }

        report.With(Loc.Get("Gpg_Row_Signer"), signerName);
        report.With(Loc.Get("Gpg_Row_KeyId"), keyId);
        report.With(Loc.Get("Gpg_Row_Fingerprint"), FormatFingerprint(validSig?.Arg(0) ?? string.Empty));

        string signatureDate = validSig?.Arg(1) ?? sigId?.Arg(1) ?? string.Empty;
        if (signatureDate.Length > 0)
        {
            report.With(Loc.Get("Gpg_Row_SignatureTime"), signatureDate);
        }

        if (validSig is not null)
        {
            report.With(Loc.Get("Gpg_Row_PublicKeyAlgorithm"), DescribeAlgorithm(validSig.Arg(6)));
            report.With(Loc.Get("Gpg_Row_HashAlgorithm"), DescribeHashAlgorithm(validSig.Arg(7)));

            string expiry = validSig.Arg(3);
            if (expiry.Length > 0 && expiry != "0")
            {
                report.With(Loc.Get("Gpg_Row_SignatureExpires"), FormatUnixTime(expiry));
            }
        }

        if (noPubKey is not null)
        {
            report.With(Loc.Get("Gpg_Row_MissingPublicKey"), ShortKeyId(noPubKey.Arg(0)), GpgReportSeverity.Warning);
        }

        if (errsig is not null)
        {
            report.With(Loc.Get("Gpg_Row_ErrorCode"), errsig.Arg(5), GpgReportSeverity.Error);
        }

        GpgStatusLine? trust = status.FirstOrDefault(s => s.Keyword.StartsWith("TRUST_", StringComparison.Ordinal));
        if (trust is not null)
        {
            report.With(Loc.Get("Gpg_Row_Trust"), DescribeTrust(trust.Keyword));
        }

        if (plaintext is not null)
        {
            string fileName = plaintext.Arg(2);
            if (fileName.Length > 0 && fileName != "_CONSOLE")
            {
                report.With(Loc.Get("Gpg_Row_SignedFile"), fileName);
            }
        }

        string length = plaintextLength?.Arg(0) ?? string.Empty;
        if (length.Length > 0)
        {
            report.With(Loc.Get("Gpg_Row_DataLength"), Loc.Format("Gpg_Value_Bytes", length));
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
            ? Loc.Get(imported + secretImported > 0 ? "Gpg_Import_Done" : "Gpg_Import_Nothing")
            : Loc.Get("Gpg_Import_Failed");

        if (result.Success && imported == 0 && secretImported == 0 && unchanged > 0)
        {
            report.Title = Loc.Get("Gpg_Import_Unchanged");
            report.Severity = GpgReportSeverity.Info;
        }

        if (summary is not null)
        {
            report.With(Loc.Get("Gpg_Row_Imported"), Loc.Format("Gpg_Value_PublicKeys", imported));
            if (secretImported > 0)
            {
                report.With(Loc.Get("Gpg_Row_ImportedSecret"), Loc.Format("Gpg_Value_Count", secretImported));
            }
            if (unchanged > 0)
            {
                report.With(Loc.Get("Gpg_Row_Unchanged"), Loc.Format("Gpg_Value_Count", unchanged));
            }
            if (notImported > 0)
            {
                report.With(Loc.Get("Gpg_Row_NotImported"), Loc.Format("Gpg_Value_Count", notImported), GpgReportSeverity.Warning);
            }
        }

        foreach (GpgStatusLine line in ok.Take(12))
        {
            string fingerprint = FormatFingerprint(line.Arg(1));
            string reason = line.Arg(0);
            string suffix = reason switch
            {
                "1" => Loc.Get("Gpg_ImportNew_Key"),
                "2" => Loc.Get("Gpg_ImportNew_Uid"),
                "3" => Loc.Get("Gpg_ImportNew_Signature"),
                "4" => Loc.Get("Gpg_ImportNew_Subkey"),
                _ => string.Empty,
            };
            report.Note(fingerprint + suffix);
        }

        if (ok.Count > 12)
        {
            report.Note(Loc.Format("Gpg_Import_More", ok.Count - 12));
        }
    }

    private static void DescribeGeneration(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status)
    {
        GpgStatusLine? created = Find(status, "KEY_CREATED");
        report.Title = Loc.Get(result.Success ? "Gpg_Gen_Done" : "Gpg_Gen_Failed");

        if (created is not null)
        {
            string fingerprint = FormatFingerprint(created.Arg(1));
            report.With(Loc.Get("Gpg_Row_NewFingerprint"), fingerprint);
            if (fingerprint.Length >= 16)
            {
                report.With(Loc.Get("Gpg_Row_KeyId"), ShortKeyId(created.Arg(1)));
            }
            report.With(Loc.Get("Gpg_Row_KeyType"), created.Arg(0) switch
            {
                "B" => Loc.Get("Gpg_KeyType_Primary"),
                "P" => Loc.Get("Gpg_KeyType_PrimaryPublic"),
                "S" => Loc.Get("Gpg_KeyType_Subkey"),
                _ => created.Arg(0),
            });
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.Note(Loc.Get("Gpg_Note_BadPassphrasePolicy"));
        }
    }

    private static void DescribeEncryption(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? outputPath,
        IReadOnlyList<string>? recipients)
    {
        report.Title = Loc.Get(result.Success ? "Gpg_Encrypt_Done" : "Gpg_Encrypt_Failed");
        report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);

        GpgStatusLine? invalid = Find(status, "INV_RECP");
        if (invalid is not null)
        {
            report.With(Loc.Get("Gpg_Row_InvalidRecipient"), invalid.Raw, GpgReportSeverity.Error);
        }

        GpgStatusLine? missing = Find(status, "NO_RECP");
        if (missing is not null)
        {
            report.With(Loc.Get("Gpg_Row_NoRecipient"), Loc.Get("Gpg_Value_NoEncryptionKey"), GpgReportSeverity.Error);
        }

        foreach (GpgStatusLine line in status.Where(s => s.Keyword == "ENC_TO"))
        {
            report.With(Loc.Get("Gpg_Row_RecipientKey"), ShortKeyId(line.Arg(0)));
        }

        if (recipients is not null && recipients.Count > 0)
        {
            report.With(Loc.Get("Gpg_Row_RecipientCount"), recipients.Count.ToString(CultureInfo.InvariantCulture));
        }

        bool signed = Find(status, "SIG_CREATED") is not null;
        report.With(Loc.Get("Gpg_Row_AlsoSigned"), Loc.Get(signed ? "Common_Yes" : "Common_No"));
    }

    private static void DescribeDecryption(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? inputPath,
        string? outputPath)
    {
        GpgStatusLine? plaintext = Find(status, "PLAINTEXT");
        report.Title = Loc.Get(result.Success ? "Gpg_Decrypt_Done" : "Gpg_Decrypt_Failed");

        report.With(Loc.Get("Gpg_Row_InputFile"), inputPath is null ? null : Path.GetFileName(inputPath));
        report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);

        if (plaintext is not null)
        {
            string name = plaintext.Arg(2);
            if (name.Length > 0 && name != "_CONSOLE")
            {
                report.With(Loc.Get("Gpg_Row_OriginalFileName"), name);
            }
        }

        GpgStatusLine? length = Find(status, "PLAINTEXT_LENGTH");
        if (length is not null)
        {
            report.With(Loc.Get("Gpg_Row_PlaintextLength"), Loc.Format("Gpg_Value_Bytes", length.Arg(0)));
        }

        if (Find(status, "DECRYPTION_FAILED") is not null)
        {
            report.With(Loc.Get("Gpg_Row_DecryptionResult"), Loc.Get("Gpg_Decrypt_Failed"), GpgReportSeverity.Error);
        }

        if (Find(status, "MISSING_PASSPHRASE") is not null)
        {
            report.Note(Loc.Get("Gpg_Note_MissingPassphrase"));
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.With(Loc.Get("Gpg_Row_Passphrase"), Loc.Get("Gpg_Value_BadPassphrase"), GpgReportSeverity.Error);
        }
    }

    private static void DescribeSignatureCreation(
        GpgOperationReport report,
        GpgResult result,
        IReadOnlyList<GpgStatusLine> status,
        string? inputPath,
        string? outputPath)
    {
        report.Title = Loc.Get(result.Success ? "Gpg_Sign_Done" : "Gpg_Sign_Failed");
        report.With(Loc.Get("Gpg_Row_InputFile"), inputPath is null ? null : Path.GetFileName(inputPath));
        report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);

        GpgStatusLine? created = Find(status, "SIG_CREATED");
        if (created is not null)
        {
            report.With(Loc.Get("Gpg_Row_SignatureType"), DescribeSignatureClass(created.Arg(0)));
            report.With(Loc.Get("Gpg_Row_PublicKeyAlgorithm"), DescribeAlgorithm(created.Arg(1)));
            report.With(Loc.Get("Gpg_Row_HashAlgorithm"), DescribeHashAlgorithm(created.Arg(2)));
        }

        if (Find(status, "BAD_PASSPHRASE") is not null)
        {
            report.With(Loc.Get("Gpg_Row_Passphrase"), Loc.Get("Gpg_Value_BadPassphrase"), GpgReportSeverity.Error);
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
            0x00 => Loc.Get("Gpg_SigClass_Binary"),
            0x01 => Loc.Get("Gpg_SigClass_CanonicalText"),
            0x10 => Loc.Get("Gpg_SigClass_Generic"),
            0x11 => Loc.Get("Gpg_SigClass_Persona"),
            0x12 => Loc.Get("Gpg_SigClass_Casual"),
            0x13 => Loc.Get("Gpg_SigClass_Positive"),
            _ => "0x" + code.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    private static string DescribeTrust(string keyword) => Loc.Get(keyword switch
    {
        "TRUST_UNDEFINED" => "Gpg_Trust_Undefined",
        "TRUST_NEVER" => "Gpg_Trust_Never",
        "TRUST_MARGINAL" => "Gpg_Trust_Marginal",
        "TRUST_FULLY" => "Gpg_Trust_Fully",
        "TRUST_ULTIMATE" => "Gpg_Trust_Ultimate",
        _ => keyword,
    });

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
