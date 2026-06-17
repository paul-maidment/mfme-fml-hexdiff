using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FmlDiff.Services;

namespace FmlDiff.Views;

internal sealed class HexByteSelectionBehavior
{
    private readonly ListBox _listBox;
    private readonly Action _onSelectionChanged;
    private bool _isDragging;

    public HexByteSelectionBehavior(ListBox listBox, Action onSelectionChanged)
    {
        _listBox = listBox ?? throw new ArgumentNullException(nameof(listBox));
        _onSelectionChanged = onSelectionChanged ?? throw new ArgumentNullException(nameof(onSelectionChanged));

        _listBox.AddHandler(InputElement.PointerPressedEvent, OnListBoxPointerPressed, RoutingStrategies.Tunnel);
        _listBox.AddHandler(InputElement.PointerMovedEvent, OnListBoxPointerMoved, RoutingStrategies.Tunnel);
        _listBox.AddHandler(InputElement.PointerReleasedEvent, OnListBoxPointerReleased, RoutingStrategies.Tunnel);
    }

    public static string GetSelectionHexText(ListBox listBox, HexDiffPresentation presentation)
    {
        if (listBox == null || presentation == null)
            return string.Empty;

        int? start = HexDiffPaneProperties.GetByteSelectionStart(listBox);
        int? end = HexDiffPaneProperties.GetByteSelectionEnd(listBox);
        if (!start.HasValue || !end.HasValue)
            return string.Empty;

        DiffByteCell[] cells = HexDiffPaneProperties.GetIsLeftPane(listBox)
            ? presentation.LeftCells
            : presentation.RightCells;

        byte[] bytes = HexByteSelection.ExtractBytesFromCellRange(cells, start.Value, end.Value);
        return HexByteSelection.FormatBytesAsHex(bytes);
    }

    public void ClearSelection()
    {
        _isDragging = false;
        HexDiffPaneProperties.ClearByteSelection(_listBox);
        _onSelectionChanged();
    }

    private void OnListBoxPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_listBox).Properties.IsLeftButtonPressed)
            return;

        Point point = e.GetPosition(_listBox);
        if (!TryHitSelectableCell(point, out int cellIndex))
            return;

        e.Handled = true;
        _isDragging = true;
        e.Pointer.Capture(_listBox);

        HexDiffPaneProperties.BeginByteSelection(_listBox, cellIndex);
        _onSelectionChanged();
    }

    private void OnListBoxPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isDragging || !e.GetCurrentPoint(_listBox).Properties.IsLeftButtonPressed)
            return;

        Point point = e.GetPosition(_listBox);
        if (!TryHitSelectableCell(point, out int cellIndex))
            return;

        e.Handled = true;
        HexDiffPaneProperties.UpdateByteSelection(_listBox, cellIndex);
        _onSelectionChanged();
    }

    private void OnListBoxPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        e.Handled = true;
        e.Pointer.Capture(null);

        Point point = e.GetPosition(_listBox);
        if (TryHitSelectableCell(point, out int cellIndex))
            HexDiffPaneProperties.UpdateByteSelection(_listBox, cellIndex);

        _isDragging = false;
        _onSelectionChanged();
    }

    private bool TryHitSelectableCell(Point position, out int cellIndex)
    {
        cellIndex = -1;

        if (_listBox.InputHitTest(position) is not Visual hit)
            return false;

        HexDumpRowControl row = hit.FindAncestorOfType<HexDumpRowControl>();
        if (row == null || !row.TryGetCellIndexFromSource(hit, out cellIndex))
            return false;

        HexDiffPresentation presentation = HexDiffPaneProperties.GetPresentation(_listBox);
        if (presentation == null)
            return false;

        DiffByteCell[] cells = HexDiffPaneProperties.GetIsLeftPane(_listBox)
            ? presentation.LeftCells
            : presentation.RightCells;

        return cellIndex >= 0
            && cellIndex < cells.Length
            && cells[cellIndex].HasValue;
    }
}
