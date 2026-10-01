using System.Security.Cryptography.X509Certificates;
using KasumiCertHelper.Core.Localization;
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
        var nameBox = new TextBox { Header = Loc.Get("X509Dlg_Name"), Text = defaultName, MinWidth = 380 };
        AutomationProperties.SetAutomationId(nameBox, "KeyName");
        var algorithmBox = new ComboBox
        {
            Header = Loc.Get("Gpg_HeaderAlgorithm"),
            ItemsSource = new[] { "RSA", "ECDSA" },
            SelectedIndex = initial.Algorithm == X509KeyAlgorithm.Ecdsa ? 1 : 0,
            MinWidth = 380,
        };
        var sizeBox = new NumberBox
        {
            Header = Loc.Get("X509Dlg_RsaKeySize"),
            Minimum = 1024,
            Maximum = 16384,
            Value = initial.KeySize <= 0 ? 4096 : initial.KeySize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 380,
        };
        var curveBox = new ComboBox
        {
            Header = Loc.Get("X509Dlg_EcdsaCurve"),
            ItemsSource = X509Factory.EcdsaCurves,
            SelectedItem = initial.Curve,
            MinWidth = 380,
        };
        var commentBox = new TextBox { Header = Loc.Get("X509Dlg_Comment"), MinWidth = 380 };

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
            Title = Loc.Get("X509Dlg_NewKeyTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = Loc.Get("Common_Generate"),
            CloseButtonText = Loc.Get("Common_Cancel"),
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
        var nameBox = new TextBox { Header = Loc.Get("X509Dlg_Name"), Text = defaultName };
        AutomationProperties.SetAutomationId(nameBox, "CertName");
        var cnBox = new TextBox { Header = Loc.Get("X509Dlg_CommonName"), Text = defaultName };
        AutomationProperties.SetAutomationId(cnBox, "CertCommonName");
        var oBox = new TextBox { Header = Loc.Get("X509Dlg_Organization") };
        AutomationProperties.SetAutomationId(oBox, "CertOrganization");
        var ouBox = new TextBox { Header = Loc.Get("X509Dlg_OrganizationalUnit") };
        var lBox = new TextBox { Header = Loc.Get("X509Dlg_Locality") };
        var stBox = new TextBox { Header = Loc.Get("X509Dlg_State") };
        var cBox = new TextBox { Header = Loc.Get("X509Dlg_CountryCode"), PlaceholderText = Loc.Get("X509Dlg_CountryCodeHint") };
        var eBox = new TextBox { Header = Loc.Get("X509Dlg_Email") };

        var daysBox = new NumberBox
        {
            Header = Loc.Get("X509Dlg_ValidityDays"),
            Minimum = 1,
            Maximum = 36500,
            Value = 365,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var hashBox = new ComboBox
        {
            Header = Loc.Get("X509Dlg_HashAlgorithm"),
            ItemsSource = X509Factory.HashAlgorithms,
            SelectedIndex = 0,
        };

        var newKeyRadio = new RadioButton { Content = Loc.Get("X509Dlg_NewKeyOption"), IsChecked = existingKeys.Count == 0 };
        AutomationProperties.SetAutomationId(newKeyRadio, "CertNewKeyMode");
        var existingKeyRadio = new RadioButton { Content = Loc.Get("X509Dlg_ExistingKeyOption"), IsChecked = existingKeys.Count > 0 };
        AutomationProperties.SetAutomationId(existingKeyRadio, "CertExistingKeyMode");
        var existingKeyBox = new ComboBox
        {
            Header = Loc.Get("X509Dlg_ExistingKey"),
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
            Header = Loc.Get("X509Dlg_NewKeyAlgorithm"),
            ItemsSource = new[] { "RSA", "ECDSA" },
            SelectedIndex = 0,
        };
        var sizeBox = new NumberBox
        {
            Header = Loc.Get("X509Dlg_NewKeySize"),
            Minimum = 1024,
            Maximum = 16384,
            Value = 4096,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var curveBox = new ComboBox
        {
            Header = Loc.Get("X509Dlg_EcdsaCurve"),
            ItemsSource = X509Factory.EcdsaCurves,
            SelectedIndex = 0,
            IsEnabled = false,
        };

        var caCheck = new CheckBox { Content = Loc.Get("X509Dlg_IsCa"), IsChecked = defaultIsCa };
        AutomationProperties.SetAutomationId(caCheck, "CertIsCa");
        var pathLenCheck = new CheckBox { Content = Loc.Get("X509Dlg_LimitPathLength"), IsEnabled = defaultIsCa };
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
            (X509KeyUsageFlags.DigitalSignature, new CheckBox { Content = Loc.Get("Cert_KeyUsage_DigitalSignature") }),
            (X509KeyUsageFlags.NonRepudiation, new CheckBox { Content = Loc.Get("Cert_KeyUsage_NonRepudiation") }),
            (X509KeyUsageFlags.KeyEncipherment, new CheckBox { Content = Loc.Get("Cert_KeyUsage_KeyEncipherment") }),
            (X509KeyUsageFlags.DataEncipherment, new CheckBox { Content = Loc.Get("Cert_KeyUsage_DataEncipherment") }),
            (X509KeyUsageFlags.KeyAgreement, new CheckBox { Content = Loc.Get("Cert_KeyUsage_KeyAgreement") }),
            (X509KeyUsageFlags.KeyCertSign, new CheckBox { Content = Loc.Get("Cert_KeyUsage_KeyCertSign") }),
            (X509KeyUsageFlags.CrlSign, new CheckBox { Content = Loc.Get("Cert_KeyUsage_CrlSign") }),
        };
        usageChecks[0].Box.IsChecked = true;
        usageChecks[2].Box.IsChecked = true;

        var ekuChecks = X509Factory.ExtendedKeyUsageChoices
            .Select(choice => new CheckBox { Content = Loc.Get(choice.NameKey), Tag = choice.Oid })
            .ToList();

        var sanBox = new TextBox
        {
            Header = Loc.Get("X509Dlg_SanHint"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
        };
        var crlBox = new TextBox
        {
            Header = Loc.Get("X509Dlg_CrlHint"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
        };
        var ocspBox = new TextBox
        {
            Header = Loc.Get("X509Dlg_OcspHint"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
        };
        var policyBox = new TextBox
        {
            Header = Loc.Get("X509Dlg_PolicyHint"),
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
        content.Children.Add(Section(Loc.Get("X509Dlg_SectionSubject"), subjectGrid));
        content.Children.Add(Section(Loc.Get("X509_NewKey"), keyPanel));
        content.Children.Add(Section(Loc.Get("X509Dlg_SectionCa"), caPanel));
        content.Children.Add(Section(Loc.Get("X509Dlg_SectionKeyUsage"), usagePanel));
        content.Children.Add(Section(Loc.Get("X509Dlg_SectionEku"), ekuPanel));
        content.Children.Add(Section(Loc.Get("Cert_Row_San"), sanBox));
        content.Children.Add(Section(Loc.Get("Cert_Row_Crl"), crlBox));
        content.Children.Add(Section(Loc.Get("X509Dlg_SectionAia"), ocspBox));
        content.Children.Add(Section(Loc.Get("Cert_Row_Policies"), policyBox));

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = Loc.Get("Common_Generate"),
            CloseButtonText = Loc.Get("Common_Cancel"),
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
            await DialogService.ShowMessageAsync(Loc.Get("Common_InvalidInput"), Loc.Get("X509Dlg_CommonNameRequired"));
            return null;
        }

        return result;
    }

    public static async Task<ExportDialogResult?> ShowExportAsync(X509Item item, bool hasPrivateKey)
    {
        var formatBox = new ComboBox
        {
            Header = Loc.Get("X509_ExportFormat"),
            ItemsSource = item.Kind switch
            {
                X509ItemKind.Certificate => new[] { Loc.Get("X509_ExportPemCert"), Loc.Get("X509_ExportDerCert"), Loc.Get("X509_ExportPfx") },
                X509ItemKind.Csr => new[] { Loc.Get("X509_ExportPemCsr"), Loc.Get("X509_ExportDerCsr") },
                _ => new[] { Loc.Get("X509_ExportPemKey"), Loc.Get("X509_ExportEncryptedPemKey") },
            },
            SelectedIndex = 0,
            MinWidth = 340,
        };
        var passwordBox = new PasswordBox { Header = Loc.Get("X509_ExportPassword"), MinWidth = 340 };
        var includeKey = new CheckBox
        {
            Content = Loc.Get("X509_ExportIncludePrivateKey"),
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
            Title = Loc.Get("X509_ExportOptionsTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Common_Export"),
            CloseButtonText = Loc.Get("Common_Cancel"),
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