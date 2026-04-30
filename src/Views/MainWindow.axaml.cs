using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FmlDiff.ViewModels;

namespace FmlDiff.Views;

public partial class MainWindow : Window
{
    private const double RowHeight = 24.0;
    private bool _isSyncingScroll;
    private ScrollViewer _leftScrollViewer;
    private ScrollViewer _rightScrollViewer;
    private MainWindowViewModel _attachedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        if (_attachedViewModel != null)
        {
            _attachedViewModel.ScrollToRowRequested -= OnScrollToRowRequested;
        }

        _attachedViewModel = DataContext as MainWindowViewModel;
        if (_attachedViewModel != null)
        {
            _attachedViewModel.ScrollToRowRequested += OnScrollToRowRequested;
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

    private void OnScrollToRowRequested(int row)
    {
        TryScrollToRow(row, remainingAttempts: 8);
    }

    private void TryScrollToRow(int row, int remainingAttempts)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (row < 0 || row >= LeftListBox.ItemCount || row >= RightListBox.ItemCount)
            {
                if (remainingAttempts > 0)
                {
                    TryScrollToRow(row, remainingAttempts - 1);
                }
                return;
            }

            _isSyncingScroll = true;
            LeftListBox.ScrollIntoView(LeftListBox.Items[row]);
            RightListBox.ScrollIntoView(RightListBox.Items[row]);
            _isSyncingScroll = false;
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
