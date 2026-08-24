using LetMeSee.Services;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace LetMeSee;

public partial class FileAssociationsWindow : Window
{
    private readonly List<FormatGroupRow> _groups;

    public FileAssociationsWindow()
    {
        InitializeComponent();

        var registeredExtensions = ReadRegisteredExtensions();
        _groups = SupportedImageFormats.Groups
            .Select(group => new FormatGroupRow(
                group.DisplayName,
                group.Description,
                group.Extensions.Select(extension => new ExtensionChoice(extension, registeredExtensions.Contains(extension)))))
            .ToList();

        foreach (var group in _groups)
        {
            group.SelectionChanged += (_, _) => UpdateSelectionSummary();
        }

        FormatGroupList.ItemsSource = _groups;
        ContextMenuCheckBox.IsChecked = ReadContextMenuRegistered();
        ShowRegistrationSource();
        UpdateSelectionSummary();
    }

    private IEnumerable<ExtensionChoice> AllChoices => _groups.SelectMany(group => group.Choices);

    private static HashSet<string> ReadRegisteredExtensions()
    {
        try
        {
            return new HashSet<string>(FileAssociationRegistrar.GetRegisteredExtensions(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            DiagnosticLog.Write("讀取檔案關聯失敗", ex);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool ReadContextMenuRegistered()
    {
        try
        {
            return FileAssociationRegistrar.IsImageContextMenuRegistered();
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return false;
        }
    }

    private void ShowRegistrationSource()
    {
        string currentExecutablePath;
        string? registeredExecutablePath;

        try
        {
            currentExecutablePath = FileAssociationRegistrar.GetCurrentExecutablePath();
            registeredExecutablePath = FileAssociationRegistrar.GetRegisteredExecutablePath();
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            ExecutablePathText.Text = $"無法讀取目前的關聯狀態：{ex.Message}";
            return;
        }

        ExecutablePathText.Text = currentExecutablePath;

        if (registeredExecutablePath is not null &&
            !string.Equals(registeredExecutablePath, currentExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            StaleRegistrationText.Text = registeredExecutablePath;
            StaleRegistrationPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateSelectionSummary()
    {
        var selectedCount = AllChoices.Count(choice => choice.IsSelected);
        SelectionSummaryText.Text = $"已選擇 {selectedCount} / {SupportedImageFormats.Extensions.Count} 種副檔名";
    }

    private void GroupCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FormatGroupRow group })
        {
            // Two-state clicking even though the box renders a third, indeterminate state:
            // anything short of "all selected" turns the whole group on.
            group.SetAll(group.IsGroupSelected != true);
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetAllChoices(true);
        ContextMenuCheckBox.IsChecked = true;
    }

    private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
    {
        SetAllChoices(false);
        ContextMenuCheckBox.IsChecked = false;
    }

    private void SetAllChoices(bool isSelected)
    {
        foreach (var group in _groups)
        {
            group.SetAll(isSelected);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedExtensions = AllChoices.Where(choice => choice.IsSelected).Select(choice => choice.Extension).ToList();
        var addContextMenu = ContextMenuCheckBox.IsChecked == true;

        try
        {
            FileAssociationRegistrar.Apply(selectedExtensions, addContextMenu);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            DiagnosticLog.Write("套用檔案關聯失敗", ex);
            MessageBox.Show(
                this,
                $"檔案關聯更新失敗：{Environment.NewLine}{ex.Message}",
                "LetMeSee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var summary = selectedExtensions.Count == 0 && !addContextMenu
            ? "已移除 LetMeSee 的檔案關聯。"
            : $"已套用 {selectedExtensions.Count} 種副檔名的檔案關聯。";

        MessageBox.Show(
            this,
            $"{summary}{Environment.NewLine}若檔案總管沒有立刻更新，請重新開啟檔案總管或登出再登入。",
            "LetMeSee",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool IsRegistryFailure(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException;
    }

    private sealed class FormatGroupRow : INotifyPropertyChanged
    {
        private bool _isUpdatingAll;

        public FormatGroupRow(string displayName, string description, IEnumerable<ExtensionChoice> choices)
        {
            DisplayName = displayName;
            Description = description;
            Choices = choices.ToList();

            foreach (var choice in Choices)
            {
                choice.PropertyChanged += (_, _) => OnChoiceChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler? SelectionChanged;

        public string DisplayName { get; }

        public string Description { get; }

        public IReadOnlyList<ExtensionChoice> Choices { get; }

        /// <summary>true when every extension is selected, false when none is, null when mixed.</summary>
        public bool? IsGroupSelected
        {
            get
            {
                var selectedCount = Choices.Count(choice => choice.IsSelected);
                if (selectedCount == 0)
                {
                    return false;
                }

                return selectedCount == Choices.Count ? true : null;
            }
        }

        public string CountSummary => $"{Choices.Count(choice => choice.IsSelected)} / {Choices.Count}";

        public void SetAll(bool isSelected)
        {
            _isUpdatingAll = true;

            foreach (var choice in Choices)
            {
                choice.IsSelected = isSelected;
            }

            _isUpdatingAll = false;
            OnChoiceChanged();
        }

        private void OnChoiceChanged()
        {
            if (_isUpdatingAll)
            {
                return;
            }

            OnPropertyChanged(nameof(IsGroupSelected));
            OnPropertyChanged(nameof(CountSummary));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class ExtensionChoice(string extension, bool isSelected) : INotifyPropertyChanged
    {
        private bool _isSelected = isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Extension { get; } = extension;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
