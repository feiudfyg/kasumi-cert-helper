using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

/// <summary>What a GnuPG home directory contains, used to explain the import to the user.</summary>
public sealed record GnupgKeyringInfo(
    string? Home,
    bool Exists,
    string? Executable,
    string? Version,
    bool HasKeyboxDatabase,
    bool HasLegacyKeyring,
    IReadOnlyList<string> ExportFiles)
{
    /// <summary>True when the keys can actually be read, directly or through gpg.exe.</summary>
    public bool CanImport => Exists && (Executable is not null || ExportFiles.Count > 0);
}

/// <summary>The result of importing a GnuPG keyring into the application's own key store.</summary>
public sealed record GnupgImportOutcome(
    string? Home,
    string? Executable,
    bool UsedBridge,
    OpenPgpImportResult Result,
    bool SecretKeysRequested,
    bool SecretKeysImported,
    string? Error)
{
    public bool Success => Error is null;

    public bool FoundSomething => Result.ImportedCount > 0 || Result.UpdatedCount > 0;
}

/// <summary>
/// Compatibility with a system GnuPG keyring: finds the GnuPG home directory and copies its keys into
/// the application's own OpenPGP key store. Public keys always work; secret keys are copied when
/// GnuPG can export them (unprotected keys, or a passphrase supplied by the user).
/// </summary>
public static class GnupgKeyring
{
    public static string? LocateHome()
    {
        string? configured = Environment.GetEnvironmentVariable("GNUPGHOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(roaming))
        {
            string candidate = Path.Combine(roaming, "gnupg");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            string candidate = Path.Combine(local, "gnupg");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            string candidate = Path.Combine(profile, ".gnupg");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static GnupgKeyringInfo Inspect(string? home = null, string? executable = null)
    {
        home ??= LocateHome();
        executable ??= GnupgBridge.FindExecutable();

        bool exists = !string.IsNullOrWhiteSpace(home) && Directory.Exists(home);
        bool keybox = exists && File.Exists(Path.Combine(home!, "public-keys.d", "pubring.db"));
        bool legacy = exists &&
            (File.Exists(Path.Combine(home!, "pubring.gpg")) || File.Exists(Path.Combine(home!, "pubring.kbx")));

        return new GnupgKeyringInfo(
            home,
            exists,
            executable,
            exists ? GnupgBridge.Version(executable) : null,
            keybox,
            legacy,
            exists ? ExportFileCandidates(home!) : Array.Empty<string>());
    }

    /// <summary>
    /// Copies every key of the GnuPG keyring at <paramref name="home"/> into <paramref name="store"/>.
    /// Never throws: problems come back in <see cref="GnupgImportOutcome.Error"/>.
    /// </summary>
    public static GnupgImportOutcome Import(
        OpenPgpKeyStore store,
        string? home = null,
        bool includeSecretKeys = false,
        string? passphrase = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        home ??= LocateHome();
        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
        {
            return new GnupgImportOutcome(
                home, null, false, EmptyResult, includeSecretKeys, false, Loc.Get("Gpg_GnupgHomeMissing"));
        }

        note ??= Loc.Get("Gpg_NoteFromGnupg");
        string? executable = GnupgBridge.FindExecutable();

        if (executable is not null)
        {
            return ImportThroughGpg(store, home, executable, includeSecretKeys, passphrase, note);
        }

        return ImportExportedFiles(store, home, note);
    }

    private static GnupgImportOutcome ImportThroughGpg(
        OpenPgpKeyStore store,
        string home,
        string executable,
        bool includeSecretKeys,
        string? passphrase,
        string note)
    {
        string? publicKeys = GnupgBridge.Export(executable, home, secretKeys: false);
        if (publicKeys is null)
        {
            return new GnupgImportOutcome(
                home, executable, true, EmptyResult, includeSecretKeys, false, Loc.Get("Gpg_GnupgExportFailed"));
        }

        OpenPgpImportResult result = store.Import(publicKeys, note);
        bool secretImported = false;
        string? error = null;

        if (includeSecretKeys)
        {
            string? secretKeys = GnupgBridge.Export(executable, home, secretKeys: true, passphrase);
            if (secretKeys is null)
            {
                error = Loc.Get("Gpg_GnupgSecretFailed");
            }
            else
            {
                OpenPgpImportResult secretResult = store.Import(secretKeys, note);
                result = Merge(result, secretResult);
                secretImported = secretResult.ImportedCount > 0 || secretResult.UpdatedCount > 0;
            }
        }

        return new GnupgImportOutcome(home, executable, true, result, includeSecretKeys, secretImported, error);
    }

    private static GnupgImportOutcome ImportExportedFiles(OpenPgpKeyStore store, string home, string note)
    {
        IReadOnlyList<string> files = ExportFileCandidates(home);
        if (files.Count == 0)
        {
            return new GnupgImportOutcome(
                home, null, false, EmptyResult, false, false, Loc.Get("Gpg_GnupgNeedsExecutable"));
        }

        var imported = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        foreach (string file in files)
        {
            try
            {
                OpenPgpImportResult result = store.Import(File.ReadAllText(file), note);
                imported.AddRange(result.Imported);
                updated.AddRange(result.Updated);
                skipped.AddRange(result.Skipped);
            }
            catch (Exception)
            {
                skipped.Add(Path.GetFileName(file));
            }
        }

        var combined = new OpenPgpImportResult(imported, updated, skipped);
        return new GnupgImportOutcome(
            home,
            null,
            false,
            combined,
            false,
            false,
            imported.Count == 0 && updated.Count == 0 ? Loc.Get("Gpg_GnupgNeedsExecutable") : null);
    }

    private static OpenPgpImportResult EmptyResult { get; } = new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Armored exports a user may have left in the GnuPG directory by hand.</summary>
    private static IReadOnlyList<string> ExportFileCandidates(string home)
    {
        try
        {
            return Directory
                .EnumerateFiles(home, "*.asc", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(home, "*.pgp", SearchOption.TopDirectoryOnly))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static OpenPgpImportResult Merge(OpenPgpImportResult first, OpenPgpImportResult second)
        => new(
            first.Imported.Concat(second.Imported).Distinct(StringComparer.Ordinal).ToList(),
            first.Updated.Concat(second.Updated).Distinct(StringComparer.Ordinal).ToList(),
            first.Skipped.Concat(second.Skipped).Distinct(StringComparer.Ordinal).ToList());
}
