using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace KasumiCertHelper.UiTests;

internal static class UiHelpers
{
    public static AutomationElement[] ListItems(AppFixture app, string automationId)
    {
        try
        {
            AutomationElement? list = app.FindById(automationId, 10);
            return list is null
                ? Array.Empty<AutomationElement>()
                : list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
        }
        catch (Exception)
        {
            return Array.Empty<AutomationElement>();
        }
    }

    public static AutomationElement[] WaitForListItems(AppFixture app, string automationId, TimeSpan timeout)
    {
        app.WaitUntil(() => ListItems(app, automationId).Length > 0, (int)timeout.TotalSeconds);
        return ListItems(app, automationId);
    }

    public static string TextOf(AppFixture app, string automationId)
    {
        try
        {
            return app.FindById(automationId, 10)?.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static bool WaitForText(AppFixture app, string automationId, string fragment, int timeoutSeconds)
        => app.WaitUntil(() => TextOf(app, automationId).Contains(fragment, StringComparison.Ordinal), timeoutSeconds);

    public static RectangleF RectOf(AppFixture app, string automationId)
    {
        AutomationElement element = app.RequireById(automationId);
        return element.BoundingRectangle;
    }

    /// <summary>Detail sections are collapsible; expand one by its title before asserting on it.</summary>
    public static void ExpandSection(AppFixture app, string title)
    {
        AutomationElement section = app.RequireById("Section_" + title, 20);
        try
        {
            if (section.Patterns.ExpandCollapse.PatternOrDefault is { } pattern)
            {
                if (!pattern.ExpandCollapseState.Value.HasFlag(ExpandCollapseState.Expanded))
                {
                    pattern.Expand();
                }
            }
        }
        catch (Exception)
        {
        }

        Thread.Sleep(500);
    }

    /// <summary>
    /// Regression guard for the old layout bug where a toolbar grew wider than the window and the
    /// trailing buttons could not be clicked at all.
    /// </summary>
    public static void AssertReachable(AppFixture app, params string[] automationIds)
    {
        RectangleF window = app.Window.BoundingRectangle;
        var problems = new List<string>();

        foreach (string id in automationIds)
        {
            AutomationElement? element = app.FindById(id, 20);
            if (element is null)
            {
                problems.Add($"{id}: 未找到");
                continue;
            }

            RectangleF rect = element.BoundingRectangle;
            if (rect.Width < 2 || rect.Height < 2)
            {
                problems.Add($"{id}: 尺寸为 {rect.Width}x{rect.Height}（不可见）");
                continue;
            }

            if (rect.Left < window.Left - 2 || rect.Top < window.Top - 2 ||
                rect.Right > window.Right + 2 || rect.Bottom > window.Bottom + 2)
            {
                problems.Add($"{id}: 位于窗口之外 {rect}");
            }
        }

        Assert.True(problems.Count == 0,
            "以下控件不可点击：\n" + string.Join("\n", problems) + "\n\n窗口：" + window + "\n" + app.DumpTree(7));
    }

    /// <summary>Header cells must stay ordered and must not spill past the list they describe.</summary>
    public static void AssertHeadersFitList(AppFixture app, string listAutomationId, params string[] headerAutomationIds)
    {
        RectangleF list = RectOf(app, listAutomationId);
        var rects = new List<(string Id, RectangleF Rect)>();

        foreach (string id in headerAutomationIds)
        {
            rects.Add((id, RectOf(app, id)));
        }

        for (int i = 1; i < rects.Count; i++)
        {
            double previousLeft = rects[i - 1].Rect.Left;
            double currentLeft = rects[i].Rect.Left;
            Assert.True(currentLeft >= previousLeft,
                $"列头顺序错误：{rects[i - 1].Id}(x={previousLeft}) 在 {rects[i].Id}(x={currentLeft}) 之后。");
        }

        RectangleF last = rects[^1].Rect;
        Assert.True(last.Right <= list.Right + 24,
            $"最后一列 \"{rects[^1].Id}\" 右边缘 {last.Right} 超出了列表右边缘 {list.Right}，内容会被裁掉。");
    }
}

[Collection("kasumi-app")]
public class ShellTests
{
    private readonly AppFixture _app;

    public ShellTests(AppFixture app) => _app = app;

    [Theory]
    [InlineData("证书存储")]
    [InlineData("X.509 证书管理")]
    [InlineData("OpenPGP")]
    public void NavigationItemExists(string name)
    {
        AutomationElement? item = _app.FindByName(name, 40);
        Assert.True(item is not null, $"导航栏缺少 '{name}'。\n{_app.DumpTree()}");
    }
}

[Collection("kasumi-app")]
public class StoresPageTests
{
    private readonly AppFixture _app;

    public StoresPageTests(AppFixture app) => _app = app;

    [Fact]
    public void UserStoreListIsPopulated()
    {
        _app.SelectPage("证书存储", "UserStoreList");

        AutomationElement[] items = UiHelpers.WaitForListItems(_app, "UserStoreList", TimeSpan.FromSeconds(40));
        Assert.NotEmpty(items);

        string[] names = items.Select(i => i.Name ?? string.Empty).ToArray();
        Assert.Contains(names, n => n.Contains("个人"));
        Assert.Contains(names, n => n.Contains("受信任的根证书颁发机构"));
        Assert.Contains(names, n => n.Contains("("));
    }

    [Fact]
    public void ClickingAStoreLoadsItsCertificates()
    {
        _app.SelectPage("证书存储", "UserStoreList");

        AutomationElement[] stores = UiHelpers.WaitForListItems(_app, "UserStoreList", TimeSpan.FromSeconds(40));
        Assert.NotEmpty(stores);

        AutomationElement rootStore = stores.First(i => (i.Name ?? string.Empty).Contains("受信任的根证书颁发机构"));
        AppFixture.Activate(rootStore);

        Assert.True(
            UiHelpers.WaitForText(_app, "StoreTitleText", "受信任的根证书颁发机构", 40),
            $"切换到根证书存储区后标题未更新，当前标题：'{UiHelpers.TextOf(_app, "StoreTitleText")}'");

        AutomationElement[] certificates = UiHelpers.WaitForListItems(_app, "CertList", TimeSpan.FromSeconds(40));
        Assert.NotEmpty(certificates);

        AppFixture.Activate(certificates[0]);

        Assert.True(
            UiHelpers.WaitForText(_app, "DetailsHeader", "证书", 40) || !string.IsNullOrEmpty(UiHelpers.TextOf(_app, "DetailsHeader")),
            "选择证书后详细信息标题未更新。");

        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Subject")),
            "选择证书后未显示解析后的使用者信息。\n" + _app.DumpTree(9));
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Validity")),
            "选择证书后未显示解析后的有效期信息。");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Thumbprint")),
            "选择证书后未显示指纹。");

        AutomationElement[] machineStores = UiHelpers.WaitForListItems(_app, "MachineStoreList", TimeSpan.FromSeconds(40));
        Assert.NotEmpty(machineStores);
    }

    [Fact]
    public void SearchingFiltersCertificates()
    {
        _app.SelectPage("证书存储", "UserStoreList");

        AutomationElement[] stores = UiHelpers.WaitForListItems(_app, "UserStoreList", TimeSpan.FromSeconds(40));
        stores.First(i => (i.Name ?? string.Empty).Contains("个人")).Click();

        AutomationElement[] before = UiHelpers.WaitForListItems(_app, "CertList", TimeSpan.FromSeconds(40));
        Assert.NotEmpty(before);

        AutomationElement search = _app.RequireById("SearchBox");
        search.AsTextBox().Text = "zzz-no-such-certificate-zzz";
        Thread.Sleep(1500);

        AutomationElement[] after = UiHelpers.ListItems(_app, "CertList");
        Assert.Empty(after);
    }

    [Fact]
    public void ToolbarButtonsAreReachable()
    {
        _app.SelectPage("证书存储", "UserStoreList");

        UiHelpers.AssertReachable(_app,
            "RefreshButton", "ImportButton", "ExportButton", "DeleteButton",
            "CopyButton", "CopyDetailsButton", "CertMgrButton", "ElevateButton",
            "DetailsToggleButton");
    }

    [Fact]
    public void TableColumnsFitInsideTheList()
    {
        _app.SelectPage("证书存储", "UserStoreList");

        UiHelpers.WaitForListItems(_app, "UserStoreList", TimeSpan.FromSeconds(40));
        UiHelpers.AssertHeadersFitList(_app, "CertList",
            "CertHeaderSubject", "CertHeaderIssuer", "CertHeaderNotAfter", "CertHeaderStatus");
    }

    [Fact]
    public void DetailsPaneCanBeCollapsedAndRestored()
    {
        _app.SelectPage("证书存储", "UserStoreList");
        Assert.NotNull(_app.RequireById("DetailsPane"));

        AppFixture.Activate(_app.RequireById("DetailsToggleButton"));
        Assert.True(
            _app.WaitUntil(() => _app.FindById("DetailsPane", 3) is null, 20),
            "点击「收起详情」后详细信息面板仍然可见。");

        AppFixture.Activate(_app.RequireById("DetailsToggleButton"));
        Assert.True(
            _app.WaitUntil(() => _app.FindById("DetailsPane", 3) is not null, 20),
            "点击「显示详情」后详细信息面板没有回来。");
    }
}

[Collection("kasumi-app")]
public class GpgPageTests
{
    private readonly AppFixture _app;

    public GpgPageTests(AppFixture app) => _app = app;

    [Fact]
    public void GeneratingAKeyPairListsItAndShowsParsedDetails()
    {
        _app.SelectPage("OpenPGP", "KeyList");

        string name = "Kasumi UI Test " + Guid.NewGuid().ToString("N")[..6];

        AppFixture.Activate(_app.RequireById("GenerateButton"));
        _app.RequireById("GpgKeyNameBox", 30).AsTextBox().Enter(name);
        _app.ClickButtonNamed("生成");

        // The operation reports what it did in a modal dialog, which has to be dismissed.
        _app.ClickButtonNamed("确定", 90);

        AutomationElement[] keys = UiHelpers.WaitForListItems(_app, "KeyList", TimeSpan.FromSeconds(90));
        Assert.NotEmpty(keys);
        Assert.Contains(keys, k => (k.Name ?? string.Empty).Contains(name, StringComparison.Ordinal));

        AppFixture.Activate(keys[0]);

        Assert.True(
            _app.WaitUntil(() => !string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Fingerprint")), 40),
            "选择密钥后未显示解析后的指纹。\n" + _app.DumpTree(9));

        string fingerprint = UiHelpers.TextOf(_app, "Detail_Fingerprint");
        Assert.Contains(" ", fingerprint);
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Algorithm")), "未显示算法。");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Status")), "未显示状态。");

        // The armored public key lives in a collapsed section.
        UiHelpers.ExpandSection(_app, "公钥");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_PublicArmor")), "未显示公钥。");
    }

    [Fact]
    public void ToolbarButtonsAreReachable()
    {
        _app.SelectPage("OpenPGP", "KeyList");

        UiHelpers.AssertReachable(_app,
            "RefreshButton", "GenerateButton", "ImportButton", "DeleteButton",
            "ExportPublicButton", "ExportSecretButton", "CopyFingerprintButton",
            "EncryptButton", "DecryptButton", "SignButton", "VerifyButton");
    }

    [Fact]
    public void TableColumnsFitInsideTheList()
    {
        _app.SelectPage("OpenPGP", "KeyList");

        UiHelpers.WaitForListItems(_app, "KeyList", TimeSpan.FromSeconds(90));
        UiHelpers.AssertHeadersFitList(_app, "KeyList",
            "KeyHeaderName", "KeyHeaderAlgorithm", "KeyHeaderExpires", "KeyHeaderSecret");
    }
}

[Collection("kasumi-app")]
public class X509PageTests
{
    private readonly AppFixture _app;

    public X509PageTests(AppFixture app) => _app = app;

    [Fact]
    public void ToolbarAndItemListAreAvailable()
    {
        _app.SelectPage("X.509 证书管理", "ItemList");

        Assert.NotNull(_app.RequireById("NewDbButton"));
        Assert.NotNull(_app.RequireById("OpenDbButton"));
        Assert.NotNull(_app.RequireById("NewKeyButton"));
        Assert.NotNull(_app.RequireById("NewCertButton"));
        Assert.NotNull(_app.RequireById("NewCsrButton"));
        Assert.NotNull(_app.RequireById("SignCsrButton"));
        Assert.NotNull(_app.RequireById("ImportButton"));
        Assert.NotNull(_app.RequireById("ExportButton"));

        UiHelpers.AssertReachable(_app,
            "NewDbButton", "OpenDbButton", "CloseDbButton", "NewKeyButton", "NewCertButton",
            "NewCaButton", "NewCsrButton", "SignCsrButton", "ImportButton", "ExportButton",
            "DeleteButton", "PasswordButton");

        UiHelpers.AssertHeadersFitList(_app, "ItemList", "ItemHeaderName", "ItemHeaderKind", "ItemHeaderStatus");
    }

    [Fact]
    public void EndToEndCreateDatabaseKeyCertificateCsrAndSignedCertificate()
    {
        // Everything is written into the fixture's throwaway profile, so no cleanup is needed here.
        string dbName = "uitest-" + Guid.NewGuid().ToString("N")[..8];

        _app.SelectPage("X.509 证书管理", "ItemList");

        AppFixture.Activate(_app.RequireById("NewDbButton"));
        _app.RequireById("DbName", 30).AsTextBox().Enter(dbName);
        _app.RequireById("DbPassword", 30).AsTextBox().Enter("pw-123456");
        _app.RequireById("DbPasswordConfirm", 30).AsTextBox().Enter("pw-123456");
        _app.ClickButtonNamed("创建");

        Assert.True(
            UiHelpers.WaitForText(_app, "DatabaseTitle", dbName, 30),
            $"数据库未创建成功，当前标题：'{UiHelpers.TextOf(_app, "DatabaseTitle")}'");

        AppFixture.Activate(_app.RequireById("NewKeyButton"));
        _app.RequireById("KeyName", 30).AsTextBox().Enter("ui-test-key");
        _app.ClickButtonNamed("生成");
        Assert.True(
            _app.WaitUntil(() => UiHelpers.ListItems(_app, "ItemList").Length == 1, 30),
            "生成密钥后项目列表中没有出现该项目。");

        AppFixture.Activate(_app.RequireById("NewCertButton"));
        _app.RequireById("CertName", 30).AsTextBox().Enter("ui-test-cert");
        _app.RequireById("CertCommonName", 30).AsTextBox().Enter("ui.example.com");
        _app.ClickButtonNamed("生成");
        Assert.True(
            _app.WaitUntil(() => UiHelpers.ListItems(_app, "ItemList").Length == 2, 30),
            "生成自签名证书后项目列表中没有出现该项目。");

        AppFixture.Activate(_app.RequireById("NewCsrButton"));
        _app.RequireById("CertName", 30).AsTextBox().Enter("ui-test-csr");
        _app.RequireById("CertCommonName", 30).AsTextBox().Enter("csr.example.com");
        _app.ClickButtonNamed("生成");
        Assert.True(
            _app.WaitUntil(() => UiHelpers.ListItems(_app, "ItemList").Length == 3, 30),
            "生成证书请求后项目列表中没有出现该项目。");

        AppFixture.Activate(_app.RequireById("NewCaButton"));
        _app.RequireById("CertName", 30).AsTextBox().Enter("ui-test-ca");
        _app.RequireById("CertCommonName", 30).AsTextBox().Enter("ui-test-ca");
        AutomationElement caCheck = _app.RequireById("CertIsCa", 30);
        if (caCheck.AsCheckBox().IsChecked != true)
        {
            caCheck.AsCheckBox().IsChecked = true;
        }
        _app.ClickButtonNamed("生成");
        Assert.True(
            _app.WaitUntil(() => UiHelpers.ListItems(_app, "ItemList").Length == 4, 30),
            "生成 CA 证书后项目列表中没有出现该项目。");

        AppFixture.Activate(_app.RequireById("SignCsrButton"));
        _app.ClickButtonNamed("签发");
        Assert.True(
            _app.WaitUntil(() => UiHelpers.ListItems(_app, "ItemList").Length == 5, 30),
            "用 CA 签发后项目列表中没有出现签发的证书。\n" + _app.DumpTree(9));

        AutomationElement[] items = UiHelpers.ListItems(_app, "ItemList");
        AutomationElement certificate = items.First(i => (i.Name ?? string.Empty).Contains("ui-test-cert"));
        AppFixture.Activate(certificate);

        Assert.True(
            UiHelpers.WaitForText(_app, "Detail_Subject", "ui.example.com", 40),
            $"选择证书后未显示解析后的使用者信息，当前值：'{UiHelpers.TextOf(_app, "Detail_Subject")}'");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Validity")), "未显示有效期。");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_Thumbprint256")), "未显示 SHA-256 指纹。");

        UiHelpers.ExpandSection(_app, "公钥与签名");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_PublicKeyAlgorithm")), "未显示公钥算法。");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_KeySize")), "未显示密钥长度。");

        UiHelpers.ExpandSection(_app, "扩展");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_IsCa")), "未显示 CA 标记。");
        Assert.False(string.IsNullOrWhiteSpace(UiHelpers.TextOf(_app, "Detail_KeyUsage")), "未显示密钥用法。");
    }
}

[Collection("kasumi-app")]
public class SettingsPageTests
{
    private readonly AppFixture _app;

    public SettingsPageTests(AppFixture app) => _app = app;

    [Fact]
    public void SettingsPageLoads()
    {
        OpenSettings();

        Assert.NotNull(_app.RequireById("LanguageBox"));
        Assert.NotNull(_app.RequireById("ElevationText"));
        Assert.NotNull(_app.RequireById("PgpKeyCountText"));
        Assert.NotNull(_app.RequireById("PgpDirectoryText"));
        Assert.NotNull(_app.RequireById("OpenPgpFolderButton"));
    }

    /// <summary>
    /// The interface language is chosen in the settings. Every XAML string is resolved while a page is
    /// parsed, so switching has to rebuild the shell; this checks both languages really render.
    /// </summary>
    [Fact]
    public void SwitchingLanguageRebuildsTheInterfaceInTheOtherLanguage()
    {
        OpenSettings();

        try
        {
            SelectLanguage(1);

            Assert.True(
                _app.WaitUntil(() => _app.FindByName("Certificate stores", 5) is not null, 40),
                "切到英文后导航栏没有显示英文的存储页面名称。\n" + _app.DumpTree(6));

            Assert.Contains("Kasumi Certificate Helper", _app.Window.Title ?? string.Empty);
            Assert.True(
                _app.WaitUntil(() => _app.FindByName("Settings", 5) is not null, 20),
                "英文界面里没有找到 Settings。");

            // The other two pages have the largest number of strings, so check their toolbars too.
            _app.SelectPage("X.509 certificates", "ItemList");
            Assert.True(
                _app.WaitUntil(() => _app.FindByName("New database", 5) is not null, 20),
                "英文界面的 X.509 工具栏没有本地化。\n" + _app.DumpTree(7));

            _app.SelectPage("OpenPGP", "KeyList");
            Assert.True(
                _app.WaitUntil(() => _app.FindByName("Generate key pair", 5) is not null, 20),
                "英文界面的 GPG 工具栏没有本地化。\n" + _app.DumpTree(7));
        }
        finally
        {
            // Restore Chinese: the other tests in this collection assert Chinese labels.
            SelectLanguage(2);

            Assert.True(
                _app.WaitUntil(() => _app.FindByName(AppFixture.ChineseStorePage, 5) is not null, 40),
                "切回中文后导航栏没有恢复中文名称。");
        }
    }

    private void OpenSettings()
    {
        AutomationElement? settingsItem = _app.Poll(() =>
        {
            AutomationElement[] matches = _app.Window.FindAllDescendants(cf => cf.ByName("设置"));
            return matches.FirstOrDefault(m => m.ControlType == ControlType.ListItem) ?? matches.FirstOrDefault();
        }, 30) ?? _app.Poll(() =>
        {
            AutomationElement[] matches = _app.Window.FindAllDescendants(cf => cf.ByName("Settings"));
            return matches.FirstOrDefault(m => m.ControlType == ControlType.ListItem) ?? matches.FirstOrDefault();
        }, 30);

        Assert.NotNull(settingsItem);
        AppFixture.Activate(settingsItem!);
        Thread.Sleep(2000);
    }

    /// <summary>Index 0 follows Windows, 1 is English, 2 is Chinese (see Loc.SupportedCultures).</summary>
    private void SelectLanguage(int index)
    {
        // The dropdown only exists on the settings page, which the caller may have navigated away from.
        OpenSettings();

        AutomationElement box = _app.RequireById("LanguageBox", 30);

        if (!_app.WaitUntil(() => box.AsComboBox().Items.Length > index, 20))
        {
            throw new InvalidOperationException("语言下拉列表中没有足够的条目。");
        }

        box.AsComboBox().Select(index);
        _app.WaitForRebuiltWindow();
    }
}
