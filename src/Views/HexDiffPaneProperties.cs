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

    static HexDiffPaneProperties()
    {
        PresentationProperty.Changed.AddClassHandler<ListBox>((listBox, _) => RefreshVisibleRows(listBox));
    }

    internal static void RefreshVisibleRows(ListBox listBox)
    {
        if (listBox == null)
            return;

        foreach (HexDumpRowControl row in listBox.GetVisualDescendants().OfType<HexDumpRowControl>())
            row.RefreshRow();
    }
}
