using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace EditorDeTextoSimples;

public sealed class LineNumberGutter : FrameworkElement
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(RichTextBox), typeof(LineNumberGutter),
        new FrameworkPropertyMetadata(null, OnEditorChanged));

    private ScrollViewer? _editorScrollViewer;

    public RichTextBox? Editor
    {
        get => (RichTextBox?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public LineNumberGutter()
    {
        Loaded += (_, _) => AttachEditorEvents();
        Unloaded += (_, _) => DetachEditorEvents();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Editor is null || ActualHeight <= 0 || ActualWidth <= 0) return;

        var brush = TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
        var lineNumber = 1;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var paragraph in Editor.Document.Blocks.OfType<Paragraph>())
        {
            var pointer = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty && rect.Bottom >= 0 && rect.Top <= ActualHeight)
            {
                var label = new FormattedText(lineNumber.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Consolas"), 11, brush, dpi);
                drawingContext.DrawText(label, new Point(Math.Max(0, ActualWidth - label.Width - 7), rect.Top + Math.Max(0, (rect.Height - label.Height) / 2)));
            }
            lineNumber++;
        }
    }

    private static void OnEditorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var gutter = (LineNumberGutter)dependencyObject;
        gutter.DetachEditorEvents();
        if (gutter.IsLoaded) gutter.AttachEditorEvents();
        gutter.InvalidateVisual();
    }

    private void AttachEditorEvents()
    {
        if (Editor is null) return;
        Editor.TextChanged += EditorTextChanged;
        Editor.SelectionChanged += EditorSelectionChanged;
        Editor.SizeChanged += EditorSizeChanged;
        var editor = Editor;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded || !ReferenceEquals(Editor, editor)) return;
            _editorScrollViewer = FindScrollViewer(editor);
            if (_editorScrollViewer is not null) _editorScrollViewer.ScrollChanged += EditorScrolled;
            InvalidateVisual();
        }));
        InvalidateVisual();
    }

    private void DetachEditorEvents()
    {
        if (Editor is not null)
        {
            Editor.TextChanged -= EditorTextChanged;
            Editor.SelectionChanged -= EditorSelectionChanged;
            Editor.SizeChanged -= EditorSizeChanged;
        }
        if (_editorScrollViewer is not null) _editorScrollViewer.ScrollChanged -= EditorScrolled;
        _editorScrollViewer = null;
    }

    private void EditorTextChanged(object sender, TextChangedEventArgs e) => InvalidateVisual();
    private void EditorSelectionChanged(object sender, RoutedEventArgs e) => InvalidateVisual();
    private void EditorSizeChanged(object sender, SizeChangedEventArgs e) => InvalidateVisual();
    private void EditorScrolled(object sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scrollViewer) return scrollViewer;
            var result = FindScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }
}
