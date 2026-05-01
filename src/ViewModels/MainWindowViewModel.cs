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
    private const int LookaheadWindow = 128;

    private readonly FmlDataLoader _dataLoader;
    private readonly HexDiffEngine _diffEngine;

    private string _fileAPath = string.Empty;
    private string _fileBPath = string.Empty;
    private string _statusMessage = "Choose two files to begin.";
    private bool _isBusy;
    private int _selectedRowIndex = -1;
    private string _differenceNavigatorText = "No differences loaded.";

    // Each entry: (visual row index, left-file offset at row start) for rows that contain at least one difference.
    private List<(int VisualRow, int LeftFileOffset)> _alignedDiffRows = new();
    private int _currentDifferenceIndex = -1;

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
    public bool CanNavigate => !IsBusy && _alignedDiffRows.Count > 1;

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
            return;

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

                DiffLayoutResult aligned = new HexDiffEngine().BuildAligned(leftBytes, rightBytes, LookaheadWindow);

                var (leftRows, rightRows, diffRows) = BuildAlignedRowPair(aligned.LeftCells, aligned.RightCells);

                return new DiffComputationResult(leftRows, rightRows, diffRows);
            });

            LeftRows.Clear();
            foreach (HexDumpRowViewModel row in result.LeftRows)
                LeftRows.Add(row);

            RightRows.Clear();
            foreach (HexDumpRowViewModel row in result.RightRows)
                RightRows.Add(row);

            Differences.Clear();

            _alignedDiffRows = result.AlignedDiffRows;
            _currentDifferenceIndex = -1;
            SelectedRowIndex = -1;
            OnPropertyChanged(nameof(CanNavigate));

            if (_alignedDiffRows.Count == 0)
            {
                DifferenceNavigatorText = "No differences found.";
                StatusMessage = "No differences found (after tag 0x97 removal).";
            }
            else
            {
                StatusMessage = $"Differences found in {_alignedDiffRows.Count} row(s).";
                MoveToNextDifference();
            }
        }
        catch (Exception ex)
        {
            LeftRows.Clear();
            RightRows.Clear();
            Differences.Clear();
            _alignedDiffRows = new List<(int, int)>();
            DifferenceNavigatorText = "No differences loaded.";
            StatusMessage = $"Error: {ex.Message}";
            OnPropertyChanged(nameof(CanNavigate));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void MoveToNextDifference()
    {
        if (_alignedDiffRows.Count == 0)
            return;

        _currentDifferenceIndex = (_currentDifferenceIndex + 1) % _alignedDiffRows.Count;
        NavigateToCurrentDifference();
    }

    public void MoveToPreviousDifference()
    {
        if (_alignedDiffRows.Count == 0)
            return;

        _currentDifferenceIndex = (_currentDifferenceIndex - 1 + _alignedDiffRows.Count) % _alignedDiffRows.Count;
        NavigateToCurrentDifference();
    }

    private void NavigateToCurrentDifference()
    {
        if (_currentDifferenceIndex < 0 || _currentDifferenceIndex >= _alignedDiffRows.Count)
            return;

        var (visualRow, leftFileOffset) = _alignedDiffRows[_currentDifferenceIndex];
        SelectedRowIndex = visualRow;
        DifferenceNavigatorText = $"Difference {_currentDifferenceIndex + 1}/{_alignedDiffRows.Count} at 0x{leftFileOffset:X8}";
        ScrollToRowRequested?.Invoke(visualRow);
    }

    /// <summary>
    /// Builds left and right row lists simultaneously from an aligned cell sequence.
    /// Gap cells (ByteCellKind.Gap / Empty) render as "--" and do not advance the file offset counter.
    /// Returns the rows plus a list of visual rows that contain at least one difference.
    /// </summary>
    private static (List<HexDumpRowViewModel> leftRows,
                    List<HexDumpRowViewModel> rightRows,
                    List<(int VisualRow, int LeftFileOffset)> diffRows)
        BuildAlignedRowPair(IReadOnlyList<DiffByteCell> leftCells, IReadOnlyList<DiffByteCell> rightCells)
    {
        int totalCells = Math.Max(leftCells.Count, rightCells.Count);
        int visualRowCount = (totalCells + BytesPerRow - 1) / BytesPerRow;

        var leftRows = new List<HexDumpRowViewModel>(visualRowCount);
        var rightRows = new List<HexDumpRowViewModel>(visualRowCount);
        var diffRows = new List<(int, int)>();

        int leftFileOffset = 0;
        int rightFileOffset = 0;

        for (int rowIndex = 0; rowIndex < visualRowCount; rowIndex++)
        {
            int leftRowStart = leftFileOffset;
            int rightRowStart = rightFileOffset;
            bool rowHasDiff = false;

            var leftHexCells = new List<HexByteCellViewModel>(BytesPerRow);
            var rightHexCells = new List<HexByteCellViewModel>(BytesPerRow);
            var leftAscii = new StringBuilder(BytesPerRow);
            var rightAscii = new StringBuilder(BytesPerRow);

            for (int i = 0; i < BytesPerRow; i++)
            {
                int cellIndex = rowIndex * BytesPerRow + i;

                DiffByteCell lc = cellIndex < leftCells.Count ? leftCells[cellIndex] : DiffByteCell.Empty();
                DiffByteCell rc = cellIndex < rightCells.Count ? rightCells[cellIndex] : DiffByteCell.Empty();

                bool leftIsGap = lc.Kind is ByteCellKind.Gap or ByteCellKind.Empty;
                bool rightIsGap = rc.Kind is ByteCellKind.Gap or ByteCellKind.Empty;

                string lBg, lFg, rBg, rFg;

                if (!leftIsGap && !rightIsGap)
                {
                    if (lc.Value == rc.Value)
                    {
                        lBg = rBg = "Transparent";
                        lFg = rFg = "#E8EAF0";
                    }
                    else
                    {
                        // Substitution — different values at same logical position
                        lBg = rBg = "#B54545";
                        lFg = rFg = "#FFFFFF";
                        rowHasDiff = true;
                    }
                }
                else if (leftIsGap && !rightIsGap)
                {
                    // Byte inserted in right file — gap on left, real byte on right
                    lBg = "Transparent";
                    lFg = "#3A4560";
                    rBg = "#5C4A00";
                    rFg = "#FFD060";
                    rowHasDiff = true;
                }
                else if (!leftIsGap && rightIsGap)
                {
                    // Byte deleted from right file — real byte on left, gap on right
                    lBg = "#5C4A00";
                    lFg = "#FFD060";
                    rBg = "Transparent";
                    rFg = "#3A4560";
                    rowHasDiff = true;
                }
                else
                {
                    // Both empty (tail padding beyond the shorter file)
                    lBg = rBg = "Transparent";
                    lFg = rFg = "#7F8798";
                }

                if (leftIsGap)
                {
                    leftHexCells.Add(new HexByteCellViewModel("--", lBg, lFg));
                    leftAscii.Append(' ');
                }
                else
                {
                    byte b = lc.Value;
                    leftHexCells.Add(new HexByteCellViewModel(b.ToString("X2"), lBg, lFg));
                    leftAscii.Append(b >= 32 && b <= 126 ? (char)b : '.');
                    leftFileOffset++;
                }

                if (rightIsGap)
                {
                    rightHexCells.Add(new HexByteCellViewModel("--", rBg, rFg));
                    rightAscii.Append(' ');
                }
                else
                {
                    byte b = rc.Value;
                    rightHexCells.Add(new HexByteCellViewModel(b.ToString("X2"), rBg, rFg));
                    rightAscii.Append(b >= 32 && b <= 126 ? (char)b : '.');
                    rightFileOffset++;
                }
            }

            leftRows.Add(new HexDumpRowViewModel(rowIndex, leftRowStart.ToString("X8"), leftHexCells, leftAscii.ToString()));
            rightRows.Add(new HexDumpRowViewModel(rowIndex, rightRowStart.ToString("X8"), rightHexCells, rightAscii.ToString()));

            if (rowHasDiff)
                diffRows.Add((rowIndex, leftRowStart));
        }

        return (leftRows, rightRows, diffRows);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

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
        List<(int VisualRow, int LeftFileOffset)> AlignedDiffRows
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
