using System.Diagnostics;
using System.Text;

namespace KasumiCertHelper.Core.Services;

/// <summary>
/// Uses an installed GnuPG only as an importer/exporter of its own keyring. GnuPG 2.3 and later keep
/// public keys in a keybox database (<c>public-keys.d\pubring.db</c>) that has no public format, so the
/// only reliable way to read such a keyring is to let GnuPG itself export it. Nothing is bundled: when
/// GnuPG is absent the application still manages its own OpenPGP key store on its own.
/// </summary>
public static class GnupgBridge
{
    /// <summary>Environment variable that overrides the location of gpg.exe.</summary>
    public const string ExecutableEnvironmentVariable = "KASUMI_GPG";

    private static readonly string[] WellKnownLocations =
    {
        @"D:\GnuPG\bin\gpg.exe",
        @"C:\GnuPG\bin\gpg.exe",
    };

    private static readonly string[] WellKnownDirectories =
    {
        @"GnuPG\bin",
        @"GnuPG\bin\gpg4win",
        @"Gpg4win\..\GnuPG\bin",
    };

    /// <summary>
    /// Finds gpg.exe: an explicit path first, then the <see cref="ExecutableEnvironmentVariable"/>
    /// override, then the usual install directories and finally %PATH%.
    /// </summary>
    public static string? FindExecutable(string? explicitPath = null)
    {
        foreach (string? candidate in new[]
                 {
                     explicitPath,
                     Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable),
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        foreach (string fixedPath in WellKnownLocations)
        {
            if (File.Exists(fixedPath))
            {
                return fixedPath;
            }
        }

        foreach (Environment.SpecialFolder folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            foreach (string directory in WellKnownDirectories)
            {
                string candidate = Path.GetFullPath(Path.Combine(root, directory, "gpg.exe"));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        foreach (string entry in (path ?? string.Empty).Split(Path.PathSeparator))
        {
            string directory = entry.Trim();
            if (directory.Length == 0)
            {
                continue;
            }

            try
            {
                string candidate = Path.Combine(directory, "gpg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    /// <summary>The first line of <c>gpg --version</c>, or null when it could not be read.</summary>
    public static string? Version(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        try
        {
            using Process? process = Start(executable, new[] { "--version" }, redirectInput: false, out _);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                TryKill(process);
                return null;
            }

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Contains("GnuPG", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Fingerprints of the primary keys in the keyring. Used to export keys one at a time, so a single
    /// key in a format this application cannot read does not hide all the others.
    /// </summary>
    public static IReadOnlyList<string> ListPrimaryFingerprints(string executable, string? home)
    {
        var arguments = new List<string> { "--batch", "--no-tty" };
        if (!string.IsNullOrWhiteSpace(home))
        {
            arguments.Add("--homedir");
            arguments.Add(home);
        }

        arguments.Add("--with-colons");
        arguments.Add("--list-keys");

        try
        {
            using Process? process = Start(executable, arguments, redirectInput: false, out _);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                TryKill(process);
                return Array.Empty<string>();
            }

            return ParsePrimaryFingerprints(output);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// In colon listings a <c>pub</c> record is followed by the primary key's <c>fpr</c> record;
    /// <c>fpr</c> records after a <c>sub</c> record belong to subkeys and are ignored.
    /// </summary>
    public static IReadOnlyList<string> ParsePrimaryFingerprints(string colonListing)
    {
        var fingerprints = new List<string>();
        bool expectPrimary = false;

        foreach (string line in colonListing.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split(':');
            if (fields.Length == 0)
            {
                continue;
            }

            switch (fields[0])
            {
                case "pub":
                    expectPrimary = true;
                    break;
                case "sub":
                case "sec":
                    expectPrimary = false;
                    break;
                case "fpr" when expectPrimary && fields.Length > 9 && fields[9].Length > 0:
                    fingerprints.Add(fields[9]);
                    expectPrimary = false;
                    break;
            }
        }

        return fingerprints;
    }

    /// <summary>
    /// Exports the whole keyring as ASCII armored OpenPGP. <paramref name="passphrase"/> is only used
    /// for secret keys and is written to gpg's standard input, never to the command line.
    /// </summary>
    public static string? Export(
        string executable,
        string? home,
        bool secretKeys,
        string? passphrase = null,
        string? keySelector = null,
        TimeSpan? timeout = null)
    {
        var arguments = new List<string> { "--batch", "--no-tty" };
        if (!string.IsNullOrWhiteSpace(home))
        {
            arguments.Add("--homedir");
            arguments.Add(home);
        }

        bool feedPassphrase = secretKeys && passphrase is not null;
        if (feedPassphrase)
        {
            arguments.Add("--pinentry-mode");
            arguments.Add("loopback");
            arguments.Add("--passphrase-fd");
            arguments.Add("0");
        }

        arguments.Add("--armor");
        arguments.Add(secretKeys ? "--export-secret-keys" : "--export");
        if (!string.IsNullOrWhiteSpace(keySelector))
        {
            arguments.Add(keySelector);
        }

        try
        {
            using Process? process = Start(executable, arguments, feedPassphrase, out StreamWriter? input);
            if (process is null)
            {
                return null;
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            if (feedPassphrase && input is not null)
            {
                input.WriteLine(passphrase);
                input.Dispose();
            }

            if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds))
            {
                TryKill(process);
                return null;
            }

            string output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0 && output.Contains("-----BEGIN PGP", StringComparison.Ordinal)
                ? output
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Process? Start(string executable, IReadOnlyList<string> arguments, bool redirectInput, out StreamWriter? input)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process = Process.Start(info);
        input = redirectInput ? process?.StandardInput : null;
        return process;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }
}
