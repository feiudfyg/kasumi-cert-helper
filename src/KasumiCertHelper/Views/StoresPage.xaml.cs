using System.Collections.ObjectModel;
using System.Diagnostics;
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

public sealed partial class StoresPage : Page
{
    private const string ColumnsKey = "Stores.Columns";
    private const string StorePaneKey = "Stores.StorePaneWidth";
    private const string DetailsPaneKey = "Stores.DetailsPaneHeight";
    private const string DetailsVisibleKey = "Stores.DetailsVisible";

    private readonly CertificateStoreService _stores = AppServices.StoreService;
    private readonly ObservableCollection<CertRow> _visibleCertificates = new();
    private readonly ObservableCollection<StoreSummary> _userStores = new();
    private readonly ObservableCollection<StoreSummary> _machineStores = new();

    private List<CertificateItem> _allCertificates = new();
    private List<StoreSummary> _allUserStores = new();
    private List<StoreSummary> _allMachineStores = new();
    private StoreLocation _location = StoreLocation.CurrentUser;
    private string _storeName = "My";
    private CertificateSummary? _currentSummary;
    private bool _suppressSelection;
    private bool _initialized;
    private bool _detailsVisible = true;

    public StoresPage()
    {
        InitializeComponent();

        CertList.ItemsSource = _visibleCertificates;
        UserStoreList.ItemsSource = _userStores;
        MachineStoreList.ItemsSource = _machineStores;
        ApplyStoredLayout();
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(56, 250, 160, 110, 64);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));

        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Settings.GetLayoutDouble(StorePaneKey, 230), GridUnitType.Pixel);
        MainGrid.RowDefinitions[5].Height = new GridLength(
            Settings.GetLayoutDouble(DetailsPaneKey, 260), GridUnitType.Pixel);

        _detailsVisible = Settings.GetLayoutBool(DetailsVisibleKey, true);
        ApplyDetailsVisibility();
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(StorePaneKey, ContentGrid.ColumnDefinitions[0].ActualWidth);
        if (_detailsVisible)
        {
            Settings.SetLayoutDouble(DetailsPaneKey, MainGrid.RowDefinitions[5].ActualHeight);
        }
        Settings.SetLayoutBool(DetailsVisibleKey, _detailsVisible);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        ShowDetails(null);
        await ReloadAsync(selectStoreName: _storeName);
    }

    // ---------------------------------------------------------------- layout

    private void OnStoreSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(220, ContentGrid.ActualWidth - 320);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(ContentGrid.ColumnDefinitions[0].ActualWidth + e.Delta.Translation.X, 160, maximum),
            GridUnitType.Pixel);
        PersistLayout();
    }

    private void OnDetailsSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(200, MainGrid.ActualHeight - 180);
        MainGrid.RowDefinitions[5].Height = new GridLength(
            Math.Clamp(MainGrid.RowDefinitions[5].ActualHeight - e.Delta.Translation.Y, 120, maximum),
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

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Columns.FitTo(e.NewSize.Width - 20);
    }

    private void OnToggleDetailsClick(object sender, RoutedEventArgs e)
    {
        _detailsVisible = !_detailsVisible;
        ApplyDetailsVisibility();
        PersistLayout();
    }

    private void ApplyDetailsVisibility()
    {
        MainGrid.RowDefinitions[5].Height = _detailsVisible
            ? new GridLength(Settings.GetLayoutDouble(DetailsPaneKey, 260), GridUnitType.Pixel)
            : new GridLength(0);

        DetailsPane.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;

        DetailsToggleIcon.Glyph = _detailsVisible ? "\uE70D" : "\uE70E";
        DetailsToggleText.Text = _detailsVisible ? "收起详情" : "显示详情";
        ToolTipService.SetToolTip(DetailsToggleButton, _detailsVisible ? "收起详细信息面板" : "显示详细信息面板");
    }

    // ---------------------------------------------------------------- stores

    private async Task ReloadAsync(string selectStoreName)
    {
        BusyRing.IsActive = true;
        try
        {
            (List<StoreSummary> user, List<StoreSummary> machine) = await Task.Run(() =>
            {
                List<StoreSummary> u = _stores.GetStoreSummaries(StoreLocation.CurrentUser).ToList();
                List<StoreSummary> m = _stores.GetStoreSummaries(StoreLocation.LocalMachine).ToList();
                return (u, m);
            });

            _allUserStores = user;
            _allMachineStores = machine;
            ApplyStoreFilter();
            SelectStore(_location, selectStoreName);
        }
        catch (Exception ex)
        {
            ShowError("读取证书存储区失败", ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private void ApplyStoreFilter()
    {
        string query = StoreSearchBox?.Text?.Trim() ?? string.Empty;

        _userStores.Clear();
        foreach (StoreSummary summary in _allUserStores)
        {
            if (Matches(summary, query)) _userStores.Add(summary);
        }

        _machineStores.Clear();
        foreach (StoreSummary summary in _allMachineStores)
        {
            if (Matches(summary, query)) _machineStores.Add(summary);
        }

        if (StoreCountText is not null)
        {
            StoreCountText.Text = $"{_userStores.Count + _machineStores.Count} 个";
        }

        UserStoreExpander.Visibility = _userStores.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MachineStoreExpander.Visibility = _machineStores.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        static bool Matches(StoreSummary summary, string query)
            => query.Length == 0
               || summary.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || summary.Store.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnStoreSearchChanged(object sender, TextChangedEventArgs e) => ApplyStoreFilter();

    private void SelectStore(StoreLocation location, string storeName)
    {
        _suppressSelection = true;
        try
        {
            if (location == StoreLocation.CurrentUser)
            {
                MachineStoreList.SelectedItem = null;
                UserStoreList.SelectedItem = _userStores.FirstOrDefault(s => s.Store.Name == storeName) ?? _userStores.FirstOrDefault();
                if (UserStoreList.SelectedItem is StoreSummary picked)
                {
                    _location = picked.Store.Location;
                    _storeName = picked.Store.Name;
                }
            }
            else
            {
                UserStoreList.SelectedItem = null;
                MachineStoreList.SelectedItem = _machineStores.FirstOrDefault(s => s.Store.Name == storeName) ?? _machineStores.FirstOrDefault();
                if (MachineStoreList.SelectedItem is StoreSummary picked)
                {
                    _location = picked.Store.Location;
                    _storeName = picked.Store.Name;
                }
            }

            if (UserStoreList.SelectedItem is not null) UserStoreExpander.IsExpanded = true;
            if (MachineStoreList.SelectedItem is not null) MachineStoreExpander.IsExpanded = true;
        }
        finally
        {
            _suppressSelection = false;
        }

        UpdateStoreTitle();
        RefreshCertificates();
    }

    private void OnStoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || sender is not ListView list || list.SelectedItem is not StoreSummary summary)
        {
            return;
        }

        _suppressSelection = true;
        try
        {
            if (ReferenceEquals(list, UserStoreList)) MachineStoreList.SelectedItem = null;
            else UserStoreList.SelectedItem = null;
            if (ReferenceEquals(list, UserStoreList)) UserStoreExpander.IsExpanded = true;
            else MachineStoreExpander.IsExpanded = true;
        }
        finally
        {
            _suppressSelection = false;
        }

        _location = summary.Store.Location;
        _storeName = summary.Store.Name;
        UpdateStoreTitle();
        RefreshCertificates();
    }

    private void UpdateStoreTitle()
    {
        string locationText = _location == StoreLocation.CurrentUser ? "当前用户" : "本地计算机";
        StoreTitleText.Text = $"{locationText} \\ {CertificateStoreService.GetFriendlyStoreName(_storeName)}";
        ToolTipService.SetToolTip(StoreTitleText, $"{locationText}\\{_storeName}");
    }

    // ---------------------------------------------------------------- certificates

    private void RefreshCertificates()
    {
        IReadOnlyList<CertificateItem> items = _stores.GetCertificates(_location, _storeName, out string? error);
        _allCertificates = items.ToList();
        ApplyCertificateFilter();

        if (error is not null)
        {
            ShowErrorMessage("读取存储区内容失败：" + error);
        }
        else
        {
            ClearStatus();
        }
    }

    private void ApplyCertificateFilter()
    {
        string query = SearchBox?.Text?.Trim() ?? string.Empty;

        IEnumerable<CertificateItem> filtered = _allCertificates;
        if (query.Length > 0)
        {
            filtered = _allCertificates.Where(c =>
                Contains(c.Subject, query) ||
                Contains(c.Issuer, query) ||
                Contains(c.Thumbprint, query) ||
                Contains(c.FriendlyName, query) ||
                Contains(c.CommonName, query));
        }

        _visibleCertificates.Clear();
        foreach (CertificateItem item in filtered)
        {
            _visibleCertificates.Add(new CertRow(item, Columns));
        }

        CertificateCountText.Text = _allCertificates.Count == _visibleCertificates.Count
            ? $"共 {_visibleCertificates.Count} 个证书"
            : $"{_visibleCertificates.Count} / {_allCertificates.Count} 个证书";

        CertEmptyState.Visibility = _visibleCertificates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CertEmptyText.Text = _allCertificates.Count == 0
            ? "此存储区中没有证书。"
            : "没有符合筛选条件的证书。";

        static bool Contains(string? value, string query)
            => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyCertificateFilter();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync(_storeName);

    private void OnCertificateSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowDetails((CertList.SelectedItem as CertRow)?.Item);

    private void ShowDetails(CertificateItem? item)
    {
        if (item is null)
        {
            _currentSummary = null;
            DetailsPanel.Children.Clear();
            DetailsHeader.Text = "证书详细信息";
            DetailsStatusText.Text = string.Empty;
            DetailsEmptyState.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            CertificateSummary summary = CertificateSummaryBuilder.Build(item.Certificate);
            _currentSummary = summary;
            DetailsHeader.Text = summary.DisplayName;
            DetailsStatusText.Text = string.Equals(summary.SubjectCommonName, summary.DisplayName, StringComparison.Ordinal)
                ? summary.ValidityText
                : summary.SubjectCommonName + " · " + summary.ValidityText;
            DetailPresenter.Render(DetailsPanel, DetailPresenter.ForCertificate(summary));
            DetailsEmptyState.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _currentSummary = null;
            DetailsPanel.Children.Clear();
            DetailsHeader.Text = item.DisplayName;
            DetailsStatusText.Text = "无法解析该证书。";
            DetailsEmptyState.Visibility = Visibility.Collapsed;
            DetailsPanel.Children.Add(new TextBlock
            {
                Text = DialogService.DescribeException(ex),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    // ---------------------------------------------------------------- context menu

    private void OnCertificateRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not CertRow row)
        {
            return;
        }

        CertList.SelectedItem = row;

        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildContextMenuItem("复制指纹", "\uE8C8", () => CopyToClipboard(row.Item.Thumbprint)));
        flyout.Items.Add(BuildContextMenuItem("复制详细信息", "\uE8C8", CopyDetails));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildContextMenuItem("导出证书...", "\uE898", () => _ = ExportAsync(row.Item)));
        flyout.Items.Add(BuildContextMenuItem("在 certmgr.msc 中打开", "\uE8A5", OpenCertMgr));
        flyout.Items.Add(BuildContextMenuItem("删除证书", "\uE74D", () => _ = DeleteAsync(row.Item)));

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private static MenuFlyoutItem BuildContextMenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void CopyDetails()
    {
        if (_currentSummary is null)
        {
            return;
        }

        CopyToClipboard(_currentSummary.RawTextReport);
    }

    // ---------------------------------------------------------------- actions

    private async void OnImportClick(object sender, RoutedEventArgs e) => await ImportAsync();

    private async Task ImportAsync()
    {
        try
        {
            string? path = await FilePickerHelper.PickOpenFileAsync("导入", CertificateFileIOExtensions.All);
            if (path is null)
            {
                return;
            }

            byte[] data = await File.ReadAllBytesAsync(path);
            string? password = null;
            try
            {
                CertificateFileIO.Load(data, null);
            }
            catch (CryptographicException)
            {
                password = await DialogService.ShowPasswordAsync("证书密码", "该文件已加密，请输入密码：");
                if (password is null)
                {
                    return;
                }
            }

            string locationText = _location == StoreLocation.CurrentUser ? "当前用户" : "本地计算机";
            bool confirmed = await DialogService.ShowConfirmAsync(
                "导入证书",
                $"确定要将 {Path.GetFileName(path)} 导入到 \"{locationText}\\{CertificateStoreService.GetFriendlyStoreName(_storeName)}\" 吗？",
                "导入");
            if (!confirmed)
            {
                return;
            }

            _stores.ImportCertificateToStore(data, password, _location, _storeName);
            await ReloadAsync(_storeName);
            await DialogService.ShowMessageAsync("导入成功", "证书已成功导入到存储区。");
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("导入失败", ex);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
        => await ExportAsync((CertList.SelectedItem as CertRow)?.Item);

    private async Task ExportAsync(CertificateItem? item)
    {
        if (item is null)
        {
            await DialogService.ShowMessageAsync("导出证书", "请先在列表中选择一个证书。");
            return;
        }

        try
        {
            var formatBox = new ComboBox
            {
                Header = "导出格式",
                ItemsSource = new[]
                {
                    "DER 编码证书 (*.cer)",
                    "Base64 (PEM) 证书 (*.crt)",
                    "PKCS#12 证书包 (*.pfx / *.p12)",
                },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var includeKey = new CheckBox
            {
                Content = "包含私钥（仅 PKCS#12 且证书含私钥时有效）",
                IsEnabled = false,
            };
            var passwordBox = new PasswordBox { Header = "PKCS#12 密码", IsEnabled = false };

            formatBox.SelectionChanged += (_, _) =>
            {
                bool isPfx = formatBox.SelectedIndex == 2;
                includeKey.IsEnabled = isPfx && item.HasPrivateKey;
                if (!isPfx)
                {
                    includeKey.IsChecked = false;
                }
                passwordBox.IsEnabled = isPfx;
            };

            var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(formatBox);
            panel.Children.Add(includeKey);
            panel.Children.Add(passwordBox);

            var dialog = new ContentDialog
            {
                Title = "导出选项",
                Content = panel,
                PrimaryButtonText = "导出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
            {
                return;
            }

            int format = formatBox.SelectedIndex;
            bool withPrivateKey = includeKey.IsChecked == true;
            string? password = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password;

            (string name, string[] extensions)[] types = format switch
            {
                0 => new[] { ("DER 证书", new[] { ".cer" }) },
                1 => new[] { ("PEM 证书", new[] { ".crt", ".pem" }) },
                _ => new[] { ("PKCS#12", new[] { ".pfx", ".p12" }) },
            };

            string? savePath = await FilePickerHelper.PickSaveFileAsync(SanitizeFileName(item.DisplayName), "导出", types);
            if (savePath is null)
            {
                return;
            }

            CertificateFileFormat fileFormat = format switch
            {
                0 => CertificateFileFormat.Der,
                1 => CertificateFileFormat.Pem,
                _ => CertificateFileFormat.Pkcs12,
            };

            CertificateFileIO.Export(item.Certificate, savePath, fileFormat, password, withPrivateKey);
            await DialogService.ShowMessageAsync("导出成功", "已导出到：" + savePath);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("导出失败", ex);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
        => await DeleteAsync((CertList.SelectedItem as CertRow)?.Item);

    private async Task DeleteAsync(CertificateItem? item)
    {
        if (item is null)
        {
            await DialogService.ShowMessageAsync("删除证书", "请先在列表中选择一个证书。");
            return;
        }

        bool confirmed = await DialogService.ShowConfirmAsync(
            "删除证书",
            $"确定要从存储区中删除以下证书吗？此操作不可撤销。\n\n{item.DisplayName}\n{item.Subject}\n指纹: {item.Thumbprint}",
            "删除");
        if (!confirmed)
        {
            return;
        }

        try
        {
            _stores.RemoveCertificate(_location, _storeName, item.Certificate);
            await ReloadAsync(_storeName);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("删除失败", ex);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (CertList.SelectedItem is not CertRow row)
        {
            _ = DialogService.ShowMessageAsync("复制指纹", "请先在列表中选择一个证书。");
            return;
        }

        CopyToClipboard(row.Item.Thumbprint);
    }

    private void OnCopyDetailsClick(object sender, RoutedEventArgs e) => CopyDetails();

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnOpenCertMgrClick(object sender, RoutedEventArgs e) => OpenCertMgr();

    private void OpenCertMgr()
    {
        try
        {
            Process.Start(new ProcessStartInfo("certmgr.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowErrorAsync("无法打开 certmgr.msc", ex);
        }
    }

    private async void OnElevateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync("无法以管理员身份重启", ex);
        }
    }

    private void ShowError(string title, Exception exception)
    {
        AppServices.Log(title + ": " + exception);
        ShowErrorMessage(title + "：" + DialogService.DescribeException(exception));
    }

    private void ShowErrorMessage(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Title = "错误";
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void ClearStatus() => StatusBar.IsOpen = false;

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "certificate";
        }
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}

public static class CertificateFileIOExtensions
{
    public static string[] All => new[] { ".cer", ".crt", ".der", ".pem", ".pfx", ".p12", ".p7b", ".sst" };
}
