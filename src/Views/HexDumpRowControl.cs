using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FmlDiff.Services;

namespace FmlDiff.Views;

public sealed class HexDumpRowControl : UserControl
{
    private static readonly string[] HexTable = CreateHexTable();

    private readonly TextBlock _offsetText;
    private readonly Border[] _hexBorders = new Border[16];
    private readonly TextBlock[] _hexTexts = new TextBlock[16];
    private readonly TextBlock _asciiText;

    public HexDumpRowControl()
    {
        _offsetText = new TextBlock
        {
            FontFamily = "Consolas",
            FontSize = 14,
            Foreground = HexDiffTheme.DefaultText,
            VerticalAlignment = VerticalAlignment.Center
        };

        var hexPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        for (int i = 0; i < 16; i++)
        {
            var text = new TextBlock
            {
                FontFamily = "Consolas",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Padding = new Thickness(1, 0),
                Height = 20,
                Child = text
            };

            _hexTexts[i] = text;
            _hexBorders[i] = border;
            hexPanel.Children.Add(border);
        }

        _asciiText = new TextBlock
        {
            FontFamily = "Consolas",
            FontSize = 14,
            Foreground = HexDiffTheme.AsciiText,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(430)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };

        Grid.SetColumn(_offsetText, 0);
        Grid.SetColumn(hexPanel, 1);
        Grid.SetColumn(_asciiText, 2);
        grid.Children.Add(_offsetText);
        grid.Children.Add(hexPanel);
        grid.Children.Add(_asciiText);

        Content = new Border
        {
            Background = HexDiffTheme.RowBackground,
            Padding = new Thickness(6, 1),
            Height = 24,
            Child = grid
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RefreshRow();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshRow();
    }

    public void RefreshRow() => UpdateRow();

    private void UpdateRow()
    {
        if (!TryGetRowContext(out HexDiffPresentation presentation, out int rowIndex, out bool isLeft))
        {
            ClearRow();
            return;
        }

        if (rowIndex < 0 || rowIndex >= presentation.RowCount)
        {
            ClearRow();
            return;
        }

        DiffByteCell[] cells = isLeft ? presentation.LeftCells : presentation.RightCells;
        DiffByteCell[] otherCells = isLeft ? presentation.RightCells : presentation.LeftCells;
        int[] rowOffsets = isLeft ? presentation.LeftRowOffsets : presentation.RightRowOffsets;

        _offsetText.Text = rowOffsets[rowIndex].ToString("X8");

        var ascii = new StringBuilder(16);
        int cellBase = rowIndex * 16;

        for (int i = 0; i < 16; i++)
        {
            int cellIndex = cellBase + i;

            DiffByteCell cell = cellIndex < cells.Length ? cells[cellIndex] : DiffByteCell.Empty();
            DiffByteCell other = cellIndex < otherCells.Length ? otherCells[cellIndex] : DiffByteCell.Empty();

            HexDiffTheme.ApplyCellStyle(
                cell,
                other,
                _hexBorders[i],
                _hexTexts[i],
                out char asciiChar);

            ascii.Append(asciiChar);
        }

        _asciiText.Text = ascii.ToString();
    }

    private bool TryGetRowContext(out HexDiffPresentation presentation, out int rowIndex, out bool isLeft)
    {
        presentation = null;
        rowIndex = -1;
        isLeft = true;

        if (DataContext is int index)
            rowIndex = index;

        ListBox listBox = this.FindAncestorOfType<ListBox>();
        if (listBox == null)
            return false;

        presentation = HexDiffPaneProperties.GetPresentation(listBox);
        isLeft = HexDiffPaneProperties.GetIsLeftPane(listBox);
        return presentation != null && rowIndex >= 0;
    }

    private void ClearRow()
    {
        _offsetText.Text = string.Empty;
        _asciiText.Text = string.Empty;

        for (int i = 0; i < 16; i++)
        {
            _hexTexts[i].Text = string.Empty;
            _hexBorders[i].Background = Brushes.Transparent;
            _hexTexts[i].Foreground = HexDiffTheme.DefaultText;
        }
    }

    private static string[] CreateHexTable()
    {
        var table = new string[256];
        for (int i = 0; i < 256; i++)
            table[i] = i.ToString("X2");

        return table;
    }

    internal static string FormatHex(byte value) => HexTable[value];
}

internal static class HexDiffTheme
{
    public static readonly IBrush RowBackground = new SolidColorBrush(Color.Parse("#1F2636"));
    public static readonly IBrush DefaultText = new SolidColorBrush(Color.Parse("#E8EAF0"));
    public static readonly IBrush AsciiText = new SolidColorBrush(Color.Parse("#D0D6E1"));
    public static readonly IBrush MismatchBackground = new SolidColorBrush(Color.Parse("#B54545"));
    public static readonly IBrush MismatchForeground = Brushes.White;
    public static readonly IBrush InsertDeleteBackground = new SolidColorBrush(Color.Parse("#5C4A00"));
    public static readonly IBrush InsertDeleteForeground = new SolidColorBrush(Color.Parse("#FFD060"));
    public static readonly IBrush GapForeground = new SolidColorBrush(Color.Parse("#3A4560"));
    public static readonly IBrush EmptyForeground = new SolidColorBrush(Color.Parse("#7F8798"));

    public static void ApplyCellStyle(
        DiffByteCell cell,
        DiffByteCell other,
        Border border,
        TextBlock text,
        out char asciiChar)
    {
        bool isGap = cell.Kind is ByteCellKind.Gap or ByteCellKind.Empty;
        bool otherIsGap = other.Kind is ByteCellKind.Gap or ByteCellKind.Empty;

        if (isGap)
        {
            text.Text = "--";
            asciiChar = ' ';
            border.Background = Brushes.Transparent;

            if (otherIsGap)
                text.Foreground = EmptyForeground;
            else
                text.Foreground = GapForeground;

            return;
        }

        byte value = cell.Value;
        text.Text = HexDumpRowControl.FormatHex(value);
        asciiChar = value >= 32 && value <= 126 ? (char)value : '.';

        if (!otherIsGap)
        {
            if (value == other.Value)
            {
                border.Background = Brushes.Transparent;
                text.Foreground = DefaultText;
            }
            else
            {
                border.Background = MismatchBackground;
                text.Foreground = MismatchForeground;
            }
        }
        else
        {
            border.Background = InsertDeleteBackground;
            text.Foreground = InsertDeleteForeground;
        }
    }
}
