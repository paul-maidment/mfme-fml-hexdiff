using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FmlDiff.Services;
using FmlDiff.ViewModels;

namespace FmlDiff.Views;

public partial class MainWindow : Window
{
    private const double RowHeight = 24.0;
    private bool _isSyncingScroll;
    private ScrollViewer _leftScrollViewer;
    private ScrollViewer _rightScrollViewer;
    private MainWindowViewModel _attachedViewModel;
    private HexByteSelectionBehavior _leftByteSelection;
    private HexByteSelectionBehavior _rightByteSelection;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnWindowKeyDown;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        if (_attachedViewModel != null)
        {
            _attachedViewModel.ScrollToRowRequested -= OnScrollToRowRequested;
            _attachedViewModel.SearchHighlightChanged -= OnSearchHighlightChanged;
        }

        _attachedViewModel = DataContext as MainWindowViewModel;
        if (_attachedViewModel != null)
        {
            _attachedViewModel.ScrollToRowRequested += OnScrollToRowRequested;
            _attachedViewModel.SearchHighlightChanged += OnSearchHighlightChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnOpened(object sender, System.EventArgs e)
    {
        _leftScrollViewer = LeftListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        _rightScrollViewer = RightListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (_leftScrollViewer != null)
        {
            _leftScrollViewer.ScrollChanged += LeftScrollChanged;
        }

        if (_rightScrollViewer != null)
        {
            _rightScrollViewer.ScrollChanged += RightScrollChanged;
        }

        LeftListBox.SelectionChanged += OnListSelectionChanged;
        RightListBox.SelectionChanged += OnListSelectionChanged;

        HexDiffPaneProperties.ByteSelectionBegan += OnByteSelectionBegan;

        _leftByteSelection = new HexByteSelectionBehavior(LeftListBox, OnByteSelectionVisualChanged);
        _rightByteSelection = new HexByteSelectionBehavior(RightListBox, OnByteSelectionVisualChanged);

        LeftListBox.Focusable = true;
        RightListBox.Focusable = true;
    }

    private void OnByteSelectionBegan(ListBox source)
    {
        ListBox other = source == LeftListBox ? RightListBox : LeftListBox;
        HexDiffPaneProperties.ClearByteSelection(other);
        SetRowSelectionSuppressedVisual(true);
        OnByteSelectionVisualChanged();
    }

    private void SetRowSelectionSuppressedVisual(bool suppressed)
    {
        LeftListBox.Classes.Set("row-selection-suppressed", suppressed);
        RightListBox.Classes.Set("row-selection-suppressed", suppressed);
        HexDiffPaneProperties.SetRowSelectionSuppressed(LeftListBox, suppressed);
        HexDiffPaneProperties.SetRowSelectionSuppressed(RightListBox, suppressed);
    }

    private void OnByteSelectionVisualChanged()
    {
        HexDiffPaneProperties.RefreshVisibleRows(LeftListBox);
        HexDiffPaneProperties.RefreshVisibleRows(RightListBox);
    }

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (await TryCopyByteSelectionAsync())
                e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ClearActiveByteSelection())
                e.Handled = true;
        }
    }

    private async Task<bool> TryCopyByteSelectionAsync()
    {
        HexDiffPresentation presentation = ViewModel.Presentation;
        if (presentation == null)
            return false;

        ListBox activeListBox = null;
        string hexText = string.Empty;

        if (HexDiffPaneProperties.GetByteSelectionActive(LeftListBox))
        {
            activeListBox = LeftListBox;
            hexText = HexByteSelectionBehavior.GetSelectionHexText(LeftListBox, presentation);
        }
        else if (HexDiffPaneProperties.GetByteSelectionActive(RightListBox))
        {
            activeListBox = RightListBox;
            hexText = HexByteSelectionBehavior.GetSelectionHexText(RightListBox, presentation);
        }

        if (activeListBox == null || string.IsNullOrEmpty(hexText))
            return false;

        if (Clipboard == null)
            return false;

        await Clipboard.SetTextAsync(hexText);

        HexDiffPaneProperties.ClearByteSelection(LeftListBox);
        HexDiffPaneProperties.ClearByteSelection(RightListBox);
        SetRowSelectionSuppressedVisual(false);
        OnByteSelectionVisualChanged();

        return true;
    }

    private bool ClearActiveByteSelection()
    {
        bool hadSelection = HexDiffPaneProperties.GetByteSelectionActive(LeftListBox)
            || HexDiffPaneProperties.GetByteSelectionActive(RightListBox);

        if (!hadSelection)
            return false;

        _leftByteSelection.ClearSelection();
        _rightByteSelection.ClearSelection();
        SetRowSelectionSuppressedVisual(false);
        OnByteSelectionVisualChanged();
        return true;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HexDiffPaneProperties.RefreshVisibleRows(LeftListBox);
        HexDiffPaneProperties.RefreshVisibleRows(RightListBox);
    }

    private async void OpenFileAClick(object sender, RoutedEventArgs e)
    {
        string path = await PickFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.FileAPath = path;
        }
    }

    private async void OpenFileBClick(object sender, RoutedEventArgs e)
    {
        string path = await PickFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.FileBPath = path;
        }
    }

    private async void RunDiffClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RunDiffAsync();
    }

    private void PreviousDifferenceClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveToPreviousDifference();
    }

    private void NextDifferenceClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveToNextDifference();
    }

    private void FindNextSearchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveToNextSearchMatch();
    }

    private void FindPreviousSearchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveToPreviousSearchMatch();
    }

    private void OnSearchHighlightChanged()
    {
        bool onLeft = ViewModel.SearchHighlightOnLeftPane;
        HexDiffPaneProperties.SetSearchHighlightActive(LeftListBox, onLeft);
        HexDiffPaneProperties.SetSearchHighlightActive(RightListBox, !onLeft);
        HexDiffPaneProperties.RefreshVisibleRows(LeftListBox);
        HexDiffPaneProperties.RefreshVisibleRows(RightListBox);
    }

    private void OnScrollToRowRequested(int row)
    {
        TryScrollToRow(row, remainingAttempts: 12);
    }

    private void TryScrollToRow(int row, int remainingAttempts)
    {
        Dispatcher.UIThread.Post(() =>
        {
            int rowCount = ViewModel.RowIndices.Count;
            if (row < 0 || row >= rowCount || _leftScrollViewer == null || _rightScrollViewer == null)
            {
                if (remainingAttempts > 0)
                {
                    TryScrollToRow(row, remainingAttempts - 1);
                }

                return;
            }

            _isSyncingScroll = true;
            try
            {
                LeftListBox.SelectedIndex = row;
                RightListBox.SelectedIndex = row;

                double offsetY = row * RowHeight;
                _leftScrollViewer.Offset = new Vector(_leftScrollViewer.Offset.X, offsetY);
                _rightScrollViewer.Offset = new Vector(_rightScrollViewer.Offset.X, offsetY);
            }
            finally
            {
                _isSyncingScroll = false;
            }

            HexDiffPaneProperties.RefreshVisibleRows(LeftListBox);
            HexDiffPaneProperties.RefreshVisibleRows(RightListBox);
        }, DispatcherPriority.Loaded);
    }

    private async Task<string> PickFileAsync()
    {
        if (StorageProvider == null)
        {
            return string.Empty;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open FML or DAT file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("FML or DAT")
                {
                    Patterns = new[] { "*.fml", "*.dat" }
                }
            }
        });

        if (files.Count == 0)
        {
            return string.Empty;
        }

        return files[0].TryGetLocalPath() ?? string.Empty;
    }

    private void LeftScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || _rightScrollViewer == null || _leftScrollViewer == null)
        {
            return;
        }

        _isSyncingScroll = true;
        _rightScrollViewer.Offset = new Vector(_rightScrollViewer.Offset.X, _leftScrollViewer.Offset.Y);
        _isSyncingScroll = false;
    }

    private void RightScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || _leftScrollViewer == null || _rightScrollViewer == null)
        {
            return;
        }

        _isSyncingScroll = true;
        _leftScrollViewer.Offset = new Vector(_leftScrollViewer.Offset.X, _rightScrollViewer.Offset.Y);
        _isSyncingScroll = false;
    }

}
