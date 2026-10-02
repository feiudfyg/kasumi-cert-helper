namespace KasumiCertHelper.Services;

/// <summary>
/// Facts the about section shows: who holds the copyright, where the source lives and which third
/// party libraries ship with the application. The GPLv3 requires the source of a distributed binary
/// to be obtainable, which is what <see cref="RepositoryUrl"/> is for.
/// </summary>
public static class ProductInfo
{
    public const string CopyrightHolder = "feiudfyg";

    public const string CopyrightYear = "2026";

    public const string RepositoryUrl = "https://github.com/feiudfyg/kasumi-cert-helper";

    public const string LicenseName = "GNU General Public License v3.0";

    /// <summary>File name of the third party notices, shipped next to the executable.</summary>
    public const string NoticesFileName = "THIRD-PARTY-NOTICES.md";

    /// <summary>File name of the license text, shipped next to the executable.</summary>
    public const string LicenseFileName = "LICENSE";

    /// <summary>Used when the notices file is not next to the executable (development builds).</summary>
    public static string NoticesUrl => RepositoryUrl + "/blob/main/" + NoticesFileName;
}
