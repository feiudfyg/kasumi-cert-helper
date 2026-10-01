using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void ShipsAnEnglishAndAChineseTable()
    {
        Assert.Equal(new[] { "en", "zh-CN" }, Loc.SupportedCultures.ToArray());
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-Hans-CN", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void ResolvesUnsupportedCulturesToTheNeutralTable(string? culture, string expected)
    {
        Assert.Equal(expected, Loc.ResolveCulture(culture));
    }

    [Fact]
    public void ReadsBothTables()
    {
        Assert.Equal("Kasumi Certificate Helper", Loc.GetFor("en", "App_Title"));
        Assert.Equal("Kasumi 证书助手", Loc.GetFor("zh-CN", "App_Title"));
    }

    [Fact]
    public void UnknownCultureFallsBackToEnglish()
    {
        Assert.Equal("Kasumi Certificate Helper", Loc.GetFor("fr-FR", "App_Title"));
    }

    [Fact]
    public void UnknownKeyReturnsTheKeySoGapsAreVisible()
    {
        Assert.Equal("No_Such_Key", Loc.GetFor("en", "No_Such_Key"));
    }

    /// <summary>
    /// Both tables must stay in step: a key added to one language and forgotten in the other would
    /// silently show English text (or a raw key) to Chinese users.
    /// </summary>
    [Fact]
    public void EveryKeyExistsInEveryLanguage()
    {
        string[] english = Loc.Keys("en").ToArray();

        foreach (string culture in Loc.SupportedCultures)
        {
            string[] keys = Loc.Keys(culture).ToArray();

            Assert.True(
                english.SequenceEqual(keys, StringComparer.Ordinal),
                $"Language '{culture}' does not define the same keys as 'en'.\n" +
                $"Missing: [{string.Join(", ", english.Except(keys, StringComparer.Ordinal))}]\n" +
                $"Unexpected: [{string.Join(", ", keys.Except(english, StringComparer.Ordinal))}]");
        }
    }

    /// <summary>No value may be blank, otherwise the UI would render an empty label.</summary>
    [Fact]
    public void NoValueIsBlank()
    {
        foreach (string culture in Loc.SupportedCultures)
        {
            foreach (string key in Loc.Keys(culture))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(Loc.GetFor(culture, key)),
                    $"'{key}' is blank in '{culture}'.");
            }
        }
    }

    /// <summary>Placeholders in a template must match between languages or Format would throw.</summary>
    [Fact]
    public void TemplatesUseTheSamePlaceholdersInEveryLanguage()
    {
        foreach (string key in Loc.Keys("en"))
        {
            string english = Loc.GetFor("en", key);
            if (!english.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            string[] englishSlots = SlotNames(english);

            foreach (string culture in Loc.SupportedCultures)
            {
                string[] slots = SlotNames(Loc.GetFor(culture, key));
                Assert.True(
                    englishSlots.SequenceEqual(slots, StringComparer.Ordinal),
                    $"'{key}' uses different placeholders in '{culture}': " +
                    $"[{string.Join(", ", englishSlots)}] vs [{string.Join(", ", slots)}].");
            }
        }
    }

    [Fact]
    public void SetCultureSwitchesTheActiveLanguage()
    {
        string original = Loc.Culture;
        try
        {
            Loc.SetCulture("zh-CN");
            Assert.Equal("zh-CN", Loc.Culture);
            Assert.Equal("Kasumi 证书助手", Loc.Get("App_Title"));

            Loc.SetCulture("en-US");
            Assert.Equal("en", Loc.Culture);
            Assert.Equal("Kasumi Certificate Helper", Loc.Get("App_Title"));
        }
        finally
        {
            Loc.SetCulture(original);
        }
    }

    private static string[] SlotNames(string template)
    {
        var names = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(template, @"\{(\d+)\}"))
        {
            names.Add(match.Groups[1].Value);
        }

        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }
}
