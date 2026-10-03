using System.Diagnostics;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// "A gpg.exe exists" is not the same as "gpg works here". GitHub's Windows runners put an MSYS gpg
/// from Git for Windows on PATH that cannot handle a Windows <c>--homedir</c> path (it mangles
/// <c>C:\Users\...</c> into a POSIX path and fails to create its keyring), so the optional GnuPG
/// integration tests have to probe the executable before they run and skip when it is unusable.
/// </summary>
internal static class GpgProbe
{
    /// <summary>The first detected GnuPG that can create and list a keyring, or <c>null</c>.</summary>
    public static string? FindUsable()
    {
        string? executable = GnupgBridge.FindExecutable();
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            return null;
        }

        string home = Path.Combine(Path.GetTempPath(), "kasumi-gpg-probe-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(home);

            var info = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[] { "--homedir", home, "--batch", "--list-keys" })
            {
                info.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }

                return null;
            }

            return process.ExitCode == 0 ? executable : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
