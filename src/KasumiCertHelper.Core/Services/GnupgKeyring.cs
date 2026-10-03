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
    int SecretKeysImported,
    int SecretKeysFailed,
    int Failed,
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
                home, null, false, EmptyResult, includeSecretKeys, 0, 0, 0, Loc.Get("Gpg_GnupgHomeMissing"));
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
        IReadOnlyList<string> fingerprints = GnupgBridge.ListPrimaryFingerprints(executable, home);

        ImportPass publicPass = ImportOnePass(store, executable, home, secretKeys: false, passphrase: null, fingerprints, note);
        if (publicPass.Imported.Count == 0 && publicPass.Updated.Count == 0 && publicPass.Failed == 0)
        {
            // The keyring could not be read at all.
            return new GnupgImportOutcome(
                home, executable, true, EmptyResult, includeSecretKeys, 0, 0, 0, Loc.Get("Gpg_GnupgExportFailed"));
        }

        ImportPass secretPass = includeSecretKeys
            ? ImportOnePass(store, executable, home, secretKeys: true, passphrase, fingerprints, note)
            : ImportPass.Empty;

        OpenPgpImportResult result = Merge(publicPass.ToResult(), secretPass.ToResult());

        return new GnupgImportOutcome(
            home,
            executable,
            true,
            result,
            includeSecretKeys,
            secretPass.Imported.Count + secretPass.Updated.Count,
            secretPass.Failed,
            publicPass.Failed,
            null);
    }

    /// <summary>
    /// Exports and imports one kind of key. A whole keyring is tried first because that is a single gpg
    /// call; if nothing at all could be read, every key is exported on its own so that a key in an
    /// unsupported format (GnuPG 2.4 writes v5 keys, which BouncyCastle cannot parse) only skips itself.
    /// </summary>
    private static ImportPass ImportOnePass(
        OpenPgpKeyStore store,
        string executable,
        string home,
        bool secretKeys,
        string? passphrase,
        IReadOnlyList<string> fingerprints,
        string note)
    {
        string? everything = GnupgBridge.Export(executable, home, secretKeys, passphrase);
        if (everything is not null)
        {
            ImportPass bulk = ImportPass.From(store.Import(everything, note));
            if (bulk.Imported.Count > 0 || bulk.Updated.Count > 0)
            {
                return bulk;
            }
        }

        if (fingerprints.Count == 0)
        {
            return ImportPass.Empty;
        }

        var pass = new ImportPass();
        foreach (string fingerprint in fingerprints)
        {
            string? armor = GnupgBridge.Export(executable, home, secretKeys, passphrase, fingerprint);
            if (armor is null)
            {
                pass.Failed++;
                continue;
            }

            ImportPass one = ImportPass.From(store.Import(armor, note));
            if (one.Imported.Count == 0 && one.Updated.Count == 0)
            {
                // gpg handed out a key, but it is in a format that cannot be read.
                pass.Failed++;
                continue;
            }

            pass.Add(one);
        }

        return pass;
    }

    private static GnupgImportOutcome ImportExportedFiles(OpenPgpKeyStore store, string home, string note)
    {
        IReadOnlyList<string> files = ExportFileCandidates(home);
        if (files.Count == 0)
        {
            return new GnupgImportOutcome(
                home, null, false, EmptyResult, false, 0, 0, 0, Loc.Get("Gpg_GnupgNeedsExecutable"));
        }

        var pass = new ImportPass();
        foreach (string file in files)
        {
            try
            {
                pass.Add(ImportPass.From(store.Import(File.ReadAllText(file), note)));
            }
            catch (Exception)
            {
                pass.Failed++;
            }
        }

        return new GnupgImportOutcome(
            home,
            null,
            false,
            pass.ToResult(),
            false,
            0,
            0,
            pass.Failed,
            pass.Imported.Count == 0 && pass.Updated.Count == 0 ? Loc.Get("Gpg_GnupgNeedsExecutable") : null);
    }

    /// <summary>Accumulates what happened to the keys of one import pass.</summary>
    private sealed class ImportPass
    {
        public List<string> Imported { get; } = new();

        public List<string> Updated { get; } = new();

        public List<string> Skipped { get; } = new();

        public int Failed { get; set; }

        public static ImportPass Empty => new();

        public static ImportPass From(OpenPgpImportResult result)
        {
            var pass = new ImportPass();
            pass.Add(result);
            return pass;
        }

        public void Add(ImportPass other)
        {
            Imported.AddRange(other.Imported);
            Updated.AddRange(other.Updated);
            Skipped.AddRange(other.Skipped);
            Failed += other.Failed;
        }

        private void Add(OpenPgpImportResult result)
        {
            Imported.AddRange(result.Imported);
            Updated.AddRange(result.Updated);
            Skipped.AddRange(result.Skipped);
        }

        public OpenPgpImportResult ToResult()
            => new(Imported.Distinct(StringComparer.Ordinal).ToList(),
                   Updated.Distinct(StringComparer.Ordinal).ToList(),
                   Skipped.Distinct(StringComparer.Ordinal).ToList());
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
