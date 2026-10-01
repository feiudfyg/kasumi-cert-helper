using System.Diagnostics;
using System.Text;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public sealed class GpgService
{
    private const int DefaultTimeoutMs = 180_000;

    public GpgService(string? executablePath = null, string? homeDirectory = null)
    {
        ExecutablePath = executablePath ?? AutoDetect() ?? string.Empty;
        HomeDirectory = homeDirectory;
    }

    public string ExecutablePath { get; set; }

    public string? HomeDirectory { get; set; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    public bool IsBundled
    {
        get
        {
            if (!IsAvailable)
            {
                return false;
            }

            try
            {
                string? bundled = DetectBundled();
                return bundled is not null
                       && string.Equals(Path.GetFullPath(bundled), Path.GetFullPath(ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// GnuPG shipped next to the application (see the <c>tools\gnupg</c> folder). Keeping a private
    /// copy means the app works on machines without gpg4win installed.
    /// </summary>
    public static string? DetectBundled()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] relativePaths =
        {
            Path.Combine("gpg", "bin", "gpg.exe"),
            Path.Combine("gpg", "gpg.exe"),
            Path.Combine("gnupg", "bin", "gpg.exe"),
        };

        foreach (string relative in relativePaths)
        {
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(baseDirectory, relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    public static string? AutoDetect()
    {
        string? bundled = DetectBundled();
        if (bundled is not null)
        {
            return bundled;
        }

        var candidates = new List<string>();
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(programFiles, "GnuPG", "bin", "gpg.exe"));
        candidates.Add(Path.Combine(programFilesX86, "GnuPG", "bin", "gpg.exe"));
        candidates.Add(Path.Combine(programFiles, "GnuPG", "bin", "gpg2.exe"));
        candidates.Add(Path.Combine(programFilesX86, "GnuPG", "bin", "gpg2.exe"));
        candidates.Add(@"D:\GnuPG\bin\gpg.exe");
        candidates.Add(@"C:\GnuPG\bin\gpg.exe");
        candidates.Add(Path.Combine(programFiles, "Gpg4win", "bin", "gpg.exe"));
        candidates.Add(Path.Combine(programFilesX86, "Gpg4win", "bin", "gpg.exe"));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                foreach (string name in new[] { "gpg.exe", "gpg2.exe" })
                {
                    try
                    {
                        string full = Path.Combine(directory.Trim(), name);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
        }

        return null;
    }

    public GpgResult Run(IReadOnlyList<string> arguments, string? standardInput = null, int timeoutMs = DefaultTimeoutMs)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("未找到 gpg.exe，请在设置中指定 GnuPG 可执行文件路径。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(HomeDirectory))
        {
            startInfo.ArgumentList.Add("--homedir");
            startInfo.ArgumentList.Add(HomeDirectory);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
            }
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch (Exception) { }
            throw new TimeoutException("GPG 命令执行超时。");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        var commandLine = new StringBuilder("\"" + ExecutablePath + "\"");
        foreach (string argument in arguments)
        {
            commandLine.Append(' ').Append(argument.Contains(' ') ? "\"" + argument + "\"" : argument);
        }

        return new GpgResult(process.ExitCode, stdout, stderr, commandLine.ToString());
    }

    public string GetVersion()
    {
        GpgResult result = Run(new[] { "--version" });
        string firstLine = result.StandardOutput.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        return firstLine;
    }

    public IReadOnlyList<GpgKey> ListKeys(bool secretOnly)
    {
        var arguments = new List<string>
        {
            "--batch", "--with-colons", "--fixed-list-mode",
            secretOnly ? "--list-secret-keys" : "--list-keys",
        };
        GpgResult result = Run(arguments);
        return GpgColonParser.Parse(result.StandardOutput);
    }

    public IReadOnlyList<GpgKey> ListAllKeys()
    {
        List<GpgKey> publicKeys = ListKeys(false).ToList();
        IReadOnlyList<GpgKey> secretKeys = ListKeys(true);

        var publicByFingerprint = publicKeys
            .GroupBy(k => k.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (GpgKey secret in secretKeys)
        {
            if (publicByFingerprint.TryGetValue(secret.Fingerprint, out GpgKey? existing))
            {
                existing.HasSecret = true;
                foreach (GpgSubkey subkey in existing.Subkeys)
                {
                    subkey.HasSecret = true;
                }
            }
            else
            {
                secret.HasSecret = true;
                publicKeys.Add(secret);
            }
        }

        return publicKeys
            .OrderByDescending(k => k.Created ?? DateTime.MinValue)
            .ToList();
    }

    public GpgResult GenerateKey(GpgKeyGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RealName))
        {
            throw new ArgumentException("必须提供姓名。", nameof(options));
        }

        string parameters = BuildKeyParameters(options);
        string parameterFile = Path.Combine(Path.GetTempPath(), "kasumi-gpg-" + Guid.NewGuid().ToString("N") + ".parameters");
        File.WriteAllText(parameterFile, parameters, new UTF8Encoding(false));

        try
        {
            var arguments = new List<string> { "--batch", "--yes", "--status-fd", "2", "--pinentry-mode", "loopback", "--gen-key", parameterFile };
            return Run(arguments);
        }
        finally
        {
            try { File.Delete(parameterFile); } catch (Exception) { }
        }
    }

    private static string BuildKeyParameters(GpgKeyGenerationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("%echo 正在生成 OpenPGP 密钥...");

        switch (options.Algorithm)
        {
            case GpgKeyAlgorithm.Rsa:
                sb.AppendLine("Key-Type: RSA");
                sb.AppendLine("Key-Length: " + options.KeyLength);
                sb.AppendLine("Key-Usage: sign cert");
                if (options.IncludeSubkey)
                {
                    sb.AppendLine("Subkey-Type: RSA");
                    sb.AppendLine("Subkey-Length: " + options.SubkeyLength);
                    sb.AppendLine("Subkey-Usage: encrypt");
                }
                break;
            case GpgKeyAlgorithm.Ecc:
                sb.AppendLine("Key-Type: ECDSA");
                sb.AppendLine("Key-Curve: " + options.Curve);
                if (options.IncludeSubkey)
                {
                    sb.AppendLine("Subkey-Type: ECDH");
                    sb.AppendLine("Subkey-Curve: " + options.Curve);
                }
                break;
            case GpgKeyAlgorithm.Ed25519:
                sb.AppendLine("Key-Type: EDDSA");
                sb.AppendLine("Key-Curve: ed25519");
                if (options.IncludeSubkey)
                {
                    sb.AppendLine("Subkey-Type: ECDH");
                    sb.AppendLine("Subkey-Curve: cv25519");
                }
                break;
        }

        sb.AppendLine("Name-Real: " + options.RealName);
        if (!string.IsNullOrWhiteSpace(options.Email))
        {
            sb.AppendLine("Name-Email: " + options.Email);
        }
        if (!string.IsNullOrWhiteSpace(options.Comment))
        {
            sb.AppendLine("Name-Comment: " + options.Comment);
        }

        sb.AppendLine("Expire-Date: " + (string.IsNullOrWhiteSpace(options.ExpireDate) ? "0" : options.ExpireDate));

        if (string.IsNullOrEmpty(options.Passphrase))
        {
            sb.AppendLine("%no-protection");
        }
        else
        {
            sb.AppendLine("Passphrase: " + options.Passphrase);
        }

        sb.AppendLine("%commit");
        sb.AppendLine("%echo 完成");
        return sb.ToString();
    }

    public GpgResult ImportKey(string filePath)
        => Run(new[] { "--batch", "--yes", "--status-fd", "2", "--import", filePath });

    public GpgResult ImportKeysFromText(string armored, bool isSecret)
    {
        string parameterFile = Path.Combine(Path.GetTempPath(), "kasumi-gpg-" + Guid.NewGuid().ToString("N") + ".asc");
        File.WriteAllText(parameterFile, armored, new UTF8Encoding(false));
        try
        {
            return ImportKey(parameterFile);
        }
        finally
        {
            try { File.Delete(parameterFile); } catch (Exception) { }
        }
    }

    public void ExportKeyToFile(string fingerprint, string outputPath, bool secret, bool armor, string? passphrase = null)
    {
        var arguments = new List<string> { "--batch", "--yes", "--output", outputPath };
        if (armor)
        {
            arguments.Add("--armor");
        }
        AddPassphrase(arguments, passphrase);
        arguments.Add(secret ? "--export-secret-keys" : "--export");
        arguments.Add(fingerprint);

        GpgResult result = Run(arguments, standardInput: passphrase is null ? null : passphrase + "\n");
        if (!result.Success && !File.Exists(outputPath))
        {
            throw new InvalidOperationException("导出失败: " + result.StandardError.Trim());
        }
    }

    public string ExportKeyToText(string fingerprint, bool secret, bool armor, string? passphrase = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), "kasumi-gpg-" + Guid.NewGuid().ToString("N") + ".key");
        try
        {
            ExportKeyToFile(fingerprint, temp, secret, armor, passphrase);
            return File.Exists(temp) ? File.ReadAllText(temp) : string.Empty;
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }
    }

    public GpgResult DeleteKey(string fingerprint, bool secret)
    {
        if (secret)
        {
            Run(new[] { "--batch", "--yes", "--status-fd", "2", "--delete-secret-keys", fingerprint });
        }
        return Run(new[] { "--batch", "--yes", "--status-fd", "2", "--delete-key", fingerprint });
    }

    public GpgResult EncryptFile(
        string inputPath,
        string outputPath,
        IEnumerable<string> recipients,
        bool armor = false,
        bool sign = false,
        string? signerKeyId = null,
        string? passphrase = null)
    {
        var arguments = new List<string> { "--batch", "--yes", "--status-fd", "2", "--trust-model", "always", "--output", outputPath };
        if (armor)
        {
            arguments.Add("--armor");
        }

        arguments.Add("--encrypt");

        if (sign)
        {
            arguments.Add("--sign");
            if (!string.IsNullOrWhiteSpace(signerKeyId))
            {
                arguments.Add("--local-user");
                arguments.Add(signerKeyId);
            }
            AddPassphrase(arguments, passphrase);
        }
        else if (passphrase is not null)
        {
            AddPassphrase(arguments, passphrase);
        }

        foreach (string recipient in recipients)
        {
            if (!string.IsNullOrWhiteSpace(recipient))
            {
                arguments.Add("--recipient");
                arguments.Add(recipient);
            }
        }

        arguments.Add(inputPath);
        return Run(arguments, standardInput: passphrase is null ? null : passphrase + "\n");
    }

    public GpgResult DecryptFile(string inputPath, string outputPath, string? passphrase = null)
    {
        var arguments = new List<string> { "--batch", "--yes", "--status-fd", "2", "--output", outputPath };
        AddPassphrase(arguments, passphrase);
        arguments.Add("--decrypt");
        arguments.Add(inputPath);
        return Run(arguments, standardInput: passphrase is null ? null : passphrase + "\n");
    }

    public GpgResult SignFile(
        string inputPath,
        string outputPath,
        bool detached = true,
        bool armor = false,
        string? keyId = null,
        string? passphrase = null,
        bool clearSign = false)
    {
        var arguments = new List<string> { "--batch", "--yes", "--status-fd", "2", "--output", outputPath };

        if (clearSign)
        {
            arguments.Add("--clearsign");
        }
        else if (detached)
        {
            arguments.Add("--detach-sign");
        }
        else
        {
            arguments.Add("--sign");
        }

        if (armor || clearSign)
        {
            arguments.Add("--armor");
        }

        if (!string.IsNullOrWhiteSpace(keyId))
        {
            arguments.Add("--local-user");
            arguments.Add(keyId);
        }

        AddPassphrase(arguments, passphrase);
        arguments.Add(inputPath);
        return Run(arguments, standardInput: passphrase is null ? null : passphrase + "\n");
    }

    public GpgResult VerifyFile(string signaturePath, string? dataPath = null)
    {
        var arguments = new List<string> { "--batch", "--status-fd", "2", "--verify", signaturePath };
        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            arguments.Add(dataPath);
        }
        return Run(arguments);
    }

    public GpgResult RunRaw(IEnumerable<string> arguments, string? standardInput = null)
        => Run(arguments.ToList(), standardInput);

    private static void AddPassphrase(ICollection<string> arguments, string? passphrase)
    {
        if (passphrase is null)
        {
            return;
        }
        arguments.Add("--pinentry-mode");
        arguments.Add("loopback");
        arguments.Add("--passphrase-fd");
        arguments.Add("0");
    }
}
