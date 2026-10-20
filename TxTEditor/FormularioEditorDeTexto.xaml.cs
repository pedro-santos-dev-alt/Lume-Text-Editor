using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EditorDeTextoSimples;

public partial class FormularioEditorDeTexto : Window
{
    private string? _filePath;
    private string? _documentName;
    private bool _dirty;
    private bool _loading;
    private bool _editorMode;
    private bool _focusMode;
    private bool _isDarkTheme;
    private int _zoomSize = 14;
    private readonly string _recoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "recovery.txt");
    private readonly string _themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "theme.txt");
    private readonly DispatcherTimer _recoveryTimer;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public FormularioEditorDeTexto(string? startupPath = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UpdateCaptionColors();
        LoadThemePreference();
        ApplyTheme(false);
        Editor.Document.FontFamily = new FontFamily("Segoe UI");
        Editor.Document.FontSize = _zoomSize;
        UpdateDocumentState();
        UpdateCursorState();
        SetEditorMode(false);
        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _recoveryTimer.Tick += (_, _) => SaveRecoveryDraft();
        _recoveryTimer.Start();
        if (!string.IsNullOrWhiteSpace(startupPath)) OpenDocument(startupPath);
        else RecoverDraftIfAvailable();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleFocusMode();
            e.Handled = true;
            return;
        }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var handled = true;
        switch (e.Key)
        {
            case Key.N: NewDocument(); break;
            case Key.O: OpenFile(); break;
            case Key.S:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveFileAs();
                else SaveFile();
                break;
            case Key.T when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): ToggleTheme(); break;
            case Key.F: ShowFindDialog(); break;
            case Key.B: EditingCommands.ToggleBold.Execute(null, Editor); break;
            case Key.I: EditingCommands.ToggleItalic.Execute(null, Editor); break;
            case Key.U: EditingCommands.ToggleUnderline.Execute(null, Editor); break;
            case Key.P: PrintFile(); break;
            case Key.Add: case Key.OemPlus: ChangeZoom(1); break;
            case Key.Subtract: case Key.OemMinus: ChangeZoom(-1); break;
            case Key.D0: SetZoom(14); break;
            default: handled = false; break;
        }
        if (handled) e.Handled = true;
    }

    private void NewDocument_Click(object sender, RoutedEventArgs e) => NewDocument();
    private void OpenFile_Click(object sender, RoutedEventArgs e) => OpenFile();
    private void SaveFile_Click(object sender, RoutedEventArgs e) => SaveFile();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveFileAs();
    private void Find_Click(object sender, RoutedEventArgs e) => ShowFindDialog();
    private void Print_Click(object sender, RoutedEventArgs e) => PrintFile();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Bold_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
    private void Italic_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
    private void Underline_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Editor);
    private void Undo_Click(object sender, RoutedEventArgs e) { if (Editor.CanUndo) Editor.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (Editor.CanRedo) Editor.Redo(); }
    private void Cut_Click(object sender, RoutedEventArgs e) => Editor.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => Editor.Paste();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Editor.SelectAll();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeZoom(1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeZoom(-1);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(14);
    private void ToggleFocus_Click(object sender, RoutedEventArgs e) => ToggleFocusMode();
    private void ThemeButton_Click(object sender, RoutedEventArgs e) => ToggleTheme();

    private void NewDocument()
    {
        if (!ConfirmSaveChanges()) return;
        var nameDialog = new DialogoNovoDocumento(_isDarkTheme) { Owner = this };
        if (nameDialog.ShowDialog() != true) return;

        _loading = true;
        Editor.Document.Blocks.Clear();
        Editor.Document.Blocks.Add(new Paragraph());
        Editor.Document.FontFamily = new FontFamily("Segoe UI");
        Editor.Document.FontSize = _zoomSize;
        _filePath = null;
        _documentName = nameDialog.DocumentName;
        _dirty = false;
        _loading = false;
        UpdateDocumentState();
        SetEditorMode(true);
        Editor.Focus();
    }

    private void OpenFile()
    {
        if (!ConfirmSaveChanges()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Abrir documento",
            Filter = "Ficheiros suportados (*.txt;*.rtf)|*.txt;*.rtf|Ficheiros de texto (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|Todos os ficheiros (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        OpenDocument(dialog.FileName);
    }

    private void OpenDocument(string path)
    {
        var opened = false;
        try
        {
            _loading = true;
            if (Path.GetExtension(path).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(path);
                new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Load(stream, DataFormats.Rtf);
            }
            else
            {
                SetPlainText(File.ReadAllText(path, Encoding.UTF8));
            }
            _filePath = path;
            _documentName = Path.GetFileName(path);
            _dirty = false;
            DeleteRecoveryDraft();
            opened = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Não foi possível abrir o ficheiro.\n\n{ex.Message}", "Erro ao abrir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _loading = false; }
        UpdateDocumentState();
        if (opened)
        {
            SetEditorMode(true);
            Editor.Focus();
        }
    }

    private void SetPlainText(string text)
    {
        Editor.Document.Blocks.Clear();
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in paragraphs)
            Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
        if (paragraphs.Length == 0) Editor.Document.Blocks.Add(new Paragraph());
    }

    private void SaveFile()
    {
        if (!_editorMode) return;
        if (string.IsNullOrWhiteSpace(_filePath)) { SaveFileAs(); return; }
        SaveToPath(_filePath);
    }

    private void SaveFileAs()
    {
        if (!_editorMode) return;
        var dialog = new SaveFileDialog
        {
            Title = "Guardar documento",
            Filter = "Documento de texto (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|Todos os ficheiros (*.*)|*.*",
            FilterIndex = Path.GetExtension(_filePath ?? "").Equals(".rtf", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            DefaultExt = Path.GetExtension(_filePath ?? "").Equals(".rtf", StringComparison.OrdinalIgnoreCase) ? "rtf" : "txt",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_filePath) ? (_documentName ?? "Documento") : Path.GetFileName(_filePath),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) SaveToPath(dialog.FileName);
    }

    private void SaveToPath(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.Create(path);
                new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(stream, DataFormats.Rtf);
            }
            else
            {
                File.WriteAllText(path, GetPlainText(), new UTF8Encoding(false));
            }
            _filePath = path;
            _documentName = Path.GetFileName(path);
            _dirty = false;
            DeleteRecoveryDraft();
            UpdateDocumentState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Não foi possível guardar o ficheiro.\n\n{ex.Message}", "Erro ao guardar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmSaveChanges()
    {
        if (!_dirty) return true;
        var name = string.IsNullOrWhiteSpace(_filePath) ? "este documento" : $"'{Path.GetFileName(_filePath)}'";
        var answer = MessageBox.Show(this, $"Queres guardar as alterações em {name}?", "Alterações por guardar",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        SaveFile();
        return !_dirty;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmSaveChanges()) { e.Cancel = true; return; }
        _recoveryTimer.Stop();
        DeleteRecoveryDraft();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _dirty = true;
        UpdateDocumentState();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateCursorState();

    private string GetPlainText()
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private void UpdateDocumentState()
    {
        if (DocumentTitle is null) return;
        var name = _documentName ?? (string.IsNullOrWhiteSpace(_filePath) ? "Documento" : Path.GetFileName(_filePath));
        DocumentTitle.Text = name + (_dirty ? "  •" : "");
        Title = _editorMode ? $"Lume — {name}{(_dirty ? " *" : "")}" : "Lume — O teu espaço para escrever";
        SaveState.Text = _dirty ? "●  Por guardar" : "●  Guardado";
        SaveState.Foreground = _dirty ? new SolidColorBrush(Color.FromRgb(160, 103, 26)) : new SolidColorBrush(Color.FromRgb(80, 130, 96));
        SaveBadge.Background = _dirty ? new SolidColorBrush(Color.FromRgb(250, 241, 220)) : new SolidColorBrush(Color.FromRgb(238, 246, 239));
        var text = GetPlainText();
        var words = Regex.Matches(text, @"\S+").Count;
        WordState.Text = $"{words} {(words == 1 ? "palavra" : "palavras")}";
        CharacterState.Text = $"{text.Length} caracteres";
    }

    private void UpdateCursorState()
    {
        if (CursorState is null) return;
        var beforeCaret = new TextRange(Editor.Document.ContentStart, Editor.CaretPosition).Text;
        var line = beforeCaret.Count(c => c == '\n') + 1;
        var lastBreak = beforeCaret.LastIndexOf('\n');
        var column = beforeCaret.Length - lastBreak;
        CursorState.Text = $"Linha {line}, coluna {column}";
    }

    private void ChangeZoom(int delta) => SetZoom(_zoomSize + delta);

    private void SetZoom(int size)
    {
        _zoomSize = Math.Clamp(size, 9, 32);
        Editor.Document.FontSize = _zoomSize;
        ZoomLabel.Text = $"{Math.Round(_zoomSize / 14d * 100)}%";
    }

    private void ShowFindDialog()
    {
        if (!_editorMode) return;
        var dialog = new DialogoLocalizar(Editor.Selection.Text) { Owner = this };
        dialog.FindRequested += (_, args) => FindNext(args);
        dialog.ReplaceRequested += (_, args) => ReplaceCurrent(args);
        dialog.ReplaceAllRequested += (_, args) => ReplaceAll(args);
        dialog.ShowDialog();
        Editor.Focus();
    }

    private List<TextPointer> _textStarts = [];
    private List<TextPointer> _textEnds = [];

    private string BuildSearchText()
    {
        var text = new StringBuilder();
        _textStarts = [];
        _textEnds = [];
        var end = Editor.Document.ContentEnd;
        for (var pointer = Editor.Document.ContentStart; pointer.CompareTo(end) < 0; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                for (var i = 0; i < run.Length; i++)
                {
                    text.Append(run[i]);
                    _textStarts.Add(pointer.GetPositionAtOffset(i, LogicalDirection.Forward));
                    _textEnds.Add(pointer.GetPositionAtOffset(i + 1, LogicalDirection.Forward));
                }
            }
            else if (context == TextPointerContext.ElementEnd && pointer.Parent is Paragraph)
            {
                text.Append('\n');
                _textStarts.Add(pointer);
                _textEnds.Add(pointer.GetNextInsertionPosition(LogicalDirection.Forward) ?? end);
            }
        }
        return text.ToString();
    }

    private void FindNext(FindEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Query)) return;
        var text = BuildSearchText();
        var start = _textStarts.FindIndex(p => p.CompareTo(Editor.Selection.End) >= 0);
        if (start < 0) start = 0;
        var index = FindIndex(text, args.Query, start, args);
        if (index < 0 && start > 0) index = FindIndex(text, args.Query, 0, args);
        if (index < 0) { System.Media.SystemSounds.Beep.Play(); return; }
        SelectMatch(index, args.Query.Length);
    }

    private static int FindIndex(string text, string query, int start, FindEventArgs options)
    {
        var comparison = options.MatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        while ((start = text.IndexOf(query, start, comparison)) >= 0)
        {
            var left = start == 0 || !char.IsLetterOrDigit(text[start - 1]) && text[start - 1] != '_';
            var after = start + query.Length;
            var right = after >= text.Length || !char.IsLetterOrDigit(text[after]) && text[after] != '_';
            if (!options.WholeWord || left && right) return start;
            start += Math.Max(query.Length, 1);
        }
        return -1;
    }

    private void SelectMatch(int index, int length)
    {
        if (index < 0 || index + length > _textStarts.Count) return;
        Editor.Selection.Select(_textStarts[index], _textEnds[index + length - 1]);
        Editor.Focus();
    }

    private void ReplaceCurrent(FindEventArgs args)
    {
        if (args.Query.Length == 0) return;
        var selected = Editor.Selection.Text;
        if (string.Compare(selected, args.Query, !args.MatchCase, System.Globalization.CultureInfo.CurrentCulture) == 0)
            Editor.Selection.Text = args.Replacement;
        FindNext(args);
    }

    private void ReplaceAll(FindEventArgs args)
    {
        if (args.Query.Length == 0) return;
        var text = BuildSearchText();
        var matches = new List<int>();
        var index = 0;
        while ((index = FindIndex(text, args.Query, index, args)) >= 0)
        {
            matches.Add(index);
            index += args.Query.Length;
        }
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            BuildSearchText();
            SelectMatch(matches[i], args.Query.Length);
            Editor.Selection.Text = args.Replacement;
        }
    }

    private void PrintFile()
    {
        if (!_editorMode) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(((IDocumentPaginatorSource)Editor.Document).DocumentPaginator,
                string.IsNullOrWhiteSpace(_filePath) ? "Documento Lume" : Path.GetFileName(_filePath));
    }

    private void SetEditorMode(bool enabled)
    {
        _editorMode = enabled;
        HomeView.Visibility = Visibility.Visible;
        HomeView.BeginAnimation(OpacityProperty, null);
        if (enabled)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
            fadeOut.Completed += (_, _) => HomeView.Visibility = Visibility.Collapsed;
            HomeView.BeginAnimation(OpacityProperty, fadeOut);
            EditorArea.Visibility = Visibility.Visible;
            EditorArea.Opacity = 0;
            EditorArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        else
        {
            HomeView.Opacity = 0;
            HomeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            EditorArea.Visibility = Visibility.Collapsed;
        }
        DocumentHeader.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        EditorToolbar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        EditorSidebarActions.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        FindSidebarButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        FooterShortcut.Text = enabled ? "Guardar documento" : "Criar documento";
        UpdateDocumentState();
    }

    private void ToggleFocusMode()
    {
        if (!_editorMode) return;
        _focusMode = !_focusMode;
        SidebarColumn.Width = _focusMode ? new GridLength(0) : new GridLength(256);
        FocusButtonLabel.Text = _focusMode ? "Sair do foco" : "Foco";
    }

    private void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme(true);
    }

    private void LoadThemePreference()
    {
        try
        {
            if (File.Exists(_themePath))
                _isDarkTheme = bool.TryParse(File.ReadAllText(_themePath), out var dark) && dark;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ApplyTheme(bool savePreference)
    {
        var colors = _isDarkTheme
            ? new Dictionary<string, string>
            {
                ["AppCanvasBrush"] = "#101422", ["SidebarBrush"] = "#171B2B", ["SidebarTextBrush"] = "#E9ECF8",
                ["SidebarMutedBrush"] = "#969FB7", ["SidebarHoverBrush"] = "#252B40", ["SidebarFooterBrush"] = "#121624",
                ["AccentBrush"] = "#A99BFF", ["AccentSoftBrush"] = "#302C50", ["AccentHoverBrush"] = "#BDB3FF",
                ["CanvasBrush"] = "#111625", ["SurfaceBrush"] = "#1A2030", ["SurfaceAltBrush"] = "#222A3E",
                ["BorderBrush"] = "#30384D", ["TextPrimaryBrush"] = "#EEF1FC", ["TextSecondaryBrush"] = "#AAB4CD",
                ["TextMutedBrush"] = "#8C96AF"
            }
            : new Dictionary<string, string>
            {
                ["AppCanvasBrush"] = "#F3F5FB", ["SidebarBrush"] = "#EEF1F9", ["SidebarTextBrush"] = "#34394B",
                ["SidebarMutedBrush"] = "#848BA0", ["SidebarHoverBrush"] = "#E2E7F5", ["SidebarFooterBrush"] = "#E9ECF5",
                ["AccentBrush"] = "#6257E8", ["AccentSoftBrush"] = "#EEEAFE", ["AccentHoverBrush"] = "#5247D2",
                ["CanvasBrush"] = "#F3F5FB", ["SurfaceBrush"] = "#FFFFFF", ["SurfaceAltBrush"] = "#F7F8FC",
                ["BorderBrush"] = "#E1E5EF", ["TextPrimaryBrush"] = "#24293A", ["TextSecondaryBrush"] = "#697187",
                ["TextMutedBrush"] = "#9299AA"
            };

        foreach (var (key, value) in colors)
            Resources[key] = new SolidColorBrush(Color.FromRgb(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16)));

        Editor.Background = (Brush)Resources["SurfaceBrush"];
        Editor.Foreground = (Brush)Resources["TextPrimaryBrush"];
        Editor.Document.Foreground = Editor.Foreground;
        ThemeButtonLabel.Text = _isDarkTheme ? "Tema claro" : "Tema escuro";
        UpdateCaptionColors();

        if (!savePreference) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!);
            File.WriteAllText(_themePath, _isDarkTheme.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void UpdateCaptionColors()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var color = _isDarkTheme ? 0x00492D29 : 0x00E85762;
        var textColor = 0x00FFFFFF;
        try
        {
            _ = DwmSetWindowAttribute(handle, 35, ref color, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 34, ref color, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private void SaveRecoveryDraft()
    {
        if (!_editorMode || !_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!);
            File.WriteAllText(_recoveryPath, GetPlainText(), new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RecoverDraftIfAvailable()
    {
        try
        {
            if (!File.Exists(_recoveryPath)) return;
            var answer = MessageBox.Show(this, "Foi encontrado um rascunho recuperável. Queres restaurá-lo?", "Recuperar rascunho", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) { DeleteRecoveryDraft(); return; }
            _loading = true;
            SetPlainText(File.ReadAllText(_recoveryPath, Encoding.UTF8));
            _filePath = null;
            _documentName = "Rascunho recuperado";
            _dirty = true;
            _loading = false;
            UpdateDocumentState();
            SetEditorMode(true);
        }
        catch (IOException) { _loading = false; }
        catch (UnauthorizedAccessException) { _loading = false; }
    }

    private void DeleteRecoveryDraft()
    {
        try { if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
