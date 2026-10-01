using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace KasumiCertHelper.Views;

internal sealed class KeyDialogResult
{
    public string Name { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public X509KeyOptions Key { get; set; } = new();
}

internal sealed class CertificateDialogResult
{
    public string Name { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public X509SubjectOptions Subject { get; set; } = new();

    public int ValidDays { get; set; } = 365;

    public string Hash { get; set; } = "SHA256";

    public bool IsCa { get; set; }

    public bool HasPathLengthConstraint { get; set; }

    public int PathLengthConstraint { get; set; }

    public X509KeyUsageFlags KeyUsage { get; set; }

    public List<string> ExtendedKeyUsages { get; set; } = new();

    public List<SanEntry> SubjectAlternativeNames { get; set; } = new();

    public List<string> CrlDistributionPoints { get; set; } = new();

    public List<string> OcspUrls { get; set; } = new();

    public List<string> CertificatePolicies { get; set; } = new();

    public string? ExistingKeyId { get; set; }

    public X509KeyOptions? NewKey { get; set; }
}

internal sealed class ExportDialogResult
{
    public int Format { get; set; }

    public string? Password { get; set; }

    public bool IncludePrivateKey { get; set; }
}

internal static class X509Dialogs
{
    public static async Task<KeyDialogResult?> ShowKeyAsync(string defaultName, X509KeyOptions? initial = null)
    {
        initial ??= new X509KeyOptions();
        var nameBox = new TextBox { Header = "名称", Text = defaultName, MinWidth = 380 };
        AutomationProperties.SetAutomationId(nameBox, "KeyName");
        var algorithmBox = new ComboBox
        {
            Header = "算法",
            ItemsSource = new[] { "RSA", "ECDSA" },
            SelectedIndex = initial.Algorithm == X509KeyAlgorithm.Ecdsa ? 1 : 0,
            MinWidth = 380,
        };
        var sizeBox = new NumberBox
        {
            Header = "密钥长度 (RSA)",
            Minimum = 1024,
            Maximum = 16384,
            Value = initial.KeySize <= 0 ? 4096 : initial.KeySize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 380,
        };
        var curveBox = new ComboBox
        {
            Header = "椭圆曲线 (ECDSA)",
            ItemsSource = X509Factory.EcdsaCurves,
            SelectedItem = initial.Curve,
            MinWidth = 380,
        };
        var commentBox = new TextBox { Header = "备注", MinWidth = 380 };

        void Update()
        {
            bool ec = algorithmBox.SelectedIndex == 1;
            sizeBox.IsEnabled = !ec;
            curveBox.IsEnabled = ec;
        }
        algorithmBox.SelectionChanged += (_, _) => Update();
        Update();

        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(nameBox);
        panel.Children.Add(algorithmBox);
        panel.Children.Add(sizeBox);
        panel.Children.Add(curveBox);
        panel.Children.Add(commentBox);

        var dialog = new ContentDialog
        {
            Title = "新建密钥",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "生成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        var key = new X509KeyOptions
        {
            Algorithm = algorithmBox.SelectedIndex == 1 ? X509KeyAlgorithm.Ecdsa : X509KeyAlgorithm.Rsa,
            KeySize = double.IsNaN(sizeBox.Value) ? 4096 : (int)sizeBox.Value,
            Curve = curveBox.SelectedItem as string ?? "nistP256",
        };

        return new KeyDialogResult
        {
            Name = string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim(),
            Comment = commentBox.Text?.Trim() ?? string.Empty,
            Key = key,
        };
    }

    public static async Task<CertificateDialogResult?> ShowCertificateAsync(
        string title,
        string defaultName,
        bool defaultIsCa,
        IReadOnlyList<X509Item> existingKeys,
        string? preselectKeyId)
    {
        var nameBox = new TextBox { Header = "名称", Text = defaultName };
        AutomationProperties.SetAutomationId(nameBox, "CertName");
        var cnBox = new TextBox { Header = "通用名称 (CN)", Text = defaultName };
        AutomationProperties.SetAutomationId(cnBox, "CertCommonName");
        var oBox = new TextBox { Header = "组织 (O)" };
        AutomationProperties.SetAutomationId(oBox, "CertOrganization");
        var ouBox = new TextBox { Header = "组织单位 (OU)" };
        var lBox = new TextBox { Header = "城市 (L)" };
        var stBox = new TextBox { Header = "省份 (ST)" };
        var cBox = new TextBox { Header = "国家代码 (C)", PlaceholderText = "例如 CN" };
        var eBox = new TextBox { Header = "电子邮件 (E)" };

        var daysBox = new NumberBox
        {
            Header = "有效期（天）",
            Minimum = 1,
            Maximum = 36500,
            Value = 365,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var hashBox = new ComboBox
        {
            Header = "签名哈希算法",
            ItemsSource = X509Factory.HashAlgorithms,
            SelectedIndex = 0,
        };

        var newKeyRadio = new RadioButton { Content = "新建密钥", IsChecked = existingKeys.Count == 0 };
        AutomationProperties.SetAutomationId(newKeyRadio, "CertNewKeyMode");
        var existingKeyRadio = new RadioButton { Content = "使用数据库中的现有密钥", IsChecked = existingKeys.Count > 0 };
        AutomationProperties.SetAutomationId(existingKeyRadio, "CertExistingKeyMode");
        var existingKeyBox = new ComboBox
        {
            Header = "现有密钥",
            ItemsSource = existingKeys.ToList(),
            DisplayMemberPath = "Name",
            MinWidth = 360,
        };
        if (preselectKeyId is not null)
        {
            existingKeyBox.SelectedItem = existingKeys.FirstOrDefault(k => k.Id == preselectKeyId);
        }
        existingKeyBox.SelectedItem ??= existingKeys.FirstOrDefault();

        var algorithmBox = new ComboBox
        {
            Header = "新密钥算法",
            ItemsSource = new[] { "RSA", "ECDSA" },
            SelectedIndex = 0,
        };
        var sizeBox = new NumberBox
        {
            Header = "新密钥长度 (RSA)",
            Minimum = 1024,
            Maximum = 16384,
            Value = 4096,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var curveBox = new ComboBox
        {
            Header = "椭圆曲线 (ECDSA)",
            ItemsSource = X509Factory.EcdsaCurves,
            SelectedIndex = 0,
            IsEnabled = false,
        };

        var caCheck = new CheckBox { Content = "这是 CA 证书（BasicConstraints: CA=TRUE）", IsChecked = defaultIsCa };
        AutomationProperties.SetAutomationId(caCheck, "CertIsCa");
        var pathLenCheck = new CheckBox { Content = "限制路径长度", IsEnabled = defaultIsCa };
        var pathLenBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 32,
            Value = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            IsEnabled = false,
        };

        var usageChecks = new List<(X509KeyUsageFlags Flag, CheckBox Box)>
        {
            (X509KeyUsageFlags.DigitalSignature, new CheckBox { Content = "数字签名" }),
            (X509KeyUsageFlags.NonRepudiation, new CheckBox { Content = "不可否认性" }),
            (X509KeyUsageFlags.KeyEncipherment, new CheckBox { Content = "密钥加密" }),
            (X509KeyUsageFlags.DataEncipherment, new CheckBox { Content = "数据加密" }),
            (X509KeyUsageFlags.KeyAgreement, new CheckBox { Content = "密钥协商" }),
            (X509KeyUsageFlags.KeyCertSign, new CheckBox { Content = "证书签名" }),
            (X509KeyUsageFlags.CrlSign, new CheckBox { Content = "CRL 签名" }),
        };
        usageChecks[0].Box.IsChecked = true;
        usageChecks[2].Box.IsChecked = true;

        var ekuChecks = X509Factory.ExtendedKeyUsageChoices
            .Select(choice => new CheckBox { Content = choice.Name, Tag = choice.Oid })
            .ToList();

        var sanBox = new TextBox
        {
            Header = "使用者可选名称 (SAN) — 每行一个，格式如 DNS:example.com / IP:1.2.3.4 / EMAIL:a@b.com / URI:https://x / UPN:user@domain",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
        };
        var crlBox = new TextBox
        {
            Header = "CRL 分发点 URL — 每行一个",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
        };
        var ocspBox = new TextBox
        {
            Header = "OCSP / CA 颁发者 URL — 每行一个",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
        };
        var policyBox = new TextBox
        {
            Header = "证书策略 OID — 每行一个，例如 2.23.140.1.2.1",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
        };

        void UpdateKeyMode()
        {
            bool useNew = newKeyRadio.IsChecked == true;
            algorithmBox.IsEnabled = useNew;
            sizeBox.IsEnabled = useNew && algorithmBox.SelectedIndex == 0;
            curveBox.IsEnabled = useNew && algorithmBox.SelectedIndex == 1;
            existingKeyBox.IsEnabled = !useNew;
        }
        newKeyRadio.Checked += (_, _) => UpdateKeyMode();
        existingKeyRadio.Checked += (_, _) => UpdateKeyMode();
        algorithmBox.SelectionChanged += (_, _) => UpdateKeyMode();
        UpdateKeyMode();

        void UpdateCa()
        {
            bool isCa = caCheck.IsChecked == true;
            pathLenCheck.IsEnabled = isCa;
            pathLenBox.IsEnabled = isCa && pathLenCheck.IsChecked == true;
            if (isCa)
            {
                usageChecks.First(c => c.Flag == X509KeyUsageFlags.KeyCertSign).Box.IsChecked = true;
                usageChecks.First(c => c.Flag == X509KeyUsageFlags.CrlSign).Box.IsChecked = true;
            }
        }
        caCheck.Checked += (_, _) => UpdateCa();
        caCheck.Unchecked += (_, _) => UpdateCa();
        pathLenCheck.Checked += (_, _) => UpdateCa();
        pathLenCheck.Unchecked += (_, _) => UpdateCa();

        var subjectGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        subjectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subjectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 5; i++)
        {
            subjectGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        AddToGrid(subjectGrid, nameBox, 0, 0);
        AddToGrid(subjectGrid, cnBox, 0, 1);
        AddToGrid(subjectGrid, oBox, 1, 0);
        AddToGrid(subjectGrid, ouBox, 1, 1);
        AddToGrid(subjectGrid, lBox, 2, 0);
        AddToGrid(subjectGrid, stBox, 2, 1);
        AddToGrid(subjectGrid, cBox, 3, 0);
        AddToGrid(subjectGrid, eBox, 3, 1);
        var validityPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        validityPanel.Children.Add(daysBox);
        validityPanel.Children.Add(hashBox);
        AddToGrid(subjectGrid, validityPanel, 4, 0);
        Grid.SetColumnSpan(validityPanel, 2);

        var keyPanel = new StackPanel { Spacing = 8 };
        keyPanel.Children.Add(newKeyRadio);
        var newKeyPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(24, 0, 0, 0) };
        newKeyPanel.Children.Add(algorithmBox);
        newKeyPanel.Children.Add(sizeBox);
        newKeyPanel.Children.Add(curveBox);
        keyPanel.Children.Add(newKeyPanel);
        keyPanel.Children.Add(existingKeyRadio);
        existingKeyBox.Margin = new Thickness(24, 0, 0, 0);
        keyPanel.Children.Add(existingKeyBox);

        var usagePanel = CreateCheckBoxGrid(usageChecks.Select(c => c.Box), 3);

        var ekuPanel = CreateCheckBoxGrid(ekuChecks, 3);

        var caPanel = new StackPanel { Spacing = 8 };
        caPanel.Children.Add(caCheck);
        var pathLenPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(24, 0, 0, 0) };
        pathLenPanel.Children.Add(pathLenCheck);
        pathLenPanel.Children.Add(pathLenBox);
        caPanel.Children.Add(pathLenPanel);

        var content = new StackPanel { Spacing = 12, MinWidth = 640 };
        content.Children.Add(Section("主题 (Subject)", subjectGrid));
        content.Children.Add(Section("密钥", keyPanel));
        content.Children.Add(Section("CA 属性", caPanel));
        content.Children.Add(Section("密钥用法 (Key Usage)", usagePanel));
        content.Children.Add(Section("增强密钥用法 (Extended Key Usage)", ekuPanel));
        content.Children.Add(Section("使用者可选名称", sanBox));
        content.Children.Add(Section("CRL 分发点", crlBox));
        content.Children.Add(Section("颁发机构信息访问 (OCSP / CA Issuers)", ocspBox));
        content.Children.Add(Section("证书策略", policyBox));

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = "生成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 980.0;

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        var result = new CertificateDialogResult
        {
            Name = string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim(),
            ValidDays = double.IsNaN(daysBox.Value) ? 365 : (int)daysBox.Value,
            Hash = hashBox.SelectedItem as string ?? "SHA256",
            IsCa = caCheck.IsChecked == true,
            HasPathLengthConstraint = pathLenCheck.IsChecked == true,
            PathLengthConstraint = double.IsNaN(pathLenBox.Value) ? 0 : (int)pathLenBox.Value,
            Subject = new X509SubjectOptions
            {
                CommonName = cnBox.Text?.Trim() ?? string.Empty,
                Organization = oBox.Text?.Trim() ?? string.Empty,
                OrganizationalUnit = ouBox.Text?.Trim() ?? string.Empty,
                Locality = lBox.Text?.Trim() ?? string.Empty,
                State = stBox.Text?.Trim() ?? string.Empty,
                Country = cBox.Text?.Trim() ?? string.Empty,
                EmailAddress = eBox.Text?.Trim() ?? string.Empty,
            },
            SubjectAlternativeNames = ParseSans(sanBox.Text),
            CrlDistributionPoints = ParseLines(crlBox.Text),
            OcspUrls = ParseLines(ocspBox.Text),
            CertificatePolicies = ParseLines(policyBox.Text),
        };

        foreach ((X509KeyUsageFlags flag, CheckBox box) in usageChecks)
        {
            if (box.IsChecked == true)
            {
                result.KeyUsage |= flag;
            }
        }

        result.ExtendedKeyUsages.AddRange(ekuChecks
            .Where(b => b.IsChecked == true)
            .Select(b => (string)b.Tag));

        if (existingKeyRadio.IsChecked == true && existingKeyBox.SelectedItem is X509Item keyItem)
        {
            result.ExistingKeyId = keyItem.Id;
        }
        else
        {
            result.NewKey = new X509KeyOptions
            {
                Algorithm = algorithmBox.SelectedIndex == 1 ? X509KeyAlgorithm.Ecdsa : X509KeyAlgorithm.Rsa,
                KeySize = double.IsNaN(sizeBox.Value) ? 4096 : (int)sizeBox.Value,
                Curve = curveBox.SelectedItem as string ?? "nistP256",
            };
        }

        if (string.IsNullOrWhiteSpace(result.Subject.CommonName))
        {
            await DialogService.ShowMessageAsync("输入有误", "通用名称 (CN) 不能为空。");
            return null;
        }

        return result;
    }

    public static async Task<ExportDialogResult?> ShowExportAsync(X509Item item, bool hasPrivateKey)
    {
        var formatBox = new ComboBox
        {
            Header = "导出格式",
            ItemsSource = item.Kind switch
            {
                X509ItemKind.Certificate => new[] { "PEM 证书 (*.crt)", "DER 证书 (*.cer)", "PKCS#12 证书包 (*.pfx)" },
                X509ItemKind.Csr => new[] { "PEM 证书请求 (*.csr)", "DER 证书请求 (*.csr)" },
                _ => new[] { "PEM 私钥 (*.key)", "加密 PEM 私钥 (*.key)" },
            },
            SelectedIndex = 0,
            MinWidth = 340,
        };
        var passwordBox = new PasswordBox { Header = "密码（PKCS#12 / 加密私钥）", MinWidth = 340 };
        var includeKey = new CheckBox
        {
            Content = "包含私钥（仅 PKCS#12 且证书含私钥时有效）",
            IsEnabled = item.Kind == X509ItemKind.Certificate && hasPrivateKey,
        };

        formatBox.SelectionChanged += (_, _) =>
        {
            bool pfx = item.Kind == X509ItemKind.Certificate && formatBox.SelectedIndex == 2;
            bool encryptedKey = item.Kind == X509ItemKind.PrivateKey && formatBox.SelectedIndex == 1;
            includeKey.IsEnabled = pfx && hasPrivateKey;
            passwordBox.IsEnabled = pfx || encryptedKey;
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = item.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
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
            return null;
        }

        return new ExportDialogResult
        {
            Format = formatBox.SelectedIndex,
            Password = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password,
            IncludePrivateKey = includeKey.IsChecked == true,
        };
    }

    public static List<SanEntry> ParseSans(string? text)
    {
        var result = new List<SanEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf(':');
            string kind = separator > 0 ? line[..separator].Trim().ToUpperInvariant() : "DNS";
            string value = separator > 0 ? line[(separator + 1)..].Trim() : line;
            if (value.Length == 0)
            {
                continue;
            }

            SanKind sanKind = kind switch
            {
                "IP" or "IPADDRESS" => SanKind.Ip,
                "EMAIL" or "E" => SanKind.Email,
                "URI" or "URL" => SanKind.Uri,
                "UPN" => SanKind.Upn,
                _ => SanKind.Dns,
            };
            result.Add(new SanEntry { Kind = sanKind, Value = value });
        }

        return result;
    }

    public static List<string> ParseLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static Grid CreateCheckBoxGrid(IEnumerable<CheckBox> boxes, int columns)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        for (int i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        int index = 0;
        foreach (CheckBox box in boxes)
        {
            int row = index / columns;
            int column = index % columns;
            if (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            box.Margin = new Thickness(0, 0, 0, 4);
            AddToGrid(grid, box, row, column);
            index++;
        }

        return grid;
    }

    private static StackPanel Section(string header, UIElement content)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = header,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
        });
        panel.Children.Add(content);
        return panel;
    }

    private static void AddToGrid(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
