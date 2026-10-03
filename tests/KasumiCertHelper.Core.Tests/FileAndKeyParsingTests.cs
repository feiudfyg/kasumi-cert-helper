using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// Guards two bugs that made imports fail silently: picker filters must be plain extensions (a wildcard
/// is rejected by WinRT), and the primary fingerprints of a keyring must be read from a colon listing.
/// </summary>
public sealed class FileAndKeyParsingTests
{
    [Theory]
    [InlineData(".cer", ".cer")]
    [InlineData("cer", ".cer")]
    [InlineData("*.cer", ".cer")]
    [InlineData(" .asc ", ".asc")]
    public void ExtensionIsAlwaysAPlainDotExtension(string input, string expected)
    {
        string extension = FileFilters.Extension(input);
        Assert.Equal(expected, extension);
        Assert.DoesNotContain("*", extension, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryFingerprintsSkipSubkeysAndSecrets()
    {
        string listing = string.Join('\n',
            "tru::1:1:0:3:1:5",
            "pub:u:255:22:AAAA1111AAAA1111:100:0:::::sc:::",
            "fpr:::::::::1111111111111111111111111111111111111111:",
            "uid:u::::100::HASH::Someone <someone@example.com>::::::::::0:",
            "sub:u:255:18:BBBB2222BBBB2222:100:0:::::e:::",
            "fpr:::::::::2222222222222222222222222222222222222222:",
            "pub:u:255:22:CCCC3333CCCC3333:100:0:::::sc:::",
            "fpr:::::::::3333333333333333333333333333333333333333:",
            "sub:u:255:18:DDDD4444DDDD4444:100:0:::::e:::",
            "fpr:::::::::4444444444444444444444444444444444444444:");

        IReadOnlyList<string> fingerprints = GnupgBridge.ParsePrimaryFingerprints(listing);

        Assert.Equal(2, fingerprints.Count);
        Assert.Equal("1111111111111111111111111111111111111111", fingerprints[0]);
        Assert.Equal("3333333333333333333333333333333333333333", fingerprints[1]);
    }
}
