using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

public sealed partial class X509Page : Page
{
    private const string ColumnsKey = "X509.Columns";
    private const string ListPaneKey = "X509.ListPaneWidth";

    private readonly ObservableCollection<X509Row> _visibleItems = new();
    private X509Database? _attached;

    public X509Page()
    {
        InitializeComponent();
        ItemList.ItemsSource = _visibleItems;
        ApplyStoredLayout();
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(58, 200, 80, 70);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Settings.GetLayoutDouble(ListPaneKey, 420), GridUnitType.Pixel);
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(ListPaneKey, ContentGrid.ColumnDefinitions[0].ActualWidth);
    }

    private void OnItemPaneSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(300, ContentGrid.ActualWidth - 260);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(ContentGrid.ColumnDefinitions[0].ActualWidth + e.Delta.Translation.X, 260, maximum),
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachDatabase();
    }

    private bool EnsureDatabase()
    {
        if (AppServices.Database is null)
        {
            _ = DialogService.ShowMessageAsync("未打开数据库", "请先新建或打开一个证书数据库。");
            return false;
        }
        return true;
    }

    private void AttachDatabase()
    {
        if (_attached is not null)
        {
            _attached.Items.CollectionChanged -= OnDatabaseItemsChanged;
        }

        X509Database? database = AppServices.Database;
        _attached = database;

        if (database is not null)
        {
            database.Items.CollectionChanged += OnDatabaseItemsChanged;
            DatabaseTitle.Text = database.Name;
            DatabaseSubtitle.Text = database.FilePath;
        }
        else
        {
            DatabaseTitle.Text = "未打开数据库";
            DatabaseSubtitle.Text = string.IsNullOrWhiteSpace(AppServices.Settings.LastDatabasePath)
                ? "请新建或打开一个证书数据库以管理密钥、证书与证书请求。"
                : "上次使用的数据库：" + AppServices.Settings.LastDatabasePath;
        }

        ApplyItemFilter();
        UpdateDetails(null);
    }

    private void OnDatabaseItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyItemFilter();

    private void ApplyItemFilter()
    {
        string query = ItemFilterBox?.Text?.Trim() ?? string.Empty;
        X509Database? database = AppServices.Database;

        _visibleItems.Clear();
        if (database is null)
        {
            ItemCountText.Text = string.Empty;
            ItemEmptyText.Text = "尚未打开数据库。";
            ItemEmptyState.Visibility = Visibility.Visible;
            return;
        }

        IEnumerable<X509Item> items = database.Items;
        if (query.Length > 0)
        {
            items = items.Where(i =>
                (i.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Subject?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Comment?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.KindText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (X509Item item in items)
        {
            _visibleItems.Add(new X509Row(item, Columns));
        }

        ItemCountText.Text = _visibleItems.Count == database.Items.Count
            ? $"{_visibleItems.Count} 个项目"
            : $"{_visibleItems.Count} / {database.Items.Count} 个项目";

        ItemEmptyState.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemEmptyText.Text = database.Items.Count == 0
            ? "数据库中没有项目。使用上方工具栏新建密钥、证书或证书请求。"
            : "没有符合筛选条件的项目。";
    }

    private void OnItemFilterChanged(object sender, TextChangedEventArgs e) => ApplyItemFilter();

    private void OnCloseDatabaseClick(object sender, RoutedEventArgs e)
    {
        AppServices.SetDatabase(null);
        AttachDatabase();
    }

    private void RefreshItems()
    {
        ApplyItemFilter();
        UpdateDetails((ItemList.SelectedItem as X509Row)?.Item);
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetails((ItemList.SelectedItem as X509Row)?.Item);

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not X509Row row)
        {
            return;
        }

        ItemList.SelectedItem = row;
        var flyout = new MenuFlyout();

        if (row.Item.Kind == X509ItemKind.Csr)
        {
            flyout.Items.Add(BuildMenuItem("用 CA 证书签发...", "\uE70F", () => OnSignCsrClick(this, new RoutedEventArgs())));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(BuildMenuItem("复制名称", "\uE8C8", () => CopyToClipboard(row.Item.Name)));
        flyout.Items.Add(BuildMenuItem("导出...", "\uE898", () => OnExportClick(this, new RoutedEventArgs())));
        flyout.Items.Add(BuildMenuItem("删除", "\uE74D", () => OnDeleteClick(this, new RoutedEventArgs())));

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

    private void UpdateDetails(X509Item? item)
    {
        if (item is null)
        {
            DetailTitle.Text = "详细信息";
            DetailStatusText.Text = string.Empty;
            DetailPanel.Children.Clear();
            DetailEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = item.Name;
        DetailEmptyState.Visibility = Visibility.Collapsed;
        X509Database? database = AppServices.Database;

        try
        {
            List<DetailSection> sections = item.Kind switch
            {
                X509ItemKind.Certificate => BuildCertificateSections(item, database),
                X509ItemKind.Csr => BuildCsrSections(item, database),
                _ => BuildKeySections(item, database),
            };

            DetailStatusText.Text = item.KindText + (string.IsNullOrWhiteSpace(item.Comment) ? string.Empty : " · " + item.Comment);
            DetailPresenter.Render(DetailPanel, sections);
        }
        catch (Exception ex)
        {
            DetailStatusText.Text = "无法读取项目详细信息。";
            DetailPanel.Children.Clear();
            DetailPanel.Children.Add(new TextBlock
            {
                Text = DialogService.DescribeException(ex),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static List<DetailSection> BuildCertificateSections(X509Item item, X509Database? database)
    {
        X509Certificate2? certificate = database?.GetCertificate(item);
        if (certificate is null)
        {
            return new List<DetailSection>
            {
                new("错误", new[] { new DetailItem("Error", "内容", "证书内容无效，无法解析。") }, Expanded: true),
            };
        }

        var sections = DetailPresenter.ForCertificate(CertificateSummaryBuilder.Build(certificate)).ToList();
        sections.Add(new DetailSection("PEM 内容", new[]
        {
            new DetailItem("CertificatePem", "证书 (PEM)", item.CertificatePem ?? string.Empty, Monospace: true),
        }));

        if (item.NotAfter is not null)
        {
            sections.Add(new DetailSection("数据库记录", new[]
            {
                new DetailItem("Created", "创建时间", item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                new DetailItem("Comment", "备注", item.Comment),
                new DetailItem("Id", "内部 ID", item.Id, Monospace: true),
            }));
        }

        return sections;
    }

    private static List<DetailSection> BuildCsrSections(X509Item item, X509Database? database)
    {
        CertificateRequest? request = database?.GetCsrRequest(item);

        var basic = new List<DetailItem>
        {
            new("Name", "名称", item.Name),
            new("Subject", "主题", X500Name.Format(item.Subject ?? string.Empty)),
            new("SubjectCn", "常用名 (CN)", X500Name.GetCommonName(item.Subject ?? string.Empty)),
            new("Hash", "摘要算法", request?.HashAlgorithm.Name ?? string.Empty),
            new("PublicKey", "公钥算法", request?.PublicKey.Oid.FriendlyName ?? request?.PublicKey.Oid.Value ?? string.Empty),
            new("Created", "创建时间", item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new("Comment", "备注", item.Comment),
        };

        var extensions = new List<DetailItem>();
        if (request is not null)
        {
            int index = 0;
            foreach (X509Extension extension in request.CertificateExtensions)
            {
                extensions.Add(new DetailItem(
                    "CsrExt" + index++,
                    extension.Oid?.FriendlyName ?? extension.Oid?.Value ?? "扩展",
                    CertificateDetailsBuilder.DescribeExtensionForDisplay(extension),
                    Monospace: true));
            }
        }

        return new List<DetailSection>
        {
            new("证书请求 (PKCS#10)", basic, Expanded: true),
            new("请求中的扩展", extensions),
            new("PEM 内容", new[] { new DetailItem("CsrPem", "证书请求 (PEM)", item.CsrPem ?? string.Empty, Monospace: true) }),
        };
    }

    private static List<DetailSection> BuildKeySections(X509Item item, X509Database? database)
    {
        string usedBy = string.Empty;
        if (database is not null)
        {
            List<X509Item> used = database.Items.Where(i => i.KeyId == item.Id).ToList();
            if (used.Count > 0)
            {
                usedBy = string.Join("；", used.Select(i => $"{i.Name}（{i.KindText}）"));
            }
        }

        var basic = new List<DetailItem>
        {
            new("Name", "名称", item.Name),
            new("Algorithm", "算法", item.KeyAlgorithm ?? string.Empty),
            new("KeySize", "密钥长度", item.KeySize is null ? string.Empty : item.KeySize + " bit"),
            new("Storage", "存储状态", item.HasPrivateKey ? "已加密存储（PKCS#8，PBES2/AES-256，由数据库密码保护）" : "未存储"),
            new("Created", "创建时间", item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new("UsedBy", "被以下项目使用", usedBy),
            new("Comment", "备注", item.Comment),
        };

        return new List<DetailSection>
        {
            new("私钥", basic, Expanded: true),
            new("公钥 (SubjectPublicKeyInfo)", new[]
            {
                new DetailItem("KeyPem", "公钥 (PEM)", BuildKeyPem(item, database), Monospace: true),
            }),
        };
    }

    private static string BuildKeyPem(X509Item item, X509Database? database)
    {
        if (database is null)
        {
            return item.EncryptedKeyPem ?? string.Empty;
        }

        try
        {
            using AsymmetricAlgorithm key = database.LoadKey(item);
            return "----- 公钥 (SubjectPublicKeyInfo) -----" + Environment.NewLine +
                   key.ExportSubjectPublicKeyInfoPem() + Environment.NewLine +
                   "----- 私钥以加密形式存储在数据库中（使用数据库密码保护）-----";
        }
        catch (Exception ex)
        {
            return "无法读取私钥：" + ex.Message + Environment.NewLine + Environment.NewLine + (item.EncryptedKeyPem ?? string.Empty);
        }
    }

    private async void OnNewDatabaseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (string Path, string Password)? input = await DatabaseDialogs.ShowCreateAsync();
            if (input is null)
            {
                return;
            }

            X509Database database = X509Database.Create(input.Value.Path, input.Value.Password);
            AppServices.SetDatabase(database);
            AppServices.AddRecentDatabase(database.FilePath);
            AttachDatabase();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("创建数据库失败", ex);
        }
    }

    private async void OnOpenDatabaseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (string Path, string Password)? input = await DatabaseDialogs.ShowOpenAsync(AppServices.Settings.RecentDatabases);
            if (input is null)
            {
                return;
            }

            X509Database database = X509Database.Open(input.Value.Path, input.Value.Password);
            if (!database.ValidatePassword(input.Value.Password))
            {
                await DialogService.ShowMessageAsync("密码错误", "数据库密码不正确，无法解密私钥。");
                return;
            }

            AppServices.SetDatabase(database);
            AppServices.AddRecentDatabase(database.FilePath);
            AttachDatabase();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("打开数据库失败", ex);
        }
    }

    private async void OnNewKeyClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        KeyDialogResult? input = await X509Dialogs.ShowKeyAsync("key-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if (input is null)
        {
            return;
        }

        try
        {
            using AsymmetricAlgorithm key = X509Factory.CreateKey(input.Key);
            database.AddKey(input.Name, key, input.Comment);
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("生成密钥失败", ex);
        }
    }

    private async void OnNewCertificateClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(false, false);

    private async void OnNewCaClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(true, false);

    private async void OnNewCsrClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(false, true);

    private async Task CreateCertificateAsync(bool isCa, bool asCsr)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        List<X509Item> keys = database.Items.Where(i => i.Kind == X509ItemKind.PrivateKey).ToList();

        string title = asCsr ? "新建证书请求 (CSR)" : isCa ? "新建 CA 证书" : "新建自签名证书";
        string defaultName = isCa ? "Kasumi Root CA" : "server.example.com";

        CertificateDialogResult? input = await X509Dialogs.ShowCertificateAsync(title, defaultName, isCa, keys, null);
        if (input is null)
        {
            return;
        }

        try
        {
            AsymmetricAlgorithm key;
            X509Item keyItem;

            if (input.ExistingKeyId is not null)
            {
                keyItem = database.Items.First(i => i.Id == input.ExistingKeyId);
                key = database.LoadKey(keyItem);
            }
            else
            {
                key = X509Factory.CreateKey(input.NewKey!);
                keyItem = database.AddKey(input.Name + " - 密钥", key, "由向导生成");
            }

            using (key)
            {
                X509CertificateOptions options = BuildOptions(input);
                if (asCsr)
                {
                    CertificateRequest request = X509Factory.CreateRequest(key, options);
                    database.AddCsr(input.Name, request, keyItem.Id, input.Comment);
                }
                else
                {
                    X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);
                    database.AddCertificate(input.Name, certificate, keyItem.Id, input.Comment);
                }
            }

            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("生成失败", ex);
        }
    }

    private static X509CertificateOptions BuildOptions(CertificateDialogResult input) => new()
    {
        Subject = input.Subject.BuildDistinguishedName(),
        ValidDays = input.ValidDays,
        HashAlgorithm = input.Hash,
        IsCa = input.IsCa,
        HasPathLengthConstraint = input.HasPathLengthConstraint,
        PathLengthConstraint = input.PathLengthConstraint,
        KeyUsage = input.KeyUsage,
        IncludeKeyUsage = true,
        ExtendedKeyUsageOids = input.ExtendedKeyUsages,
        SubjectAlternativeNames = input.SubjectAlternativeNames,
        CrlDistributionPoints = input.CrlDistributionPoints,
        OcspUrls = input.OcspUrls,
        CertificatePolicies = input.CertificatePolicies,
    };

    private async void OnSignCsrClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        List<X509Item> csrs = database.Items.Where(i => i.Kind == X509ItemKind.Csr).ToList();
        List<X509Item> cas = database.Items.Where(i => i.Kind == X509ItemKind.Certificate && i.IsCa).ToList();

        if (csrs.Count == 0)
        {
            await DialogService.ShowMessageAsync("用 CA 签发", "数据库中没有证书请求 (CSR)。请先创建一个证书请求。");
            return;
        }
        if (cas.Count == 0)
        {
            await DialogService.ShowMessageAsync("用 CA 签发", "数据库中没有 CA 证书。请先创建一个 CA 证书。");
            return;
        }

        var csrBox = new ComboBox { Header = "证书请求 (CSR)", ItemsSource = csrs, DisplayMemberPath = "Name", MinWidth = 380 };
        csrBox.SelectedItem = ItemList.SelectedItem is X509Row { Item.Kind: X509ItemKind.Csr } selected ? selected.Item : csrs[0];

        var caBox = new ComboBox { Header = "颁发者 CA 证书", ItemsSource = cas, DisplayMemberPath = "Name", MinWidth = 380 };
        caBox.SelectedItem = cas[0];

        var daysBox = new NumberBox
        {
            Header = "有效期（天）",
            Minimum = 1,
            Maximum = 36500,
            Value = 365,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 380,
        };

        var caHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
        };

        void UpdateCaHint()
        {
            if (caBox.SelectedItem is not X509Item ca)
            {
                return;
            }

            int remainingDays = ca.NotAfter is null
                ? 365
                : Math.Max(1, (int)(ca.NotAfter.Value - DateTime.Now).TotalDays);
            daysBox.Value = Math.Min(remainingDays, 365);
            caHint.Text = $"该 CA 证书有效期至 {ca.NotAfter:yyyy-MM-dd}（剩余 {remainingDays} 天）。签发的证书有效期不会超过该日期。";
        }

        caBox.SelectionChanged += (_, _) => UpdateCaHint();
        UpdateCaHint();
        var hashBox = new ComboBox { Header = "签名哈希算法", ItemsSource = X509Factory.HashAlgorithms, SelectedIndex = 0, MinWidth = 380 };
        var caCheck = new CheckBox { Content = "将签发的证书标记为 CA 证书" };
        var pathLenCheck = new CheckBox { Content = "限制路径长度", IsEnabled = false };
        var pathLenBox = new NumberBox { Minimum = 0, Maximum = 32, Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, IsEnabled = false };
        caCheck.Checked += (_, _) => { pathLenCheck.IsEnabled = true; };
        caCheck.Unchecked += (_, _) => { pathLenCheck.IsEnabled = false; pathLenCheck.IsChecked = false; pathLenBox.IsEnabled = false; };
        pathLenCheck.Checked += (_, _) => pathLenBox.IsEnabled = true;
        pathLenCheck.Unchecked += (_, _) => pathLenBox.IsEnabled = false;

        var pathLenPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pathLenPanel.Children.Add(pathLenCheck);
        pathLenPanel.Children.Add(pathLenBox);

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "将使用所选 CA 证书的私钥对 CSR 进行签名。CSR 中未在此处重写的扩展（例如使用者可选名称）会被保留。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(csrBox);
        panel.Children.Add(caBox);
        panel.Children.Add(caHint);
        panel.Children.Add(daysBox);
        panel.Children.Add(hashBox);
        panel.Children.Add(caCheck);
        panel.Children.Add(pathLenPanel);

        var dialog = new ContentDialog
        {
            Title = "用 CA 证书签发",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "签发",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (csrBox.SelectedItem is not X509Item csrItem || caBox.SelectedItem is not X509Item caItem)
        {
            return;
        }

        try
        {
            CertificateRequest? request = database.GetCsrRequest(csrItem);
            if (request is null)
            {
                await DialogService.ShowMessageAsync("签发失败", "CSR 内容无效。");
                return;
            }

            X509Certificate2? caCertificate = database.GetCertificateWithKey(caItem);
            if (caCertificate is null || !caCertificate.HasPrivateKey)
            {
                await DialogService.ShowMessageAsync("签发失败", "CA 证书不包含可用的私钥。");
                return;
            }

            bool isCa = caCheck.IsChecked == true;
            var options = new X509CertificateOptions
            {
                ValidDays = double.IsNaN(daysBox.Value) ? 365 : (int)daysBox.Value,
                HashAlgorithm = hashBox.SelectedItem as string ?? "SHA256",
                IsCa = isCa,
                HasPathLengthConstraint = pathLenCheck.IsChecked == true,
                PathLengthConstraint = double.IsNaN(pathLenBox.Value) ? 0 : (int)pathLenBox.Value,
                IncludeKeyUsage = isCa,
                KeyUsage = X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                Subject = csrItem.Subject ?? string.Empty,
            };

            X509Certificate2 signed = X509Factory.SignCsr(
                X509Factory.LoadCsr(csrItem.CsrPem!, options.HashAlgorithm),
                caCertificate,
                options);

            string name = X500Name.GetCommonName(signed.Subject);
            database.AddCertificate(name, signed, csrItem.KeyId, "由 " + caItem.Name + " 签发");
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("签发失败", ex);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        try
        {
            IReadOnlyList<string> paths = await FilePickerHelper.PickOpenFilesAsync(
                "导入",
                ".pem", ".crt", ".cer", ".der", ".key", ".csr", ".pfx", ".p12", ".txt", ".asc");
            if (paths.Count == 0)
            {
                return;
            }

            int imported = 0;
            var errors = new List<string>();

            foreach (string path in paths)
            {
                try
                {
                    imported += await ImportFileAsync(path);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }

            RefreshItems();

            string message = $"已导入 {imported} 个项目。";
            if (errors.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + "以下文件导入失败：" + Environment.NewLine + string.Join(Environment.NewLine, errors);
            }
            await DialogService.ShowMessageAsync("导入完成", message);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("导入失败", ex);
        }
    }

    private async Task<int> ImportFileAsync(string path)
    {
        X509Database database = AppServices.Database!;
        byte[] data = await File.ReadAllBytesAsync(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string? text = TryGetText(data);

        if (text is not null && text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            int count = 0;

            if (text.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                string? password = text.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)
                    ? await DialogService.ShowPasswordAsync("私钥密码", "私钥已加密，请输入密码：")
                    : null;
                database.ImportKey(name, text, password, "从 " + Path.GetFileName(path) + " 导入");
                count++;
            }

            if (text.Contains("CERTIFICATE REQUEST", StringComparison.Ordinal))
            {
                database.ImportCsr(name, text, "从 " + Path.GetFileName(path) + " 导入");
                count++;
            }

            if (text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
            {
                string? keyPem = text.Contains("PRIVATE KEY", StringComparison.Ordinal) ? text : null;
                database.ImportCertificate(name, text, keyPem, "从 " + Path.GetFileName(path) + " 导入");
                count++;
            }

            return count;
        }

        bool looksLikePkcs12 = data.Length > 2 && data[0] == 0x30;
        if (looksLikePkcs12)
        {
            try
            {
                string? password = null;
                IReadOnlyList<X509Certificate2> certs;
                try
                {
                    certs = CertificateFileIO.Load(data, null);
                }
                catch (CryptographicException)
                {
                    password = await DialogService.ShowPasswordAsync("PKCS#12 密码", "该 PKCS#12 文件已加密，请输入密码：");
                    if (password is null)
                    {
                        return 0;
                    }
                    certs = CertificateFileIO.Load(data, password);
                }

                if (certs.Count > 0 && certs[0].HasPrivateKey)
                {
                    CertificateFileIO.Export(certs[0], path + ".tmp", CertificateFileFormat.Pkcs12, password, true);
                    database.ImportCertificate(name, certs[0].ExportCertificatePem(), null, "从 " + Path.GetFileName(path) + " 导入");
                    using AsymmetricAlgorithm? privateKey = CertificateKeyIO.GetPrivateKey(certs[0]);
                    if (privateKey is not null)
                    {
                        database.AddKey(name + " - 密钥", privateKey, "从 " + Path.GetFileName(path) + " 导入");
                    }
                    try { File.Delete(path + ".tmp"); } catch (Exception) { }
                }
                else
                {
                    foreach (X509Certificate2 certificate in certs)
                    {
                        database.ImportCertificate(name, certificate.ExportCertificatePem(), null, "从 " + Path.GetFileName(path) + " 导入");
                    }
                }

                return Math.Max(1, certs.Count);
            }
            catch (CryptographicException)
            {
                X509Certificate2 certificate = new(data);
                database.ImportCertificate(name, certificate.ExportCertificatePem(), null, "从 " + Path.GetFileName(path) + " 导入");
                return 1;
            }
        }

        throw new InvalidOperationException("无法识别的文件格式。");
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not X509Item item)
        {
            await DialogService.ShowMessageAsync("导出", "请先在列表中选择一个项目。");
            return;
        }

        X509Database database = AppServices.Database!;
        bool hasPrivateKey = item.HasPrivateKey || database.FindKey(item) is not null;

        ExportDialogResult? options = await X509Dialogs.ShowExportAsync(item, hasPrivateKey);
        if (options is null)
        {
            return;
        }

        try
        {
            string baseName = SanitizeFileName(item.Name);

            if (item.Kind == X509ItemKind.Certificate)
            {
                X509Certificate2? certificate = options.IncludePrivateKey
                    ? database.GetCertificateWithKey(item)
                    : database.GetCertificate(item);
                if (certificate is null)
                {
                    await DialogService.ShowMessageAsync("导出失败", "证书内容无效。");
                    return;
                }

                (string name, string[] extensions)[] types = options.Format switch
                {
                    0 => new[] { ("PEM 证书", new[] { ".crt", ".pem" }) },
                    1 => new[] { ("DER 证书", new[] { ".cer" }) },
                    _ => new[] { ("PKCS#12", new[] { ".pfx", ".p12" }) },
                };

                string? path = await FilePickerHelper.PickSaveFileAsync(baseName, "导出", types);
                if (path is null)
                {
                    return;
                }

                CertificateFileFormat format = options.Format switch
                {
                    0 => CertificateFileFormat.Pem,
                    1 => CertificateFileFormat.Der,
                    _ => CertificateFileFormat.Pkcs12,
                };

                CertificateFileIO.Export(certificate, path, format, options.Password, options.IncludePrivateKey);
                await DialogService.ShowMessageAsync("导出成功", "已导出到：" + path);
                return;
            }

            if (item.Kind == X509ItemKind.Csr)
            {
                string? path = await FilePickerHelper.PickSaveFileAsync(baseName, "导出", ("PEM 证书请求", new[] { ".csr", ".pem" }));
                if (path is null)
                {
                    return;
                }
                await File.WriteAllTextAsync(path, item.CsrPem ?? string.Empty);
                await DialogService.ShowMessageAsync("导出成功", "已导出到：" + path);
                return;
            }

            string? keyPath = await FilePickerHelper.PickSaveFileAsync(baseName, "导出", ("PEM 私钥", new[] { ".key", ".pem" }));
            if (keyPath is null)
            {
                return;
            }
            await File.WriteAllTextAsync(keyPath, database.ExportPrivateKeyPem(item, options.Password));
            await DialogService.ShowMessageAsync("导出成功", "已导出到：" + keyPath);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("导出失败", ex);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not X509Item item)
        {
            await DialogService.ShowMessageAsync("删除", "请先在列表中选择一个项目。");
            return;
        }

        X509Database database = AppServices.Database!;
        string extra = item.Kind == X509ItemKind.PrivateKey
            ? "\n\n注意：使用该私钥的证书将失去关联的私钥。"
            : string.Empty;

        bool confirmed = await DialogService.ShowConfirmAsync(
            "删除项目",
            $"确定要删除 \"{item.Name}\"（{item.KindText}）吗？此操作不可撤销。{extra}",
            "删除");
        if (!confirmed)
        {
            return;
        }

        try
        {
            database.Remove(item);
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("删除失败", ex);
        }
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        string? oldPassword = await DialogService.ShowPasswordAsync("修改数据库密码", "当前密码：");
        if (oldPassword is null)
        {
            return;
        }
        if (!database.ValidatePassword(oldPassword))
        {
            await DialogService.ShowMessageAsync("密码错误", "当前密码不正确。");
            return;
        }

        string? newPassword = await DialogService.ShowPasswordAsync("修改数据库密码", "新密码：", confirmRequired: true);
        if (newPassword is null)
        {
            return;
        }
        if (newPassword.Length == 0)
        {
            await DialogService.ShowMessageAsync("密码不能为空", "新密码不能为空。");
            return;
        }

        try
        {
            database.ChangePassword(oldPassword, newPassword);
            AppServices.Settings.Save();
            await DialogService.ShowMessageAsync("完成", "数据库密码已更新，所有私钥已使用新密码重新加密。");
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("修改密码失败", ex);
        }
    }

    private static string? TryGetText(byte[] data)
    {
        try
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            return text.Contains('\uFFFD') ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "item";
        }
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}
