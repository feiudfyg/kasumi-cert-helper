using System.Text.RegularExpressions;
using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// GPLv3 requires the source of a distributed binary to be obtainable and the licenses of bundled
/// libraries to travel with it. These tests keep the about page, the license files and the package
/// versions from drifting apart.
/// </summary>
public class AboutNoticeTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void AboutPageNamesTheCopyrightHolderAndTheLicense(string culture)
    {
        string about = Loc.GetFor(culture, "Settings_AboutText");
        string copyright = Loc.GetFor(culture, "Settings_Copyright");
        string license = Loc.GetFor(culture, "Settings_LicenseLine");

        Assert.Contains("feiudfyg", copyright, StringComparison.Ordinal);
        Assert.Contains("2026", copyright, StringComparison.Ordinal);
        Assert.Contains("GPLv3", license, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(about));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void AboutPageNamesEveryBundledLibraryWithItsVersion(string culture)
    {
        string components = Loc.GetFor(culture, "Settings_AboutComponents");

        foreach (string version in BundledPackageVersions())
        {
            Assert.Contains(version, components, StringComparison.Ordinal);
        }

        Assert.Contains("MIT", components, StringComparison.Ordinal);
    }

    /// <summary>The source offer the GPLv3 asks for has to be visible in the interface.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void AboutPagePointsAtTheSourceCode(string culture)
    {
        string label = Loc.GetFor(culture, "Settings_SourceCodeLabel");

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Contains("GPLv3", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LicenseAndNoticesShipWithTheRepository()
    {
        Assert.True(File.Exists(Path.Combine(Root, "LICENSE")), "LICENSE is missing");
        Assert.True(File.Exists(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")), "THIRD-PARTY-NOTICES.md is missing");

        string license = File.ReadAllText(Path.Combine(Root, "LICENSE"));
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", license, StringComparison.Ordinal);
        Assert.Contains("Version 3, 29 June 2007", license, StringComparison.Ordinal);

        string notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("BouncyCastle", notices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommunityToolkit", notices, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The license and the notices have to be copied next to the built executable.</summary>
    [Fact]
    public void LicenseAndNoticesAreCopiedNextToTheExecutable()
    {
        foreach (string file in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" })
        {
            Assert.True(
                File.Exists(Path.Combine(AppOutputDirectory(), file)),
                $"{file} was not copied next to the application executable");
        }
    }

    /// <summary>Where the application of the configuration under test was built.</summary>
    private static string AppOutputDirectory()
    {
        string configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        return Path.Combine(
            Root, "src", "KasumiCertHelper", "bin", configuration, "net8.0-windows10.0.19041.0", "win-x64");
    }

    /// <summary>Versions of the libraries that end up in a published build, read from the projects.</summary>
    private static IEnumerable<string> BundledPackageVersions()
    {
        var packages = new List<(string Project, string Id)>
        {
            ("src/KasumiCertHelper/KasumiCertHelper.csproj", "CommunityToolkit.Mvvm"),
            ("src/KasumiCertHelper/KasumiCertHelper.csproj", "Microsoft.WindowsAppSDK.WinUI"),
            ("src/KasumiCertHelper/KasumiCertHelper.csproj", "Microsoft.WindowsAppSDK.Runtime"),
            ("src/KasumiCertHelper.Core/KasumiCertHelper.Core.csproj", "BouncyCastle.Cryptography"),
        };

        var versions = new List<string>();

        foreach ((string project, string id) in packages)
        {
            string path = Path.Combine(Root, project.Replace('/', Path.DirectorySeparatorChar));
            string text = File.ReadAllText(path);
            string pattern = "<PackageReference\\s+Include=\"" + Regex.Escape(id) + "\"\\s+Version=\"([^\"]+)\"";
            Match match = Regex.Match(text, pattern);

            Assert.True(match.Success, $"{id} is not referenced from {project}");
            versions.Add(match.Groups[1].Value);
        }

        return versions.Distinct(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KasumiCertHelper.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("KasumiCertHelper.slnx was not found above the test output.");
    }
}
