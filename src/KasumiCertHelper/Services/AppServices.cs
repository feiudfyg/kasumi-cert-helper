using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Services;

public static class AppServices
{
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

    public static string PgpDirectory => Pgp.Directory;

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

        Pgp = new OpenPgpKeyStore(DefaultPgpDirectory);
        Log($"OpenPGP: directory={Pgp.Directory} keys={Pgp.List().Count} lang={Loc.Culture}");
    }

    private static string DefaultPgpDirectory => Path.Combine(AppDataDirectory, "pgp", "keys");

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
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
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
