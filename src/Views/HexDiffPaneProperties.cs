using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FmlDiff.Services;

namespace FmlDiff.Views;

public static class HexDiffPaneProperties
{
    public static readonly AttachedProperty<HexDiffPresentation> PresentationProperty =
        AvaloniaProperty.RegisterAttached<ListBox, HexDiffPresentation>("Presentation", typeof(HexDiffPaneProperties));

    public static readonly AttachedProperty<bool> IsLeftPaneProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("IsLeftPane", typeof(HexDiffPaneProperties), defaultValue: true);

    public static HexDiffPresentation GetPresentation(ListBox listBox) =>
        listBox.GetValue(PresentationProperty);

    public static void SetPresentation(ListBox listBox, HexDiffPresentation value) =>
        listBox.SetValue(PresentationProperty, value);

    public static bool GetIsLeftPane(ListBox listBox) =>
        listBox.GetValue(IsLeftPaneProperty);

    public static void SetIsLeftPane(ListBox listBox, bool value) =>
        listBox.SetValue(IsLeftPaneProperty, value);

    public static readonly AttachedProperty<int?> SearchHighlightStartProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int?>("SearchHighlightStart", typeof(HexDiffPaneProperties));

    public static readonly AttachedProperty<int?> SearchHighlightEndProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int?>("SearchHighlightEnd", typeof(HexDiffPaneProperties));

    public static readonly AttachedProperty<bool> SearchHighlightActiveProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("SearchHighlightActive", typeof(HexDiffPaneProperties), defaultValue: false);

    public static int? GetSearchHighlightStart(ListBox listBox) =>
        listBox.GetValue(SearchHighlightStartProperty);

    public static void SetSearchHighlightStart(ListBox listBox, int? value) =>
        listBox.SetValue(SearchHighlightStartProperty, value);

    public static int? GetSearchHighlightEnd(ListBox listBox) =>
        listBox.GetValue(SearchHighlightEndProperty);

    public static void SetSearchHighlightEnd(ListBox listBox, int? value) =>
        listBox.SetValue(SearchHighlightEndProperty, value);

    public static bool GetSearchHighlightActive(ListBox listBox) =>
        listBox.GetValue(SearchHighlightActiveProperty);

    public static void SetSearchHighlightActive(ListBox listBox, bool value) =>
        listBox.SetValue(SearchHighlightActiveProperty, value);

    public static readonly AttachedProperty<int?> ByteSelectionStartProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int?>("ByteSelectionStart", typeof(HexDiffPaneProperties));

    public static readonly AttachedProperty<int?> ByteSelectionEndProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int?>("ByteSelectionEnd", typeof(HexDiffPaneProperties));

    public static readonly AttachedProperty<bool> ByteSelectionActiveProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("ByteSelectionActive", typeof(HexDiffPaneProperties), defaultValue: false);

    public static readonly AttachedProperty<bool> RowSelectionSuppressedProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("RowSelectionSuppressed", typeof(HexDiffPaneProperties), defaultValue: false);

    public static int? GetByteSelectionStart(ListBox listBox) =>
        listBox.GetValue(ByteSelectionStartProperty);

    public static void SetByteSelectionStart(ListBox listBox, int? value) =>
        listBox.SetValue(ByteSelectionStartProperty, value);

    public static int? GetByteSelectionEnd(ListBox listBox) =>
        listBox.GetValue(ByteSelectionEndProperty);

    public static void SetByteSelectionEnd(ListBox listBox, int? value) =>
        listBox.SetValue(ByteSelectionEndProperty, value);

    public static bool GetByteSelectionActive(ListBox listBox) =>
        listBox.GetValue(ByteSelectionActiveProperty);

    public static void SetByteSelectionActive(ListBox listBox, bool value) =>
        listBox.SetValue(ByteSelectionActiveProperty, value);

    public static bool GetRowSelectionSuppressed(ListBox listBox) =>
        listBox.GetValue(RowSelectionSuppressedProperty);

    public static void SetRowSelectionSuppressed(ListBox listBox, bool value) =>
        listBox.SetValue(RowSelectionSuppressedProperty, value);

    public static event Action<ListBox> ByteSelectionBegan;

    public static void BeginByteSelection(ListBox listBox, int cellIndex)
    {
        SetByteSelectionStart(listBox, cellIndex);
        SetByteSelectionEnd(listBox, cellIndex);
        SetByteSelectionActive(listBox, true);
        ByteSelectionBegan?.Invoke(listBox);
    }

    public static void UpdateByteSelection(ListBox listBox, int cellIndex)
    {
        SetByteSelectionEnd(listBox, cellIndex);
    }

    public static void ClearByteSelection(ListBox listBox)
    {
        SetByteSelectionStart(listBox, null);
        SetByteSelectionEnd(listBox, null);
        SetByteSelectionActive(listBox, false);
    }

    static HexDiffPaneProperties()
    {
        PresentationProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        SearchHighlightStartProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        SearchHighlightEndProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        SearchHighlightActiveProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        ByteSelectionStartProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        ByteSelectionEndProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        ByteSelectionActiveProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
        RowSelectionSuppressedProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
    }

    internal static void RefreshVisibleRows(ListBox listBox)
    {
        if (listBox == null)
            return;

        foreach (HexDumpRowControl row in listBox.GetVisualDescendants().OfType<HexDumpRowControl>())
            row.RefreshRow();
    }
}
