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



    public MainWindowViewModel(FmlDataLoader dataLoader, HexDiffEngine diffEngine)

    {

        _dataLoader = dataLoader ?? throw new ArgumentNullException(nameof(dataLoader));

        _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));

        RowIndices = new RowIndexList();

        Differences = new ObservableCollection<ByteDifferenceViewModel>();

    }



    public event PropertyChangedEventHandler PropertyChanged;

    public event Action<int> ScrollToRowRequested;



    public RowIndexList RowIndices { get; }

    public ObservableCollection<ByteDifferenceViewModel> Differences { get; }



    public HexDiffPresentation Presentation

    {

        get => _presentation;

        private set => SetProperty(ref _presentation, value);

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

