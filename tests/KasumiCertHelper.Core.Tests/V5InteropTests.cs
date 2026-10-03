using System.Diagnostics;
using System.Text;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using Xunit;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// End-to-end interoperability tests for GnuPG draft-v5 (crypto-refresh) keys.
/// They drive the real gpg binary when one is available and are skipped
/// otherwise.
/// </summary>
public sealed class V5InteropTests : IDisposable
{
    private readonly string _gpg;
    private readonly bool _available;
    private readonly string _home;

    public V5InteropTests()
    {
        _gpg = GpgProbe.FindUsable() ?? string.Empty;
        _available = !string.IsNullOrEmpty(_gpg);

        _home = Path.Combine(Path.GetTempPath(), "kasumi-v5-interop-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        if (_available)
        {
            Directory.CreateDirectory(_home);

            string fx = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            string secretPath = Path.Combine(_home, "import-secret.asc");
            File.WriteAllText(secretPath, File.ReadAllText(Path.Combine(fx, "v5-ed448-secret.asc")));
            (int exit, _, _) = RunGpg("--import", secretPath);
            File.Delete(secretPath);

            // If even the import fails, this gpg cannot be used for interoperability: skip instead
            // of failing the suite on an environment problem.
            _available = exit == 0;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home))
            {
                Directory.Delete(_home, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void GpgDecryptsWhatWeEncrypt()
    {
        if (!_available)
        {
            return;
        }

        string fx = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string pub = File.ReadAllText(Path.Combine(fx, "v5-ed448-public.asc"));
        byte[] payload = Encoding.UTF8.GetBytes("hello v5 from BouncyCastle\nsecond line\n");

        byte[] encrypted = OpenPgp.Encrypt(payload, new[] { pub }, armor: true, null, null, "v5-interop.txt");

        string file = Path.Combine(_home, "encrypted.asc");
        File.WriteAllBytes(file, encrypted);

        (int exit, string stdout, string stderr) = RunGpg("--decrypt", file);

        Assert.True(exit == 0, stderr + "\n" + stdout);
        Assert.Contains("hello v5 from BouncyCastle", stdout);
    }

    [Fact]
    public void GpgVerifiesWhatWeSign()
    {
        if (!_available)
        {
            return;
        }

        string fx = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string sec = File.ReadAllText(Path.Combine(fx, "v5-ed448-secret.asc"));
        byte[] payload = Encoding.UTF8.GetBytes("signed by v5\n");

        byte[] signature = OpenPgp.Sign(payload, sec, "kasumi-test", detached: true, armor: true);

        string pub = File.ReadAllText(Path.Combine(fx, "v5-ed448-public.asc"));
        OpenPgpVerification self = OpenPgp.Verify(payload, signature, pub);
        Assert.True(self.IsValid, "BC self-verification failed: " + self.Summary);

        string dataFile = Path.Combine(_home, "signed.txt");
        string sigFile = dataFile + ".asc";
        File.WriteAllBytes(dataFile, payload);
        File.WriteAllBytes(sigFile, signature);

        (int exit, string stdout, string stderr) = RunGpg("--verify", sigFile, dataFile);

        Assert.True(exit == 0, stderr + "\n" + stdout);
    }

    private (int Exit, string Stdout, string Stderr) RunGpg(params string[] arguments)
    {
        var psi = new ProcessStartInfo(_gpg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--homedir");
        psi.ArgumentList.Add(_home);
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--pinentry-mode");
        psi.ArgumentList.Add("loopback");
        psi.ArgumentList.Add("--passphrase");
        psi.ArgumentList.Add("kasumi-test");
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
