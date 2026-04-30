using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using FmlDiff.Services;

namespace FmlDiff.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int BytesPerRow = 16;
    private const int MaxDifferencesDisplayed = 10000;

    private readonly FmlDataLoader _dataLoader;
    private readonly HexDiffEngine _diffEngine;

    private string _fileAPath = string.Empty;
    private string _fileBPath = string.Empty;
    private string _statusMessage = "Choose two files to begin.";
    private bool _isBusy;
    private int _selectedRowIndex = -1;
    private string _differenceNavigatorText = "No differences loaded.";
    private List<int> _differenceOffsets = new();

    public MainWindowViewModel(FmlDataLoader dataLoader, HexDiffEngine diffEngine)
    {
        _dataLoader = dataLoader ?? throw new ArgumentNullException(nameof(dataLoader));
        _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));
        LeftRows = new ObservableCollection<HexDumpRowViewModel>();
        RightRows = new ObservableCollection<HexDumpRowViewModel>();
        Differences = new ObservableCollection<ByteDifferenceViewModel>();
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public event Action<int> ScrollToRowRequested;

    public ObservableCollection<HexDumpRowViewModel> LeftRows { get; }
    public ObservableCollection<HexDumpRowViewModel> RightRows { get; }
    public ObservableCollection<ByteDifferenceViewModel> Differences { get; }

    public string FileAPath
    {
        get => _fileAPath;
        set => SetProperty(ref _fileAPath, value);
    }

    public string FileBPath
    {
        get => _fileBPath;
        set => SetProperty(ref _fileBPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanNavigate));
            }
        }
    }

    public bool CanRun => !IsBusy;
    public bool CanNavigate => !IsBusy && _differenceOffsets.Count > 1;

    public string DifferenceNavigatorText
    {
        get => _differenceNavigatorText;
        private set => SetProperty(ref _differenceNavigatorText, value);
    }

    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        private set => SetProperty(ref _selectedRowIndex, value);
    }

    public async Task RunDiffAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FileAPath) || string.IsNullOrWhiteSpace(FileBPath))
        {
            StatusMessage = "Select both files first.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Loading and decoding files...";

            string pathA = FileAPath;
            string pathB = FileBPath;

            DiffComputationResult result = await Task.Run(() =>
            {
                byte[] leftBytes = _dataLoader.LoadCleanedBytes(pathA);
                byte[] rightBytes = _dataLoader.LoadCleanedBytes(pathB);

                List<int> differenceOffsets = FindDifferenceOffsets(leftBytes, rightBytes);
                List<HexDumpRowViewModel> leftRows = BuildRows(leftBytes, differenceOffsets);
                List<HexDumpRowViewModel> rightRows = BuildRows(rightBytes, differenceOffsets);
                List<ByteDifferenceViewModel> differences = BuildDifferenceRows(leftBytes, rightBytes, differenceOffsets);

                return new DiffComputationResult(leftRows, rightRows, differences, differenceOffsets);
            });

            LeftRows.Clear();
            foreach (HexDumpRowViewModel row in result.LeftRows)
            {
                LeftRows.Add(row);
            }

            RightRows.Clear();
            foreach (HexDumpRowViewModel row in result.RightRows)
            {
                RightRows.Add(row);
            }

            Differences.Clear();
            foreach (ByteDifferenceViewModel diff in result.Differences)
            {
                Differences.Add(diff);
            }

            _differenceOffsets = result.DifferenceOffsets;
            SelectedRowIndex = -1;
            OnPropertyChanged(nameof(CanNavigate));

            if (_differenceOffsets.Count == 0)
            {
                DifferenceNavigatorText = "No differences found.";
                StatusMessage = "No differences found (after tag 0x97 removal).";
            }
            else
            {
                if (_differenceOffsets.Count > result.Differences.Count)
                {
                    StatusMessage = $"Differences found: {_differenceOffsets.Count}. Showing first {result.Differences.Count} in table.";
                }
                else
                {
                    StatusMessage = $"Differences found: {_differenceOffsets.Count}.";
                }

                _currentDifferenceIndex = -1;
                MoveToNextDifference();
            }
        }
        catch (Exception ex)
        {
            LeftRows.Clear();
            RightRows.Clear();
            Differences.Clear();
            _differenceOffsets = new List<int>();
            DifferenceNavigatorText = "No differences loaded.";
            StatusMessage = $"Error: {ex.Message}";
            OnPropertyChanged(nameof(CanNavigate));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int _currentDifferenceIndex = -1;

    public void MoveToNextDifference()
    {
        if (_differenceOffsets.Count == 0)
        {
            return;
        }

        _currentDifferenceIndex = (_currentDifferenceIndex + 1) % _differenceOffsets.Count;
        NavigateToCurrentDifference();
    }

    public void MoveToPreviousDifference()
    {
        if (_differenceOffsets.Count == 0)
        {
            return;
        }

        _currentDifferenceIndex = (_currentDifferenceIndex - 1 + _differenceOffsets.Count) % _differenceOffsets.Count;
        NavigateToCurrentDifference();
    }

    private void NavigateToCurrentDifference()
    {
        if (_currentDifferenceIndex < 0 || _currentDifferenceIndex >= _differenceOffsets.Count)
        {
            return;
        }

        int offset = _differenceOffsets[_currentDifferenceIndex];
        int row = offset / BytesPerRow;
        SelectedRowIndex = row;
        DifferenceNavigatorText = $"Difference {_currentDifferenceIndex + 1}/{_differenceOffsets.Count} at 0x{offset:X8}";
        ScrollToRowRequested?.Invoke(row);
    }

    private static List<int> FindDifferenceOffsets(IReadOnlyList<byte> leftBytes, IReadOnlyList<byte> rightBytes)
    {
        int maxLen = Math.Max(leftBytes.Count, rightBytes.Count);
        var offsets = new List<int>();

        for (int i = 0; i < maxLen; i++)
        {
            bool leftExists = i < leftBytes.Count;
            bool rightExists = i < rightBytes.Count;
            if (!leftExists || !rightExists || leftBytes[i] != rightBytes[i])
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static List<ByteDifferenceViewModel> BuildDifferenceRows(
        IReadOnlyList<byte> leftBytes,
        IReadOnlyList<byte> rightBytes,
        IReadOnlyList<int> differenceOffsets)
    {
        int count = Math.Min(differenceOffsets.Count, MaxDifferencesDisplayed);
        var rows = new List<ByteDifferenceViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            int offset = differenceOffsets[i];
            string left = offset < leftBytes.Count ? leftBytes[offset].ToString("X2") : "--";
            string right = offset < rightBytes.Count ? rightBytes[offset].ToString("X2") : "--";
            rows.Add(new ByteDifferenceViewModel(offset, left, right));
        }

        return rows;
    }

    private static List<HexDumpRowViewModel> BuildRows(IReadOnlyList<byte> bytes, IReadOnlyList<int> differenceOffsets)
    {
        var differenceLookup = new HashSet<int>(differenceOffsets);
        int rowCount = (bytes.Count + BytesPerRow - 1) / BytesPerRow;
        var rows = new List<HexDumpRowViewModel>(rowCount);

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int offset = rowIndex * BytesPerRow;
            var ascii = new StringBuilder(BytesPerRow);
            var hexCells = new List<HexByteCellViewModel>(BytesPerRow);

            for (int i = 0; i < BytesPerRow; i++)
            {
                int index = offset + i;
                if (index < bytes.Count)
                {
                    byte b = bytes[index];
                    bool isDifferent = differenceLookup.Contains(index);
                    hexCells.Add(new HexByteCellViewModel(
                        b.ToString("X2"),
                        isDifferent ? "#B54545" : "Transparent",
                        isDifferent ? "#FFFFFF" : "#E8EAF0"));
                    ascii.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }
                else
                {
                    hexCells.Add(new HexByteCellViewModel("  ", "Transparent", "#7F8798"));
                    ascii.Append(' ');
                }
            }

            rows.Add(new HexDumpRowViewModel(rowIndex, offset.ToString("X8"), hexCells, ascii.ToString()));
        }

        return rows;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly record struct DiffComputationResult(
        List<HexDumpRowViewModel> LeftRows,
        List<HexDumpRowViewModel> RightRows,
        List<ByteDifferenceViewModel> Differences,
        List<int> DifferenceOffsets
    );
}

public sealed class HexDumpRowViewModel
{
    public HexDumpRowViewModel(int rowIndex, string offsetText, IReadOnlyList<HexByteCellViewModel> hexCells, string asciiText)
    {
        RowIndex = rowIndex;
        OffsetText = offsetText;
        HexCells = hexCells;
        AsciiText = asciiText;
    }

    public int RowIndex { get; }
    public string OffsetText { get; }
    public IReadOnlyList<HexByteCellViewModel> HexCells { get; }
    public string AsciiText { get; }
    public string RowBackground => "#1F2636";
}

public sealed class HexByteCellViewModel
{
    public HexByteCellViewModel(string text, string background, string foreground)
    {
        Text = text;
        Background = background;
        Foreground = foreground;
    }

    public string Text { get; }
    public string Background { get; }
    public string Foreground { get; }
}

public sealed class ByteDifferenceViewModel
{
    public ByteDifferenceViewModel(int offset, string leftHex, string rightHex)
    {
        Offset = offset;
        LeftHex = leftHex;
        RightHex = rightHex;
    }

    public int Offset { get; }
    public string LeftHex { get; }
    public string RightHex { get; }

    public string OffsetText => $"0x{Offset:X8}";
}
