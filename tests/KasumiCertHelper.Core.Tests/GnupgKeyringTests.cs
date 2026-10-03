using System.Diagnostics;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// GnuPG keyring compatibility. The integration test needs an installed gpg.exe and is skipped when
/// there is none, but the detection and file based parts always run.
/// </summary>
public sealed class GnupgKeyringTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "kasumi-gnupg-tests", Guid.NewGuid().ToString("N")[..8]);

    private readonly string? _originalHome = Environment.GetEnvironmentVariable("GNUPGHOME");

    public GnupgKeyringTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GNUPGHOME", _originalHome);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    [Fact]
    public void LocateHomeUsesTheConfiguredEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("GNUPGHOME", _directory);
        Assert.Equal(_directory, GnupgKeyring.LocateHome());
        Assert.True(GnupgKeyring.Inspect().Exists);
    }

    [Fact]
    public void FindExecutableHonoursAnExplicitPath()
    {
        string? self = Environment.ProcessPath;
        Assert.False(string.IsNullOrEmpty(self));
        Assert.Equal(Path.GetFullPath(self!), GnupgBridge.FindExecutable(self));
    }

    [Fact]
    public void InspectionReadsExportedArmoredFilesWithoutAnExecutable()
    {
        File.WriteAllText(Path.Combine(_directory, "someone.asc"), "-----BEGIN PGP PUBLIC KEY BLOCK-----\n\n-----END PGP PUBLIC KEY BLOCK-----\n");
        Environment.SetEnvironmentVariable("GNUPGHOME", _directory);

        GnupgKeyringInfo info = GnupgKeyring.Inspect(executable: string.Empty);

        Assert.True(info.Exists);
        Assert.False(info.HasKeyboxDatabase);
        Assert.Single(info.ExportFiles);
        Assert.True(info.CanImport);
    }

    [Fact]
    public void ImportReportsAProblemWhenThereIsNoKeyring()
    {
        Environment.SetEnvironmentVariable("GNUPGHOME", Path.Combine(_directory, "absent"));
        using var store = new TempStore(_directory);

        GnupgImportOutcome outcome = GnupgKeyring.Import(store.Store);

        Assert.False(outcome.Success);
        Assert.False(outcome.FoundSomething);
        Assert.NotNull(outcome.Error);
    }

    /// <summary>
    /// Generates a throwaway key in a private GNUPGHOME and imports it through the installed gpg.exe.
    /// Skipped when GnuPG is not installed.
    /// </summary>
    [Fact]
    public void ImportsKeysFromARealGnuPgKeyring()
    {
        string? executable = GpgProbe.FindUsable();
        if (executable is null)
        {
            return;
        }

        Environment.SetEnvironmentVariable("GNUPGHOME", _directory);
        (bool generated, string _) = GenerateKey(executable, _directory, "Kasumi Keyring Test <kasumi-keyring@example.com>");
        if (!generated)
        {
            return;
        }

        using var store = new TempStore(Path.Combine(_directory, "store"));
        GnupgImportOutcome outcome = GnupgKeyring.Import(
            store.Store,
            _directory,
            includeSecretKeys: true,
            passphrase: string.Empty);

        Assert.True(outcome.Success, outcome.Error);
        Assert.True(outcome.FoundSomething, "no key was imported from the generated keyring");
        Assert.True(outcome.UsedBridge);

        var keys = store.Store.List();
        OpenPgpStoredKey imported = Assert.Single(keys);
        Assert.Contains("Kasumi Keyring Test", imported.UserId, StringComparison.Ordinal);
        Assert.True(imported.HasSecretKey, "the unprotected secret key should have been imported as well");
    }

    /// <summary>
    /// GnuPG wraps every exported key in a single ASCII armor block, so the importer has to walk the
    /// whole stream instead of stopping after the first key ring.
    /// </summary>
    [Fact]
    public void ImportsEveryKeyOfAMultiKeyGnuPgExport()
    {
        string? executable = GpgProbe.FindUsable();
        if (executable is null)
        {
            return;
        }

        Environment.SetEnvironmentVariable("GNUPGHOME", _directory);
        (bool first, string _) = GenerateKey(executable, _directory, "Kasumi First <kasumi-first@example.com>");
        if (!first)
        {
            return;
        }

        (bool second, string _) = GenerateKey(executable, _directory, "Kasumi Second <kasumi-second@example.com>");
        if (!second)
        {
            return;
        }

        string? exported = GnupgBridge.Export(executable, _directory, secretKeys: false);
        Assert.False(string.IsNullOrWhiteSpace(exported), "gpg exported nothing");

        using var store = new TempStore(Path.Combine(_directory, "multi-store"));
        OpenPgpImportResult result = store.Store.Import(exported!);

        Assert.Equal(2, result.ImportedCount);

        // And the whole flow reports the same, with nothing counted as unreadable.
        using var secondStore = new TempStore(Path.Combine(_directory, "multi-store-2"));
        GnupgImportOutcome outcome = GnupgKeyring.Import(secondStore.Store, _directory);
        Assert.Equal(2, outcome.Result.ImportedCount);
        Assert.Equal(0, outcome.Failed);
        Assert.Contains(secondStore.Store.List(), key => key.UserId.Contains("Kasumi First", StringComparison.Ordinal));
        Assert.Contains(secondStore.Store.List(), key => key.UserId.Contains("Kasumi Second", StringComparison.Ordinal));
    }

    private static (bool Success, string Error) GenerateKey(string executable, string home, string userId)

    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "--homedir", home,
                     "--batch",
                     "--pinentry-mode", "loopback",
                     "--passphrase", string.Empty,
                     "--quick-generate-key", userId, "default", "default", "never",
                 })
        {
            info.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(info);
        if (process is null)
        {
            return (false, "could not start gpg");
        }

        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(90000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }

            return (false, "gpg timed out");
        }

        return (process.ExitCode == 0, $"exit {process.ExitCode}: {error}");
    }

    /// <summary>A key store in a temporary directory that cleans itself up.</summary>
    private sealed class TempStore : IDisposable
    {
        public TempStore(string root)
        {
            Directory.CreateDirectory(root);
            Store = new OpenPgpKeyStore(root);
        }

        public OpenPgpKeyStore Store { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Store.Directory, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
