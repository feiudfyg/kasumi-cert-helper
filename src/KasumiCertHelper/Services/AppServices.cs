using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Services;

public static class AppServices
{
    /// <summary>A log larger than this is rolled to <c>kasumi.log.1</c> before the next entry.</summary>
    private const long MaxLogBytes = 1_048_576;

    /// <summary>
    /// Settings, databases and the log live under %APPDATA%\KasumiCertHelper by default. Setting the
    /// <c>KASUMI_APPDATA</c> environment variable redirects all of it (used by the UI tests and for
    /// portable installs).
    /// </summary>
    public static string AppDataDirectory { get; } = ResolveAppDataDirectory();

    public static string LogPath => Path.Combine(AppDataDirectory, "kasumi.log");

    public static CertificateStoreService StoreService { get; } = new();

    public static SettingsService Settings { get; private set; } = new();

    /// <summary>
    /// OpenPGP keys live in plain armored files (<c>%APPDATA%\KasumiCertHelper\pgp\keys</c>) so they can
    /// be copied into and out of a GnuPG keyring by hand. Cryptography runs in process, no gpg.exe.
    /// </summary>
    public static OpenPgpKeyStore Pgp { get; private set; } = new(DefaultPgpDirectory);

    public static X509Database? Database { get; private set; }

    public static event EventHandler? DatabaseChanged;

    /// <summary>Raised after the OpenPGP key store directory changes.</summary>
    public static event EventHandler? PgpChanged;

    public static string PgpDirectory => Pgp.Directory;

    public static string DefaultPgpDirectory => Path.Combine(AppDataDirectory, "pgp", "keys");

    public static string DefaultDatabaseDirectory => Path.Combine(AppDataDirectory, "databases");

    /// <summary>Where the user wants OpenPGP keys kept, or the default directory.</summary>
    public static string PgpKeyDirectory => ResolveDirectory(Settings.PgpKeyDirectory, DefaultPgpDirectory);

    /// <summary>Where the user wants X.509 databases kept, or the default directory.</summary>
    public static string DatabaseDirectory => ResolveDirectory(Settings.DatabaseDirectory, DefaultDatabaseDirectory);

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
        }
        catch (Exception)
        {
        }

        Settings = SettingsService.Load();

        // The language has to be settled before any window or page is created, because XAML text is
        // resolved while the page is parsed.
        if (string.IsNullOrWhiteSpace(Settings.Language))
        {
            Loc.UseSystemCulture();
        }
        else
        {
            Loc.SetCulture(Settings.Language);
        }

        Pgp = new OpenPgpKeyStore(PgpKeyDirectory);
        Log($"OpenPGP: directory={Pgp.Directory} keys={Pgp.List().Count} lang={Loc.Culture}");
    }

    /// <summary>
    /// Moves the OpenPGP key store to another directory (pass <c>null</c> for the default). Keys already
    /// in the new directory are picked up; keys in the old one are left where they are.
    /// </summary>
    public static void SetPgpKeyDirectory(string? directory)
    {
        Settings.PgpKeyDirectory = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
        Settings.Save();

        Pgp = new OpenPgpKeyStore(PgpKeyDirectory);
        Log($"OpenPGP: directory={Pgp.Directory} keys={Pgp.List().Count}");
        PgpChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Sets the directory new X.509 databases are created in (pass <c>null</c> for the default).</summary>
    public static void SetDatabaseDirectory(string? directory)
    {
        Settings.DatabaseDirectory = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
        Settings.Save();
        DatabaseChanged?.Invoke(null, EventArgs.Empty);
    }

    private static string ResolveDirectory(string? configured, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        try
        {
            return Path.GetFullPath(configured);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static void SetDatabase(X509Database? database)
    {
        Database = database;
        Settings.LastDatabasePath = database?.FilePath;
        Settings.Save();
        DatabaseChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void NotifyDatabaseChanged() => DatabaseChanged?.Invoke(null, EventArgs.Empty);

    public static void AddRecentDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Settings.RecentDatabases.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentDatabases.Insert(0, path);
        while (Settings.RecentDatabases.Count > 10)
        {
            Settings.RecentDatabases.RemoveAt(Settings.RecentDatabases.Count - 1);
        }
        Settings.Save();
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            RollLogIfTooLarge();
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The log only exists to diagnose a problem after the fact, so keeping one previous file is enough
    /// and it never grows without bound.
    /// </summary>
    private static void RollLogIfTooLarge()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxLogBytes)
        {
            return;
        }

        try
        {
            File.Move(LogPath, LogPath + ".1", overwrite: true);
        }
        catch (Exception)
        {
            // Another process holds the file; the entry below simply appends to the current one.
        }
    }

    private static string ResolveAppDataDirectory()
    {
        string? custom = Environment.GetEnvironmentVariable("KASUMI_APPDATA");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try
            {
                return Path.GetFullPath(custom);
            }
            catch (Exception)
            {
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KasumiCertHelper");
    }
}
