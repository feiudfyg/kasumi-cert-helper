using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace KasumiCertHelper.UiTests;

public sealed class AppFixture : IDisposable
{
    private const string WindowTitleFragment = "Kasumi";

    public AppFixture()
    {
        string executable = LocateExecutable();
        ProfileDirectory = PrepareIsolatedProfile();
        WriteTestSettings();

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        startInfo.Environment["KASUMI_APPDATA"] = ProfileDirectory;

        Application = Application.Launch(startInfo);
        Automation = new UIA3Automation();

        Window = WaitForWindow();
        Poll(() => Window.FindFirstDescendant(cf => cf.ByName(ChineseStorePage)), 60);
        Thread.Sleep(4000);
        BringToFront();
    }

    /// <summary>Navigation label of the store page, asserted by <see cref="SelectPage"/> callers.</summary>
    public const string ChineseStorePage = "证书存储";

    /// <summary>
    /// The app reads/writes settings, databases and its log under %APPDATA%\KasumiCertHelper. Point it
    /// at a throwaway directory so running the tests never disturbs the real user profile.
    /// </summary>
    public string ProfileDirectory { get; }

    private static string PrepareIsolatedProfile()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "kasumi-ui-tests",
            Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Pins the interface language. The assertions in these tests are written against the Chinese
    /// table, and it keeps the run deterministic on machines with an English display language.
    /// </summary>
    private void WriteTestSettings()
    {
        string path = Path.Combine(ProfileDirectory, "settings.json");
        File.WriteAllText(path, """{ "Language": "zh-CN" }""", System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Re-acquires the main window after the shell was rebuilt (which is what a language change does:
    /// the old window closes and a new one opens).
    /// </summary>
    public void WaitForRebuiltWindow()
    {
        int[] previous = RuntimeIdOf(Window);
        Thread.Sleep(600);

        Window? found = Poll(
            () => Application.GetAllTopLevelWindows(Automation).FirstOrDefault(w =>
            {
                if (!(w.Title ?? string.Empty).Contains(WindowTitleFragment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                int[] id = RuntimeIdOf(w);
                return id.Length > 0 && !id.SequenceEqual(previous);
            }),
            60) as Window;

        if (found is null)
        {
            throw new TimeoutException("重建语言后未能找到新的主窗口。");
        }

        Window = found;
        Window.WaitUntilClickable(TimeSpan.FromSeconds(30));
        Thread.Sleep(2500);
        BringToFront();
    }

    /// <summary>
    /// Stable identity of an element. NativeWindowHandle is not reliable here (it throws for some
    /// windows), and a stale element would otherwise make every lookup fail.
    /// </summary>
    private static int[] RuntimeIdOf(AutomationElement element)
    {
        try
        {
            return element.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>();
        }
        catch (Exception)
        {
            return Array.Empty<int>();
        }
    }

    private static bool IsSameElement(AutomationElement left, AutomationElement right)
    {
        int[] leftId = RuntimeIdOf(left);
        int[] rightId = RuntimeIdOf(right);
        return leftId.Length > 0 && leftId.SequenceEqual(rightId);
    }

    private void DeleteIsolatedProfile()
    {
        if (Environment.GetEnvironmentVariable("KASUMI_KEEP_TEST_PROFILE") == "1")
        {
            Console.WriteLine("kept test profile: " + ProfileDirectory);
            return;
        }

        try
        {
            if (Directory.Exists(ProfileDirectory))
            {
                Directory.Delete(ProfileDirectory, recursive: true);
            }
        }
        catch (Exception)
        {
        }
    }

    public void BringToFront()
    {
        try
        {
            Window.SetForeground();
        }
        catch (Exception)
        {
        }

        try
        {
            Window.Focus();
        }
        catch (Exception)
        {
        }

        Thread.Sleep(400);
    }

    public Application Application { get; }

    public UIA3Automation Automation { get; }

    public Window Window { get; private set; }

    public void Dispose()
    {
        try
        {
            if (!Application.HasExited)
            {
                Application.Close();
                Application.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (!Application.HasExited)
            {
                Application.Kill();
            }
        }
        catch (Exception)
        {
        }

        Automation.Dispose();
        DeleteIsolatedProfile();
    }

    public AutomationElement? FindById(string automationId, int timeoutSeconds = 20)
        => Poll(() => ProbeByAutomationId(automationId), timeoutSeconds);

    public AutomationElement? FindByName(string name, int timeoutSeconds = 20)
        => Poll(() => ProbeByName(name), timeoutSeconds);

    private AutomationElement? ProbeByAutomationId(string automationId)
        => Probe(w => w.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));

    private AutomationElement? ProbeByName(string name)
        => Probe(w => w.FindFirstDescendant(cf => cf.ByName(name)));

    private AutomationElement? Probe(Func<AutomationElement, AutomationElement?> probe)
    {
        AutomationElement? found = TryProbe(Window, probe);
        if (found is not null)
        {
            return found;
        }

        foreach (Window window in Application.GetAllTopLevelWindows(Automation))
        {
            if (IsSameElement(window, Window))
            {
                continue;
            }

            found = TryProbe(window, probe);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static AutomationElement? TryProbe(AutomationElement window, Func<AutomationElement, AutomationElement?> probe)
    {
        try
        {
            return probe(window);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void ClickButtonNamed(string name, int timeoutSeconds = 30)
    {
        AutomationElement? button = Poll(() => ProbeByName(name), timeoutSeconds)
            ?? Poll(() => Probe(w =>
            {
                AutomationElement[] buttons = w.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
                return buttons.LastOrDefault(b => string.Equals(b.Name, name, StringComparison.Ordinal));
            }), 5);

        if (button is null)
        {
            throw new InvalidOperationException(
                $"未找到名为 '{name}' 的按钮。\n可见的按钮：[{DescribeButtons()}]\n\n当前自动化树：\n{DumpTree(10)}");
        }

        BringToFront();
        Activate(button);
        Thread.Sleep(800);
    }

    public static void Activate(AutomationElement element)
    {
        AutomationElement target = element;
        for (int depth = 0; depth < 6 && target is not null && target.ControlType != FlaUI.Core.Definitions.ControlType.Button; depth++)
        {
            target = target.Parent;
        }

        if (target is not null && target.ControlType == FlaUI.Core.Definitions.ControlType.Button)
        {
            try
            {
                target.AsButton().Invoke();
                return;
            }
            catch (Exception)
            {
            }
        }

        try
        {
            FlaUI.Core.Patterns.ISelectionItemPattern? selectionItem = element.Patterns.SelectionItem.PatternOrDefault;
            if (selectionItem is not null)
            {
                selectionItem.Select();
                return;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            element.Click();
        }
        catch (Exception)
        {
        }
    }

    public string DescribeButtons()
    {
        var names = new List<string>();
        IEnumerable<Window> windows = new[] { Window }.Concat(Application.GetAllTopLevelWindows(Automation));
        foreach (Window window in windows)
        {
            try
            {
                foreach (AutomationElement button in window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)))
                {
                    names.Add(button.Name ?? "(null)");
                }
            }
            catch (Exception)
            {
            }
        }

        return string.Join(" | ", names.Distinct());
    }

    public AutomationElement? Poll(Func<AutomationElement?> probe, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            try
            {
                AutomationElement? found = probe();
                if (found is not null)
                {
                    return found;
                }
            }
            catch (Exception)
            {
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            Thread.Sleep(350);
        }
    }

    public bool WaitUntil(Func<bool> probe, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            try
            {
                if (probe())
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(350);
        }
    }

    public AutomationElement RequireById(string automationId, int timeoutSeconds = 20)
        => FindById(automationId, timeoutSeconds)
           ?? throw new InvalidOperationException(
               $"未找到 AutomationId='{automationId}' 的控件。\n\n顶级窗口：{DescribeWindows()}\n\n当前自动化树：\n{DumpTree()}\n{DescribeOtherWindows()}");

    /// <summary>Dumps the tree of every other top level window, which is where the probe looks too.</summary>
    private string DescribeOtherWindows()
    {
        var builder = new StringBuilder();

        foreach (Window window in Application.GetAllTopLevelWindows(Automation))
        {
            try
            {
                builder.AppendLine().Append("其它窗口 ").Append(window.Title).AppendLine(":");
                Dump(window, builder, 0, 8);
            }
            catch (Exception exception)
            {
                builder.AppendLine().Append("  (无法读取: ").Append(exception.Message).AppendLine(")");
            }
        }

        return builder.ToString();
    }

    /// <summary>Handles and titles of every top level window, for diagnosing stale window elements.</summary>
    public string DescribeWindows()
    {
        var builder = new StringBuilder();
        builder.Append("current=").Append(HandleOf(Window));

        foreach (Window window in Application.GetAllTopLevelWindows(Automation))
        {
            builder.Append(" | ").Append(HandleOf(window)).Append(':');
            try { builder.Append(window.Title); } catch (Exception) { builder.Append('?'); }
        }

        return builder.ToString();
    }

    private static string HandleOf(AutomationElement element)
    {
        try
        {
            return "0x" + element.Properties.NativeWindowHandle.ValueOrDefault.ToString("X");
        }
        catch (Exception)
        {
            return "(unknown)";
        }
    }

    public void SelectPage(string navigationItemName, string expectedAutomationId)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            AutomationElement? item = Poll(() =>
            {
                AutomationElement[] matches = Window.FindAllDescendants(cf => cf.ByName(navigationItemName));
                return matches.FirstOrDefault(m => m.ControlType == FlaUI.Core.Definitions.ControlType.ListItem)
                       ?? matches.FirstOrDefault();
            }, 20);

            if (item is null)
            {
                throw new InvalidOperationException(
                    $"未找到导航项 '{navigationItemName}'。\n\n当前自动化树：\n{DumpTree()}");
            }

            BringToFront();

            try
            {
                item.Click();
            }
            catch (Exception)
            {
            }

            if (FindById(expectedAutomationId, 15) is not null)
            {
                Thread.Sleep(1500);
                return;
            }
        }

        throw new InvalidOperationException(
            $"点击导航项 '{navigationItemName}' 后未出现 AutomationId='{expectedAutomationId}'。\n\n当前自动化树：\n{DumpTree()}");
    }

    public string DumpTree(int maxDepth = 6)
    {
        var builder = new StringBuilder();
        Dump(Window, builder, 0, maxDepth);
        return builder.ToString();
    }

    private static void Dump(AutomationElement element, StringBuilder builder, int depth, int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        string controlType;
        string id;
        string name;
        try { controlType = element.ControlType.ToString(); } catch (Exception) { controlType = "?"; }
        try { id = element.AutomationId ?? string.Empty; } catch (Exception) { id = "?"; }
        try { name = element.Name ?? string.Empty; } catch (Exception) { name = "?"; }

        builder.Append(new string(' ', depth * 2))
               .Append(controlType)
               .Append(" id='").Append(id)
               .Append("' name='").Append(name)
               .AppendLine("'");

        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch (Exception) { return; }

        foreach (AutomationElement child in children)
        {
            Dump(child, builder, depth + 1, maxDepth);
        }
    }

    private Window WaitForWindow()
    {
        RetryResult<Window?> result = Retry.WhileNull(
            () => Application.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(w => (w.Title ?? string.Empty).Contains(WindowTitleFragment, StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(500));

        Window? window = result.Result;
        if (window is null)
        {
            throw new TimeoutException("未能在 120 秒内找到应用程序主窗口。");
        }

        window.WaitUntilClickable(TimeSpan.FromSeconds(30));
        return window;
    }

    private static string LocateExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KasumiCertHelper.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("未能定位解决方案根目录（KasumiCertHelper.slnx）。");
        }

        string executable = Path.Combine(
            directory.FullName,
            "src", "KasumiCertHelper", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64",
            "KasumiCertHelper.exe");

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("未找到应用程序可执行文件，请先构建解决方案。", executable);
        }

        return executable;
    }
}

[CollectionDefinition("kasumi-app")]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
}
