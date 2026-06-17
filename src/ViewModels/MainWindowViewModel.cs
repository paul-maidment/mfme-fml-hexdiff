using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

using System.Runtime.CompilerServices;

using System.Threading.Tasks;

using FmlDiff.Services;



namespace FmlDiff.ViewModels;



public sealed class MainWindowViewModel : INotifyPropertyChanged

{

    private const int LookaheadWindow = 128;



    private readonly FmlDataLoader _dataLoader;

    private readonly HexDiffEngine _diffEngine;



    private string _fileAPath = string.Empty;

    private string _fileBPath = string.Empty;

    private string _statusMessage = "Choose two files to begin.";

    private bool _isBusy;

    private double _loadingProgress;

    private int _selectedRowIndex = -1;

    private string _differenceNavigatorText = "No differences loaded.";

    private HexDiffPresentation _presentation;



    private List<(int VisualRow, int LeftFileOffset)> _alignedDiffRows = new();

    private int _currentDifferenceIndex = -1;

    private string _searchText = string.Empty;
    private HexSearchMode _searchMode = HexSearchMode.Utf8;
    private HexSearchPane _searchPane = HexSearchPane.PaneA;
    private string _searchNavigatorText = string.Empty;
    private int? _searchHighlightStartCellIndex;
    private int? _searchHighlightEndCellIndex;
    private bool _searchHighlightOnLeftPane;

    private List<HexSearchMatch> _searchMatches = new();
    private int _currentSearchIndex = -1;
    private string _lastSearchKey = string.Empty;



    public MainWindowViewModel(FmlDataLoader dataLoader, HexDiffEngine diffEngine)

    {

        _dataLoader = dataLoader ?? throw new ArgumentNullException(nameof(dataLoader));

        _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));

        RowIndices = new RowIndexList();

        Differences = new ObservableCollection<ByteDifferenceViewModel>();

    }



    public event PropertyChangedEventHandler PropertyChanged;

    public event Action<int> ScrollToRowRequested;

    public event Action SearchHighlightChanged;



    public RowIndexList RowIndices { get; }

    public ObservableCollection<ByteDifferenceViewModel> Differences { get; }



    public HexDiffPresentation Presentation

    {

        get => _presentation;

        private set

        {

            if (SetProperty(ref _presentation, value))

                OnPropertyChanged(nameof(CanSearchNavigate));

        }

    }



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

                OnPropertyChanged(nameof(CanSearchNavigate));

            }

        }

    }



    public bool CanRun => !IsBusy;

    public bool CanNavigate => !IsBusy && _alignedDiffRows.Count > 0;



    public double LoadingProgress

    {

        get => _loadingProgress;

        private set => SetProperty(ref _loadingProgress, value);

    }



    public string DifferenceNavigatorText

    {

        get => _differenceNavigatorText;

        private set => SetProperty(ref _differenceNavigatorText, value);

    }



    public int SelectedRowIndex

    {

        get => _selectedRowIndex;

        set => SetProperty(ref _selectedRowIndex, value);

    }



    public string SearchText

    {

        get => _searchText;

        set => SetProperty(ref _searchText, value);

    }



    public int SearchModeIndex

    {

        get => (int)_searchMode;

        set

        {

            if (value < 0 || value > 2)

                return;

            SearchMode = (HexSearchMode)value;

        }

    }



    public HexSearchMode SearchMode

    {

        get => _searchMode;

        set => SetProperty(ref _searchMode, value);

    }



    public int SearchPaneIndex

    {

        get => (int)_searchPane;

        set

        {

            if (value < 0 || value > 1)

                return;

            SearchPane = (HexSearchPane)value;

        }

    }



    public HexSearchPane SearchPane

    {

        get => _searchPane;

        set => SetProperty(ref _searchPane, value);

    }



    public string SearchNavigatorText

    {

        get => _searchNavigatorText;

        private set => SetProperty(ref _searchNavigatorText, value);

    }



    public bool CanSearchNavigate => !IsBusy && Presentation != null;



    public int? SearchHighlightStartCellIndex => _searchHighlightStartCellIndex;

    public int? SearchHighlightEndCellIndex => _searchHighlightEndCellIndex;

    public bool SearchHighlightOnLeftPane => _searchHighlightOnLeftPane;



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

            LoadingProgress = 0;

            StatusMessage = "Loading and decoding files...";



            string pathA = FileAPath;

            string pathB = FileBPath;



            IProgress<(double Percent, string Message)> progress = new Progress<(double Percent, string Message)>(update =>

            {

                LoadingProgress = update.Percent;

                StatusMessage = update.Message;

            });



            DiffComputationResult result = await Task.Run(() =>
            {
                HexDiffPresentation presentation;

                if (string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase))
                {
                    byte[] sharedBytes = LoadFileWithProgress(pathA, progress, fileStart: 0, fileEnd: 90, "A");
                    progress.Report((92, "Computing diff alignment..."));
                    presentation = HexDiffPresentation.BuildIdentical(sharedBytes);
                }
                else
                {
                    byte[] leftBytes = null;
                    byte[] rightBytes = null;

                    System.Threading.Tasks.Parallel.Invoke(
                        () => leftBytes = LoadFileWithProgress(pathA, progress, fileStart: 0, fileEnd: 45, "A"),
                        () => rightBytes = LoadFileWithProgress(pathB, progress, fileStart: 45, fileEnd: 90, "B"));

                    progress.Report((92, "Computing diff alignment..."));
                    presentation = _diffEngine.BuildPresentation(leftBytes, rightBytes, LookaheadWindow);
                }

                progress.Report((100, "Finalizing..."));
                return new DiffComputationResult(presentation);
            });



            Presentation = result.Presentation;

            RowIndices.SetCount(result.Presentation.RowCount);

            Differences.Clear();

            ClearSearchState();



            _alignedDiffRows = new List<(int, int)>(result.Presentation.DiffRows);

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

            Presentation = null;

            RowIndices.SetCount(0);

            Differences.Clear();

            _alignedDiffRows = new List<(int, int)>();

            ClearSearchState();

            DifferenceNavigatorText = "No differences loaded.";

            StatusMessage = $"Error: {ex.Message}";

            OnPropertyChanged(nameof(CanNavigate));

        }

        finally

        {

            IsBusy = false;

            LoadingProgress = 0;

        }

    }



    private byte[] LoadFileWithProgress(

        string path,

        IProgress<(double Percent, string Message)> progress,

        double fileStart,

        double fileEnd,

        string label)

    {

        progress.Report((fileStart, $"Loading file {label}..."));



        var fileProgress = new Progress<double>(fraction =>

        {

            double percent = fileStart + fraction * (fileEnd - fileStart);

            progress.Report((percent, $"Loading file {label}..."));

        });



        return _dataLoader.LoadCleanedBytes(path, progress: fileProgress);

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



    public void MoveToNextSearchMatch()

    {

        if (!EnsureSearchMatches(out string error))

        {

            SearchNavigatorText = error;

            return;

        }



        _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count;

        NavigateToCurrentSearchMatch();

    }



    public void MoveToPreviousSearchMatch()

    {

        if (!EnsureSearchMatches(out string error))

        {

            SearchNavigatorText = error;

            return;

        }



        _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;

        NavigateToCurrentSearchMatch();

    }



    private bool EnsureSearchMatches(out string error)

    {

        error = string.Empty;

        if (Presentation == null)

        {

            error = "Load a diff before searching.";

            ClearSearchHighlight();

            return false;

        }



        string searchKey = BuildSearchKey();

        if (!string.Equals(searchKey, _lastSearchKey, StringComparison.Ordinal))

        {

            _lastSearchKey = searchKey;

            _searchMatches = new List<HexSearchMatch>();

            _currentSearchIndex = -1;

            ClearSearchHighlight();



            if (!HexPaneSearcher.TryBuildPattern(SearchText, SearchMode, out byte[] pattern, out error))

                return false;



            _searchMatches = HexPaneSearcher.FindAll(Presentation, SearchPane, pattern);

            OnPropertyChanged(nameof(CanSearchNavigate));



            if (_searchMatches.Count == 0)

            {

                error = "No matches found.";

                SearchNavigatorText = error;

                return false;

            }

        }



        return _searchMatches.Count > 0;

    }



    private void NavigateToCurrentSearchMatch()

    {

        if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchMatches.Count)

            return;



        HexSearchMatch match = _searchMatches[_currentSearchIndex];

        DiffByteCell[] cells = SearchPane == HexSearchPane.PaneA

            ? Presentation.LeftCells

            : Presentation.RightCells;

        int startCell = HexPaneSearcher.FileOffsetToCellIndex(cells, match.FileOffset);

        int visualRow = startCell >= 0 ? HexPaneSearcher.CellIndexToVisualRow(startCell) : 0;

        SelectedRowIndex = visualRow;



        if (HexPaneSearcher.TryMapMatchToCells(Presentation, SearchPane, match, out startCell, out int endCell))

        {

            _searchHighlightStartCellIndex = startCell;

            _searchHighlightEndCellIndex = endCell;

            _searchHighlightOnLeftPane = SearchPane == HexSearchPane.PaneA;

            OnPropertyChanged(nameof(SearchHighlightStartCellIndex));

            OnPropertyChanged(nameof(SearchHighlightEndCellIndex));

            OnPropertyChanged(nameof(SearchHighlightOnLeftPane));

            SearchHighlightChanged?.Invoke();

        }



        string paneLabel = SearchPane == HexSearchPane.PaneA ? "A" : "B";

        SearchNavigatorText =

            $"Match {_currentSearchIndex + 1}/{_searchMatches.Count} in pane {paneLabel} at 0x{match.FileOffset:X8}";

        ScrollToRowRequested?.Invoke(visualRow);

    }



    private string BuildSearchKey() =>

        $"{SearchPane}|{SearchMode}|{SearchText}";



    private void ClearSearchState()

    {

        _searchMatches = new List<HexSearchMatch>();

        _currentSearchIndex = -1;

        _lastSearchKey = string.Empty;

        SearchNavigatorText = string.Empty;

        ClearSearchHighlight();

        OnPropertyChanged(nameof(CanSearchNavigate));

    }



    private void ClearSearchHighlight()

    {

        _searchHighlightStartCellIndex = null;

        _searchHighlightEndCellIndex = null;

        OnPropertyChanged(nameof(SearchHighlightStartCellIndex));

        OnPropertyChanged(nameof(SearchHighlightEndCellIndex));

        SearchHighlightChanged?.Invoke();

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



    private readonly record struct DiffComputationResult(HexDiffPresentation Presentation);

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

