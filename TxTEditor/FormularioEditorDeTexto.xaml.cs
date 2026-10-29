using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
    private DialogoLocalizar? _findDialog;
    private string? _pendingUpdateInstallerPath;
    private string? _workspacePath;
    private TreeViewItem? _selectedWorkspaceItem;
    private int _zoomSize = 14;
    private int _updateCheckInProgress;
    private readonly DispatcherTimer _updateCheckTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly string _recoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "recovery.txt");
    private readonly string _themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "theme.txt");
    private readonly string _reopenAfterUpdatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "update-reopen.txt");
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
        var documentToOpen = string.IsNullOrWhiteSpace(startupPath) ? ConsumeUpdateReopenPath() : startupPath;
        if (!string.IsNullOrWhiteSpace(documentToOpen)) OpenDocument(documentToOpen);
        else RecoverDraftIfAvailable();
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync(showUpToDate: false);
        Loaded += async (_, _) =>
        {
            _updateCheckTimer.Start();
            await CheckForUpdatesAsync(showUpToDate: false);
        };
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
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => OpenWorkspaceFolder();
    private void OpenWebStudio_Click(object sender, RoutedEventArgs e) => new JanelaIdeWeb { Owner = this }.Show();
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
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(showUpToDate: true);

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

    private static FrameworkElement CreateFolderIcon()
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1.5,2.5 C 1.5,1.95 1.95,1.5 2.5,1.5 L 5.5,1.5 C 5.8,1.5 6.1,1.7 6.3,1.9 L 7.3,3.1 C 7.5,3.3 7.8,3.5 8.2,3.5 L 12.5,3.5 C 13.05,3.5 13.5,3.95 13.5,4.5 L 13.5,11.5 C 13.5,12.05 13.05,12.5 12.5,12.5 L 2.5,12.5 C 1.95,12.5 1.5,12.05 1.5,11.5 Z"),
            Width = 15,
            Height = 13,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
        return path;
    }

    private static FrameworkElement CreateFileIcon(bool isRtf)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 2.5,1 C 2.5,0.7 2.7,0.5 3,0.5 L 8.5,0.5 L 12.5,4.5 L 12.5,13 C 12.5,13.3 12.3,13.5 12,13.5 L 3,13.5 C 2.7,13.5 2.5,13.3 2.5,13 Z M 8,0.5 L 8,4.5 L 12,4.5"),
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 13,
            Height = 14,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, isRtf ? "AccentBrush" : "SidebarMutedBrush");
        return path;
    }

    private void OpenWorkspaceFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Abrir pasta de trabalho",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        _workspacePath = dialog.FolderName;
        _selectedWorkspaceItem = null;
        WorkspaceTree.Items.Clear();
        var root = CreateWorkspaceFolderItem(new DirectoryInfo(_workspacePath));
        WorkspaceTree.Items.Add(root);
        root.IsExpanded = true;
        var folderName = Path.GetFileName(_workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        WorkspaceFolderName.Text = string.IsNullOrEmpty(folderName) ? _workspacePath : folderName;
    }

    private TreeViewItem CreateWorkspaceFolderItem(DirectoryInfo directory)
    {
        var label = new TextBlock
        {
            Text = directory.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { CreateFolderIcon(), label }
        };

        var item = new TreeViewItem
        {
            Header = header,
            Tag = directory.FullName
        };
        item.Items.Add(new TreeViewItem { Header = "", Tag = "__workspace_placeholder__" });
        item.Expanded += WorkspaceFolder_Expanded;
        return item;
    }

    private void WorkspaceFolder_Expanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not string path ||
            item.Items.Count != 1 || item.Items[0] is not TreeViewItem { Tag: "__workspace_placeholder__" }) return;

        item.Items.Clear();
        try
        {
            var entries = new DirectoryInfo(path).EnumerateFileSystemInfos()
                .Where(entry => (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(entry => entry is not DirectoryInfo)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo directory)
                {
                    item.Items.Add(CreateWorkspaceFolderItem(directory));
                    continue;
                }

                if (entry is FileInfo file && (file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || file.Extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase)))
                    item.Items.Add(CreateWorkspaceFileItem(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var errorLabel = new TextBlock
            {
                Text = "N\u00E3o foi poss\u00EDvel ler esta pasta",
                FontSize = 11,
                FontStyle = FontStyles.Italic
            };
            errorLabel.SetResourceReference(TextBlock.ForegroundProperty, "SidebarMutedBrush");
            item.Items.Add(new TreeViewItem { Header = errorLabel, IsEnabled = false });
        }
    }

    private TreeViewItem CreateWorkspaceFileItem(FileInfo file)
    {
        var isRtf = file.Extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase);
        var label = new TextBlock
        {
            Text = file.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { CreateFileIcon(isRtf), label }
        };

        return new TreeViewItem
        {
            Header = header,
            Tag = file.FullName
        };
    }

    private void WorkspaceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item || item.Tag is not string path || !File.Exists(path)) return;
        if (string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _selectedWorkspaceItem = item;
            return;
        }
        if (!ConfirmSaveChanges())
        {
            if (_selectedWorkspaceItem is not null) _selectedWorkspaceItem.IsSelected = true;
            else item.IsSelected = false;
            return;
        }
        OpenDocument(path);
        if (string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase)) _selectedWorkspaceItem = item;
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
            ApplyPendingUpdateIfReady();
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
        if (_pendingUpdateInstallerPath is not null) StartPendingUpdate();
        _updateCheckTimer.Stop();
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
        if (_findDialog is { IsVisible: true })
        {
            _findDialog.Activate();
            _findDialog.FindBox.Focus();
            return;
        }

        var dialog = new DialogoLocalizar(Editor.Selection.Text, _isDarkTheme) { Owner = this };
        _findDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            _findDialog = null;
            Editor.Focus();
        };
        dialog.FindRequested += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Query))
            {
                var caret = Editor.Selection.End;
                Editor.Selection.Select(caret, caret);
                dialog.SetFeedback("Escreve o texto que pretendes localizar.");
                return;
            }

            var found = FindNext(args);
            dialog.SetFeedback(found
                ? $"Correspondência encontrada · {GetSelectionLocation()}."
                : "Não foram encontradas correspondências.", found);
        };
        dialog.ReplaceRequested += (_, args) =>
        {
            var replaced = ReplaceCurrent(args);
            dialog.SetFeedback(replaced ? "Texto substituído. A procurar a ocorrência seguinte." : "Não havia uma ocorrência selecionada para substituir.", replaced);
        };
        dialog.ReplaceAllRequested += (_, args) =>
        {
            var count = ReplaceAll(args);
            dialog.SetFeedback(count == 1 ? "1 ocorrência substituída." : $"{count} ocorrências substituídas.", count > 0);
        };
        dialog.Show();
        dialog.Activate();
    }

    private string GetSelectionLocation()
    {
        var text = BuildSearchText();
        var index = _textStarts.FindIndex(pointer => pointer.CompareTo(Editor.Selection.Start) >= 0);
        if (index < 0) index = text.Length;
        var prefix = text[..Math.Min(index, text.Length)];
        var line = prefix.Count(character => character == '\n') + 1;
        var column = prefix.Length - prefix.LastIndexOf('\n');
        return $"linha {line}, coluna {column}";
    }

    private async Task CheckForUpdatesAsync(bool showUpToDate)
    {
        if (_pendingUpdateInstallerPath is not null) return;
        if (Interlocked.Exchange(ref _updateCheckInProgress, 1) != 0) return;

        try
        {
            var update = await AtualizadorLume.CheckAsync();
            if (update is null)
            {
                if (showUpToDate)
                    MessageBox.Show(this, "Já tens a versão mais recente do Lume.", "Atualizações do Lume",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveState.Text = $"A transferir o Lume {update.Version}…";
            var installerPath = await AtualizadorLume.DownloadAndVerifyAsync(update);
            _pendingUpdateInstallerPath = installerPath;
            if (_dirty)
                SaveState.Text = "Atualização pronta · guarda o documento para instalar";
            else
                StartPendingUpdate();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            UpdateDocumentState();
            if (showUpToDate)
                MessageBox.Show(this, $"Não foi possível verificar ou instalar a atualização.\n\n{ex.Message}",
                    "Atualizações do Lume", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInProgress, 0);
        }
    }

    private void ApplyPendingUpdateIfReady()
    {
        if (!_dirty && _pendingUpdateInstallerPath is not null)
            StartPendingUpdate();
    }

    private void StartPendingUpdate()
    {
        if (_pendingUpdateInstallerPath is null) return;

        var installerPath = _pendingUpdateInstallerPath;
        try
        {
            if (!string.IsNullOrWhiteSpace(_filePath) && File.Exists(_filePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reopenAfterUpdatePath)!);
                File.WriteAllText(_reopenAfterUpdatePath, _filePath, Encoding.UTF8);
            }

            var startInfo = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!
            };
            startInfo.ArgumentList.Add("/SILENT");
            startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
            startInfo.ArgumentList.Add("/NORESTART");
            startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
            Process.Start(startInfo);
            _pendingUpdateInstallerPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            try { if (File.Exists(_reopenAfterUpdatePath)) File.Delete(_reopenAfterUpdatePath); }
            catch (IOException) { }
            _pendingUpdateInstallerPath = null;
            SaveState.Text = "Não foi possível iniciar a atualização";
            MessageBox.Show(this, $"A atualização foi transferida, mas não foi possível iniciá-la.\n\n{ex.Message}",
                "Atualizações do Lume", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? ConsumeUpdateReopenPath()
    {
        try
        {
            if (!File.Exists(_reopenAfterUpdatePath)) return null;
            var path = File.ReadAllText(_reopenAfterUpdatePath, Encoding.UTF8).Trim();
            File.Delete(_reopenAfterUpdatePath);
            return File.Exists(path) ? path : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
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

    private bool FindNext(FindEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Query)) return false;
        var text = BuildSearchText();
        var selectionText = Editor.Selection.Text;
        var selectedQuery = string.Compare(selectionText, args.Query, !args.MatchCase,
            System.Globalization.CultureInfo.CurrentCulture) == 0;
        var searchFrom = selectedQuery ? Editor.Selection.End : Editor.Selection.Start;
        var start = _textStarts.FindIndex(p => p.CompareTo(searchFrom) >= 0);
        if (start < 0) start = 0;
        var index = FindIndex(text, args.Query, start, args);
        if (index < 0 && start > 0) index = FindIndex(text, args.Query, 0, args);
        if (index < 0) { System.Media.SystemSounds.Beep.Play(); return false; }
        SelectMatch(index, args.Query.Length);
        return true;
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
    }

    private bool ReplaceCurrent(FindEventArgs args)
    {
        if (args.Query.Length == 0) return false;
        var selected = Editor.Selection.Text;
        if (string.Compare(selected, args.Query, !args.MatchCase, System.Globalization.CultureInfo.CurrentCulture) != 0)
        {
            var found = FindNext(args);
            if (!found) return false;
        }

        Editor.Selection.Text = args.Replacement;
        FindNext(args);
        return true;
    }

    private int ReplaceAll(FindEventArgs args)
    {
        if (args.Query.Length == 0) return 0;
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
        return matches.Count;
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
        Editor.SelectionBrush = (Brush)Resources["AccentBrush"];
        Editor.SelectionTextBrush = Brushes.White;
        Editor.SelectionOpacity = 0.82;
        _findDialog?.SetTheme(_isDarkTheme);
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
