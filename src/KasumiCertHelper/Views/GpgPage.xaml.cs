using System.Collections.ObjectModel;
using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using KasumiCertHelper.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace KasumiCertHelper.Views;

public sealed partial class GpgPage : Page
{
    private const string ColumnsKey = "Gpg.Columns";
    private const string ListPaneKey = "Gpg.ListPaneWidth";

    private readonly ObservableCollection<GpgRow> _visible = new();
    private List<GpgKey> _all = new();
    private bool _initialized;

    public GpgPage()
    {
        InitializeComponent();
        KeyList.ItemsSource = _visible;
        ApplyStoredLayout();
        FilterBox.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(52, 250, 110, 100, 56);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Settings.GetLayoutDouble(ListPaneKey, 520), GridUnitType.Pixel);
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(ListPaneKey, ContentGrid.ColumnDefinitions[0].ActualWidth);
    }

    private void OnKeyPaneSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(320, ContentGrid.ActualWidth - 260);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(ContentGrid.ColumnDefinitions[0].ActualWidth + e.Delta.Translation.X, 280, maximum),
            GridUnitType.Pixel);
        PersistLayout();
    }

    private void OnColumnResize(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag || !int.TryParse(tag, out int index))
        {
            return;
        }

        Columns[index] = Columns[index] + e.Delta.Translation.X;
        PersistLayout();
    }

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e) => Columns.FitTo(e.NewSize.Width - 20);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        UpdateDetails(null);
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private void UpdateGpgState()
    {
        GpgService gpg = AppServices.Gpg;
        GpgInfoBar.IsOpen = !gpg.IsAvailable;

        if (!gpg.IsAvailable)
        {
            DetailStatusText.Text = "未找到 GnuPG，请安装 Gpg4win 或在设置中指定 gpg.exe 的路径。";
            return;
        }

        string origin = gpg.IsBundled ? "内置副本" : "系统安装";
        string version = string.Empty;
        try
        {
            version = gpg.GetVersion();
        }
        catch (Exception ex)
        {
            AppServices.Log("读取 GPG 版本失败: " + ex.Message);
        }

        DetailStatusText.Text = string.IsNullOrWhiteSpace(version)
            ? $"使用 {origin}：{gpg.ExecutablePath}"
            : $"{version}（{origin}）";
        ToolTipService.SetToolTip(GpgInfoBar, gpg.ExecutablePath);
    }

    private async Task LoadKeysAsync()
    {
        if (!AppServices.Gpg.IsAvailable)
        {
            _all = new List<GpgKey>();
            ApplyFilter();
            return;
        }

        try
        {
            BusyRing.IsActive = true;
            GpgService gpg = AppServices.Gpg;
            _all = await Task.Run(() => gpg.ListAllKeys().ToList());
        }
        catch (Exception ex)
        {
            _all = new List<GpgKey>();
            await DialogService.ShowErrorAsync("读取 GPG 密钥失败", ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (KeyList is null || CountText is null)
        {
            return;
        }

        IEnumerable<GpgKey> filtered = FilterBox.SelectedIndex switch
        {
            1 => _all.Where(k => k.HasSecret),
            2 => _all.Where(k => !k.HasSecret),
            _ => _all,
        };

        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            filtered = filtered.Where(k =>
                k.PrimaryUserId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.Fingerprint.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.KeyId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.UserIds.Any(u => u.Value.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        _visible.Clear();
        foreach (GpgKey key in filtered)
        {
            _visible.Add(new GpgRow(key, Columns));
        }

        CountText.Text = _all.Count == _visible.Count
            ? $"共 {_visible.Count} 个"
            : $"{_visible.Count} / {_all.Count} 个";

        if (KeyEmptyState is null)
        {
            return;
        }

        KeyEmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyEmptyText.Text = !AppServices.Gpg.IsAvailable
            ? "未找到 GnuPG，无法读取密钥。"
            : _all.Count == 0
                ? "本机密钥环中还没有密钥。可以使用「生成密钥对」创建，或导入现有的密钥。"
                : "没有符合筛选条件的密钥。";
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnKeySelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetails((KeyList.SelectedItem as GpgRow)?.Key);

    private void UpdateDetails(GpgKey? key)
    {
        if (key is null)
        {
            DetailTitle.Text = "密钥详细信息";
            DetailPanel.Children.Clear();
            DetailEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = key.PrimaryUserId;
        DetailEmptyState.Visibility = Visibility.Collapsed;
        DetailPresenter.Render(DetailPanel, BuildKeySections(key));
    }

    private static List<DetailSection> BuildKeySections(GpgKey key)
    {
        var basic = new List<DetailItem>
        {
            new("Fingerprint", "指纹", key.GroupedFingerprint, Monospace: true),
            new("KeyId", "密钥 ID", key.KeyId),
            new("Algorithm", "算法", key.AlgorithmText),
            new("Curve", "曲线", key.Curve),
            new("Created", "创建时间", key.Created?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
            new("Expires", "过期时间", key.Expires is null ? "永不过期" : key.Expires.Value.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Status", "状态", key.StatusText),
            new("Capabilities", "具备能力", DescribeCapabilities(key.Capabilities)),
            new("Secret", "私钥", key.HasSecret ? "存在于本机密钥环" : "不存在（仅公钥）"),
        };

        var uids = new List<DetailItem>();
        for (int i = 0; i < key.UserIds.Count; i++)
        {
            GpgUid uid = key.UserIds[i];
            uids.Add(new DetailItem(
                "Uid" + i,
                uid.IsPrimary ? "主用户 ID" : "用户 ID " + (i + 1),
                $"{uid.Value}　[{uid.ValidityText}]"));
        }

        var subkeys = new List<DetailItem>();
        for (int i = 0; i < key.Subkeys.Count; i++)
        {
            GpgSubkey subkey = key.Subkeys[i];
            var parts = new List<string>
            {
                subkey.AlgorithmText,
                "能力：" + DescribeCapabilities(subkey.Capabilities),
                subkey.HasSecret ? "含私钥" : "仅公钥",
                subkey.Expires is null ? "永不过期" : "有效至 " + subkey.Expires.Value.ToString("yyyy-MM-dd"),
            };
            subkeys.Add(new DetailItem("Sub" + i, "子密钥 " + (i + 1), string.Join("；", parts) + "\n" + subkey.Fingerprint, Monospace: true));
        }

        return new List<DetailSection>
        {
            new("基本信息", basic, Expanded: true),
            new("用户 ID", uids, Expanded: true),
            new("子密钥", subkeys),
        };
    }

    private static string DescribeCapabilities(string capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
        {
            return "未知";
        }

        var usable = new SortedSet<string>(StringComparer.Ordinal);
        var unavailable = new SortedSet<string>(StringComparer.Ordinal);

        foreach (char c in capabilities)
        {
            string? text = char.ToLowerInvariant(c) switch
            {
                'e' => "加密",
                's' => "签名",
                'c' => "认证",
                'a' => "身份验证",
                _ => null,
            };

            if (text is not null)
            {
                (char.IsUpper(c) ? usable : unavailable).Add(text);
            }
        }

        unavailable.ExceptWith(usable);

        var parts = new List<string>();
        if (usable.Count > 0)
        {
            parts.Add("可" + string.Join("、", usable));
        }
        if (unavailable.Count > 0)
        {
            parts.Add("密钥环中不可用：" + string.Join("、", unavailable));
        }

        return parts.Count == 0 ? capabilities : string.Join("；", parts);
    }

    // ---------------------------------------------------------------- context menu

    private void OnKeyRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GpgRow row)
        {
            return;
        }

        KeyList.SelectedItem = row;
        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildMenuItem("复制指纹", "\uE8C8", () => CopyToClipboard(row.Key.GroupedFingerprint)));
        flyout.Items.Add(BuildMenuItem("复制用户 ID", "\uE8C8", () => CopyToClipboard(row.Key.PrimaryUserId)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildMenuItem("导出公钥...", "\uE898", () => OnExportPublicClick(this, new RoutedEventArgs())));
        if (row.Key.HasSecret)
        {
            flyout.Items.Add(BuildMenuItem("导出私钥...", "\uE72E", () => OnExportSecretClick(this, new RoutedEventArgs())));
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildMenuItem("删除密钥", "\uE74D", () => OnDeleteClick(this, new RoutedEventArgs())));

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private static MenuFlyoutItem BuildMenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnCopyFingerprintClick(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            _ = DialogService.ShowMessageAsync("复制指纹", "请先在列表中选择一个密钥。");
            return;
        }

        CopyToClipboard(row.Key.GroupedFingerprint);
    }

    // ---------------------------------------------------------------- operation plumbing

    private async Task<GpgOperationReport?> RunAsync(
        string title,
        string operation,
        Func<GpgResult> action,
        string? inputPath = null,
        string? outputPath = null,
        IReadOnlyList<string>? recipients = null)
    {
        if (!AppServices.Gpg.IsAvailable)
        {
            await DialogService.ShowMessageAsync("未找到 GnuPG", "请先安装 Gpg4win、使用内置的 GnuPG，或在设置中指定 gpg.exe 的路径。");
            return null;
        }

        try
        {
            BusyRing.IsActive = true;
            GpgResult result = await Task.Run(action);
            GpgOperationReport report = GpgOutputInterpreter.Describe(result, operation, inputPath, outputPath, recipients);
            await ShowReportAsync(title, report);
            return report;
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(title + "失败", ex);
            return null;
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private static async Task ShowReportAsync(string title, GpgOperationReport report)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = ResultPresenter.Create(report),
                MaxHeight = 480,
                MinWidth = 400,
            },
            PrimaryButtonText = "确定",
            DefaultButton = ContentDialogButton.Primary,
        };

        await DialogService.ShowAsync(dialog);
    }

    // ---------------------------------------------------------------- key management

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private async void OnLocateGpgClick(object sender, RoutedEventArgs e)
    {
        string? path = await FilePickerHelper.PickOpenFileAsync("选择 gpg.exe", ".exe");
        if (path is null)
        {
            return;
        }

        AppServices.Settings.GpgExecutablePath = path;
        AppServices.Settings.Save();
        AppServices.ReinitializeGpg();
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "姓名 (Name-Real)", PlaceholderText = "例如 Kasumi User" };
        var emailBox = new TextBox { Header = "电子邮件 (Name-Email)" };
        var commentBox = new TextBox { Header = "备注 (Name-Comment)" };
        var algorithmBox = new ComboBox
        {
            Header = "算法",
            ItemsSource = new[] { "RSA (2048/3072/4096)", "ECC (NIST/Brainpool)", "Ed25519" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var lengthBox = new NumberBox
        {
            Header = "RSA 密钥长度",
            Minimum = 1024,
            Maximum = 8192,
            Value = 4096,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var curveBox = new ComboBox
        {
            Header = "椭圆曲线 (ECC)",
            ItemsSource = new[] { "nistp256", "nistp384", "nistp521", "brainpoolP256r1", "brainpoolP384r1", "brainpoolP512r1" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
        };
        var expireBox = new TextBox { Header = "有效期", Text = "2y", PlaceholderText = "例如 2y / 365d / 0 表示永不过期" };
        var passphraseBox = new PasswordBox { Header = "私钥密码（留空表示不加密）" };
        var subkeyCheck = new CheckBox { Content = "同时生成加密子密钥", IsChecked = true };

        algorithmBox.SelectionChanged += (_, _) =>
        {
            curveBox.IsEnabled = algorithmBox.SelectedIndex == 1;
            lengthBox.IsEnabled = algorithmBox.SelectedIndex != 1;
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = "密钥生成可能需要数分钟（尤其是 4096 位 RSA）。期间请勿关闭程序。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(emailBox);
        panel.Children.Add(commentBox);
        panel.Children.Add(algorithmBox);
        panel.Children.Add(lengthBox);
        panel.Children.Add(curveBox);
        panel.Children.Add(expireBox);
        panel.Children.Add(passphraseBox);
        panel.Children.Add(subkeyCheck);

        var dialog = new ContentDialog
        {
            Title = "生成 OpenPGP 密钥对",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "生成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await DialogService.ShowMessageAsync("输入有误", "姓名不能为空。");
            return;
        }

        var options = new GpgKeyGenerationOptions
        {
            RealName = nameBox.Text.Trim(),
            Email = emailBox.Text?.Trim() ?? string.Empty,
            Comment = commentBox.Text?.Trim() ?? string.Empty,
            Algorithm = algorithmBox.SelectedIndex switch
            {
                1 => GpgKeyAlgorithm.Ecc,
                2 => GpgKeyAlgorithm.Ed25519,
                _ => GpgKeyAlgorithm.Rsa,
            },
            KeyLength = double.IsNaN(lengthBox.Value) ? 4096 : (int)lengthBox.Value,
            SubkeyLength = double.IsNaN(lengthBox.Value) ? 4096 : (int)lengthBox.Value,
            Curve = curveBox.SelectedItem as string ?? "nistp256",
            ExpireDate = string.IsNullOrWhiteSpace(expireBox.Text) ? "2y" : expireBox.Text.Trim(),
            Passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password,
            IncludeSubkey = subkeyCheck.IsChecked == true,
        };

        GpgOperationReport? report = await RunAsync("生成密钥对", "generate", () => AppServices.Gpg.GenerateKey(options));
        if (report is not null && report.Success)
        {
            await LoadKeysAsync();
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> paths = await FilePickerHelper.PickOpenFilesAsync(
            "导入", ".asc", ".gpg", ".pgp", ".key", ".pub", ".txt");
        if (paths.Count == 0)
        {
            return;
        }

        var reports = new List<(string File, GpgOperationReport Report)>();

        foreach (string path in paths)
        {
            if (!AppServices.Gpg.IsAvailable)
            {
                break;
            }

            try
            {
                BusyRing.IsActive = true;
                string file = path;
                GpgResult result = await Task.Run(() => AppServices.Gpg.ImportKey(file));
                reports.Add((Path.GetFileName(file), GpgOutputInterpreter.Describe(result, "import", inputPath: file)));
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync("导入 " + Path.GetFileName(path) + " 失败", ex);
            }
            finally
            {
                BusyRing.IsActive = false;
            }
        }

        await LoadKeysAsync();

        if (reports.Count == 0)
        {
            return;
        }

        if (reports.Count == 1)
        {
            await ShowReportAsync("导入密钥", reports[0].Report);
            return;
        }

        var combined = new GpgOperationReport("import")
        {
            Title = $"已处理 {reports.Count} 个文件",
            Success = reports.All(r => r.Report.Success),
            Severity = reports.All(r => r.Report.Success)
                ? GpgReportSeverity.Success
                : reports.Any(r => r.Report.Success) ? GpgReportSeverity.Warning : GpgReportSeverity.Error,
        };

        foreach ((string file, GpgOperationReport report) in reports)
        {
            string detail = report.Rows.Count == 0
                ? report.Title
                : report.Title + "：" + string.Join("，", report.Rows.Take(2).Select(r => r.Label + " " + r.Value));
            combined.With(file, detail, report.Severity);
        }

        await ShowReportAsync("导入密钥", combined);
    }

    private async void OnExportPublicClick(object sender, RoutedEventArgs e)
        => await ExportAsync(secret: false);

    private async void OnExportSecretClick(object sender, RoutedEventArgs e)
        => await ExportAsync(secret: true);

    private async Task ExportAsync(bool secret)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            await DialogService.ShowMessageAsync(secret ? "导出私钥" : "导出公钥", "请先在列表中选择一个密钥。");
            return;
        }

        GpgKey key = row.Key;
        if (secret && !key.HasSecret)
        {
            await DialogService.ShowMessageAsync("导出私钥", "所选密钥不包含私钥，无法导出。");
            return;
        }

        string? passphrase = null;
        if (secret)
        {
            passphrase = await DialogService.ShowPasswordAsync("导出私钥", "请输入该私钥的密码（若未设置密码则留空）：");
            if (passphrase is null)
            {
                return;
            }
        }

        string suffix = secret ? "-secret" : "-public";
        string extension = secret ? ".asc" : ".asc";
        string? path = await FilePickerHelper.PickSaveFileAsync(
            key.ShortFingerprint + suffix, "导出",
            (secret ? "ASCII Armor 私钥" : "ASCII Armor 公钥", new[] { ".asc" }),
            ("二进制密钥", new[] { ".gpg" }));

        if (path is null)
        {
            return;
        }

        bool armor = path.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);
        _ = extension;

        try
        {
            BusyRing.IsActive = true;
            string target = path;
            string fingerprint = key.Fingerprint;
            string? secretPassphrase = passphrase is { Length: 0 } ? null : passphrase;
            await Task.Run(() => AppServices.Gpg.ExportKeyToFile(fingerprint, target, secret, armor, secretPassphrase));

            var report = new GpgOperationReport("export")
            {
                Title = secret ? "私钥已导出" : "公钥已导出",
                Success = true,
                Severity = GpgReportSeverity.Success,
            };
            if (secret)
            {
                report.Note("请妥善保管私钥文件，并使用强密码保护。");
            }
            report.With("密钥", key.PrimaryUserId);
            report.With("指纹", key.GroupedFingerprint);
            report.With("输出文件", path);
            await ShowReportAsync(secret ? "导出私钥" : "导出公钥", report);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("导出失败", ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            await DialogService.ShowMessageAsync("删除密钥", "请先在列表中选择一个密钥。");
            return;
        }

        GpgKey key = row.Key;
        bool confirmed = await DialogService.ShowConfirmAsync(
            "删除密钥",
            $"确定要删除以下密钥吗？此操作不可撤销。\n\n{key.PrimaryUserId}\n{key.GroupedFingerprint}" +
            (key.HasSecret ? "\n\n警告：该密钥包含私钥，删除后将无法恢复！" : string.Empty),
            "删除");
        if (!confirmed)
        {
            return;
        }

        await RunAsync("删除密钥", "delete", () => AppServices.Gpg.DeleteKey(key.Fingerprint, key.HasSecret));
        await LoadKeysAsync();
    }

    // ---------------------------------------------------------------- file operations

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        List<GpgKey> recipients = _all.Where(k => k.CanEncrypt).ToList();
        if (recipients.Count == 0)
        {
            await DialogService.ShowMessageAsync("加密文件", "没有可用于加密的公钥。请先生成或导入密钥。");
            return;
        }

        string? inputPath = await FilePickerHelper.PickOpenFileAsync("选择要加密的文件", ".*");
        if (inputPath is null)
        {
            return;
        }

        var recipientList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = recipients,
            DisplayMemberPath = "PrimaryUserId",
            Height = 200,
        };

        var armorCheck = new CheckBox { Content = "输出 ASCII Armor 文本格式 (.asc)", IsChecked = true };
        var signCheck = new CheckBox { Content = "同时签名（需要私钥）" };
        var passphraseBox = new PasswordBox { Header = "签名私钥密码（如需要）" };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(new TextBlock { Text = "选择接收者（可多选）：" });
        panel.Children.Add(recipientList);
        panel.Children.Add(armorCheck);
        panel.Children.Add(signCheck);
        panel.Children.Add(passphraseBox);

        var dialog = new ContentDialog
        {
            Title = "加密文件",
            Content = panel,
            PrimaryButtonText = "下一步",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        List<GpgKey> selected = recipientList.SelectedItems.Cast<GpgKey>().ToList();
        if (selected.Count == 0)
        {
            await DialogService.ShowMessageAsync("加密文件", "请至少选择一个接收者。");
            return;
        }

        string extension = armorCheck.IsChecked == true ? ".asc" : ".gpg";
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(
            Path.GetFileName(inputPath) + extension, "保存加密文件", ("加密文件", new[] { extension }));
        if (outputPath is null)
        {
            return;
        }

        bool armor = armorCheck.IsChecked == true;
        bool sign = signCheck.IsChecked == true;
        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;
        List<string> fingerprints = selected.Select(k => k.Fingerprint).ToList();

        await RunAsync(
            "加密文件",
            "encrypt",
            () => AppServices.Gpg.EncryptFile(inputPath, outputPath, fingerprints, armor, sign, null, passphrase),
            inputPath: inputPath,
            outputPath: outputPath,
            recipients: fingerprints);
    }

    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        string? inputPath = await FilePickerHelper.PickOpenFileAsync("选择要解密的文件", ".asc", ".gpg", ".pgp");
        if (inputPath is null)
        {
            return;
        }

        string? passphrase = await DialogService.ShowPasswordAsync("解密文件", "私钥密码（若未设置密码则留空）：");
        if (passphrase is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(inputPath);
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(suggested, "保存解密文件", ("所有文件", new[] { ".*" }));
        if (outputPath is null)
        {
            return;
        }

        await RunAsync(
            "解密文件",
            "decrypt",
            () => AppServices.Gpg.DecryptFile(inputPath, outputPath, passphrase.Length == 0 ? null : passphrase),
            inputPath: inputPath,
            outputPath: outputPath);
    }

    private async void OnSignClick(object sender, RoutedEventArgs e)
    {
        List<GpgKey> signingKeys = _all.Where(k => k.CanSign && k.HasSecret).ToList();
        if (signingKeys.Count == 0)
        {
            await DialogService.ShowMessageAsync("签名文件", "没有可用的私钥。请先生成或导入包含私钥的密钥。");
            return;
        }

        string? inputPath = await FilePickerHelper.PickOpenFileAsync("选择要签名的文件", ".*");
        if (inputPath is null)
        {
            return;
        }

        var keyBox = new ComboBox
        {
            Header = "签名密钥",
            ItemsSource = signingKeys,
            DisplayMemberPath = "PrimaryUserId",
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var detachedCheck = new CheckBox { Content = "生成独立签名文件 (.sig)", IsChecked = true };
        var clearCheck = new CheckBox { Content = "生成明文签名 (clearsign)" };
        var passphraseBox = new PasswordBox { Header = "私钥密码（若未设置密码则留空）" };

        detachedCheck.Checked += (_, _) => { clearCheck.IsChecked = false; };
        clearCheck.Checked += (_, _) => { detachedCheck.IsChecked = false; };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(keyBox);
        panel.Children.Add(detachedCheck);
        panel.Children.Add(clearCheck);
        panel.Children.Add(passphraseBox);

        var dialog = new ContentDialog
        {
            Title = "签名文件",
            Content = panel,
            PrimaryButtonText = "下一步",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (keyBox.SelectedItem is not GpgKey signingKey)
        {
            return;
        }

        bool detached = detachedCheck.IsChecked == true;
        bool clear = clearCheck.IsChecked == true;
        string extension = detached ? ".sig" : clear ? ".asc" : ".gpg";
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(
            Path.GetFileName(inputPath) + extension, "保存签名", ("签名文件", new[] { extension }));
        if (outputPath is null)
        {
            return;
        }

        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;

        await RunAsync(
            "签名文件",
            "sign",
            () => AppServices.Gpg.SignFile(inputPath, outputPath, detached, armor: true, signingKey.Fingerprint, passphrase, clear),
            inputPath: inputPath,
            outputPath: outputPath);
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        string? signaturePath = await FilePickerHelper.PickOpenFileAsync("选择签名文件", ".sig", ".asc", ".gpg", ".pgp");
        if (signaturePath is null)
        {
            return;
        }

        string? dataPath = await FilePickerHelper.PickOpenFileAsync("选择被签名的原始文件（若非独立签名可取消）", ".*");

        await RunAsync(
            "签名验证结果",
            "verify",
            () => AppServices.Gpg.VerifyFile(signaturePath, dataPath),
            inputPath: signaturePath);
    }
}
