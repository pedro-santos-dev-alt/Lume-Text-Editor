using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EditorDeTextoSimples;

public partial class JanelaIdeWeb : Window
{
    private static readonly HashSet<string> WebExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".htm", ".css", ".js", ".mjs", ".json", ".xml", ".svg", ".md", ".txt" };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private string? _projectPath;
    private string? _filePath;
    private string? _previewHtmlPath;
    private bool _loading;
    private bool _dirty;
    private bool _webViewReady;

    public JanelaIdeWeb() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _previewTimer.Tick += PreviewTimer_Tick;
        try
        {
            await PreviewBrowser.EnsureCoreWebView2Async();
            PreviewBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            PreviewBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webViewReady = true;
            if (_projectPath is not null) MapProjectFolder();
            RenderPreview();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Não foi possível iniciar o navegador integrado. Instala o Microsoft Edge WebView2 Runtime e volta a abrir o Lume.";
            MessageBox.Show(this, $"O motor WebView2 não está disponível.\n\n{ex.Message}", "Web Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        var dialog = new OpenFolderDialog { Title = "Abrir projeto web", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        SetProjectFolder(dialog.FolderName);
    }

    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        if (_projectPath is null)
        {
            OpenProject_Click(sender, e);
            if (_projectPath is null) return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Criar ficheiro web",
            InitialDirectory = _projectPath,
            FileName = "index.html",
            Filter = "HTML (*.html)|*.html|CSS (*.css)|*.css|JavaScript (*.js)|*.js|JSON (*.json)|*.json|Todos os ficheiros (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!File.Exists(dialog.FileName))
        {
            var template = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".html" or ".htm" => "<!doctype html>\n<html lang=\"pt-PT\">\n<head>\n  <meta charset=\"utf-8\">\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n  <title>Novo site</title>\n  <link rel=\"stylesheet\" href=\"style.css\">\n</head>\n<body>\n  <h1>O teu site começa aqui</h1>\n  <script src=\"script.js\"></script>\n</body>\n</html>\n",
                ".css" => "/* Estilos do projeto */\n",
                ".js" => "// Código do projeto\n",
                ".json" => "{\n  \n}\n",
                _ => string.Empty
            };
            File.WriteAllText(dialog.FileName, template, new UTF8Encoding(false));
        }
        ReloadProjectTree();
        OpenCodeFile(dialog.FileName);
        if (IsHtml(dialog.FileName))
        {
            _previewHtmlPath = dialog.FileName;
            PreviewPageLabel.Text = Path.GetRelativePath(_projectPath!, dialog.FileName);
            RenderPreview();
        }
    }

    private void ReloadProjectTree()
    {
        if (_projectPath is null) return;
        ProjectTree.Items.Clear();
        ProjectTree.Items.Add(CreateFolderNode(new DirectoryInfo(_projectPath), isRoot: true));
        if (ProjectTree.Items[0] is TreeViewItem root) root.IsExpanded = true;
    }

    private void SetProjectFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        _projectPath = Path.GetFullPath(path);
        ProjectPathLabel.Text = _projectPath;
        ProjectNameLabel.Text = new DirectoryInfo(_projectPath).Name;
        ProjectTree.Items.Clear();
        ProjectTree.Items.Add(CreateFolderNode(new DirectoryInfo(_projectPath), isRoot: true));
        if (_webViewReady) MapProjectFolder();

        var htmlFiles = FindHtmlFiles(_projectPath);
        _previewHtmlPath = htmlFiles.FirstOrDefault(file =>
            Path.GetFileName(file).Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(file).Equals("index.htm", StringComparison.OrdinalIgnoreCase))
            ?? htmlFiles.FirstOrDefault();
        PreviewPageLabel.Text = _previewHtmlPath is null ? "Sem página HTML" : Path.GetRelativePath(_projectPath, _previewHtmlPath);
        StatusLabel.Text = _previewHtmlPath is null
            ? "Projeto aberto. Adiciona ou abre um ficheiro HTML para ver o preview."
            : "Projeto pronto. As alterações ao código atualizam o preview automaticamente.";
        if (_previewHtmlPath is not null)
        {
            OpenCodeFile(_previewHtmlPath);
            RenderPreview();
        }
    }

    private TreeViewItem CreateFolderNode(DirectoryInfo directory, bool isRoot = false)
    {
        var node = new TreeViewItem
        {
            Header = new StackPanel { Orientation = Orientation.Horizontal, Children =
            {
                new TextBlock { Text = isRoot ? "◈" : "▰", Foreground = (Brush)FindResource("Accent"), Margin = new Thickness(0, 0, 7, 0) },
                new TextBlock { Text = directory.Name, Foreground = (Brush)FindResource("Foreground") }
            } },
            Tag = directory.FullName
        };
        node.Items.Add(new TreeViewItem { Header = "", Tag = "__lazy__" });
        node.Expanded += Folder_Expanded;
        return node;
    }

    private void Folder_Expanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem node || node.Tag is not string folder || node.Items.Count != 1 || node.Items[0] is not TreeViewItem { Tag: "__lazy__" }) return;
        node.Items.Clear();
        try
        {
            var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(item => (item.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Where(item => item is DirectoryInfo || WebExtensions.Contains(Path.GetExtension(item.Name)))
                .Where(item => !item.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) && !item.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item is not DirectoryInfo)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo subfolder) node.Items.Add(CreateFolderNode(subfolder));
                else
                {
                    var fileName = Path.GetFileName(entry.Name);
                    node.Items.Add(new TreeViewItem
                    {
                        Header = new StackPanel { Orientation = Orientation.Horizontal, Children =
                        {
                            new TextBlock { Text = GetFileGlyph(entry.Extension), Foreground = GetFileBrush(entry.Extension), Margin = new Thickness(0, 0, 7, 0), FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold },
                            new TextBlock { Text = fileName, Foreground = (Brush)FindResource("Foreground") }
                        } },
                        Tag = entry.FullName
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            node.Items.Add(new TreeViewItem { Header = "Não foi possível ler esta pasta", IsEnabled = false });
        }
    }

    private static string GetFileGlyph(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => "◇", ".css" => "#", ".js" or ".mjs" => "JS", ".json" => "{}", ".svg" => "◎", _ => "·"
    };

    private Brush GetFileBrush(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => (Brush)FindResource("Accent"), ".css" => Brushes.DeepSkyBlue, ".js" or ".mjs" => Brushes.Gold, _ => (Brush)FindResource("Muted")
    };

    private static IEnumerable<string> FindHtmlFiles(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        try
        {
            while (pending.Count > 0 && files.Count < 500)
            {
                var folder = pending.Pop();
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (entry is DirectoryInfo directory)
                    {
                        if (!directory.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) && !directory.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)) pending.Push(directory.FullName);
                    }
                    else if (Path.GetExtension(entry.Name).Equals(".html", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(entry.Name).Equals(".htm", StringComparison.OrdinalIgnoreCase))
                        files.Add(entry.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        return files.OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: string path } || !File.Exists(path) || !WebExtensions.Contains(Path.GetExtension(path))) return;
        if (string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase)) return;
        if (!ConfirmPendingChanges()) return;
        OpenCodeFile(path);
        if (IsHtml(path))
        {
            _previewHtmlPath = path;
            PreviewPageLabel.Text = Path.GetRelativePath(_projectPath!, path);
        }
        SchedulePreview();
    }

    private void OpenCodeFile(string path)
    {
        try
        {
            _loading = true;
            CodeEditor.Text = File.ReadAllText(path, Encoding.UTF8);
            _filePath = path;
            _dirty = false;
            CodeEditor.IsReadOnly = false;
            EditorFileLabel.Text = $"CÓDIGO  ·  {Path.GetRelativePath(_projectPath!, path)}";
            CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path));
            DirtyLabel.Text = "";
            StatusLabel.Text = $"A editar {Path.GetFileName(path)} · Ctrl+S para guardar";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLabel.Text = $"Não foi possível abrir {Path.GetFileName(path)}: {ex.Message}";
        }
        finally { _loading = false; }
    }

    private void CodeEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        DirtyLabel.Text = "● por guardar";
        StatusLabel.Text = "Alterações por guardar · Ctrl+S para guardar no projeto";
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        if (_previewHtmlPath is null) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        RenderPreview();
    }

    private void MapProjectFolder()
    {
        if (!_webViewReady || _projectPath is null) return;
        PreviewBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping("lume-workspace", _projectPath, CoreWebView2HostResourceAccessKind.Allow);
    }

    private void RenderPreview()
    {
        if (!_webViewReady || _projectPath is null || _previewHtmlPath is null) return;
        try
        {
            var html = string.Equals(_filePath, _previewHtmlPath, StringComparison.OrdinalIgnoreCase)
                ? CodeEditor.Text
                : File.ReadAllText(_previewHtmlPath, Encoding.UTF8);
            if (_dirty && _filePath is not null && !string.Equals(_filePath, _previewHtmlPath, StringComparison.OrdinalIgnoreCase))
                html = InjectEditedResource(html, _previewHtmlPath, _filePath, CodeEditor.Text);
            var relative = Path.GetRelativePath(_projectPath, _previewHtmlPath).Replace('\\', '/');
            var baseUri = "https://lume-workspace/" + string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
            var baseTag = $"<base href=\"{baseUri}\">";
            if (Regex.IsMatch(html, "<head\\b[^>]*>", RegexOptions.IgnoreCase))
                html = Regex.Replace(html, "<head\\b[^>]*>", match => match.Value + baseTag, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            else html = $"<!doctype html><html><head>{baseTag}<meta charset=\"utf-8\"></head><body>{html}</body></html>";
            PreviewBrowser.NavigateToString(html);
            PreviewPageLabel.Text = Path.GetRelativePath(_projectPath, _previewHtmlPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusLabel.Text = $"Preview indisponível: {ex.Message}";
        }
    }

    private static string InjectEditedResource(string html, string htmlPath, string editedPath, string code)
    {
        var htmlDirectory = Path.GetDirectoryName(htmlPath)!;
        var linkPattern = new Regex("<link\\b(?=[^>]*\\bhref\\s*=\\s*['\"](?<url>[^'\"]+)['\"])[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        html = linkPattern.Replace(html, match =>
        {
            var url = match.Groups["url"].Value.Split('?', '#')[0];
            if (string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _)) return match.Value;
            var resolved = Path.GetFullPath(Path.Combine(htmlDirectory, Uri.UnescapeDataString(url.Replace('/', Path.DirectorySeparatorChar))));
            if (!string.Equals(resolved, editedPath, StringComparison.OrdinalIgnoreCase)) return match.Value;
            return "<style>\n" + code.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase) + "\n</style>";
        });

        var scriptPattern = new Regex("<script\\b(?=[^>]*\\bsrc\\s*=\\s*['\"](?<url>[^'\"]+)['\"])[^>]*>\\s*</script>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return scriptPattern.Replace(html, match =>
        {
            var url = match.Groups["url"].Value.Split('?', '#')[0];
            if (string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _)) return match.Value;
            var resolved = Path.GetFullPath(Path.Combine(htmlDirectory, Uri.UnescapeDataString(url.Replace('/', Path.DirectorySeparatorChar))));
            if (!string.Equals(resolved, editedPath, StringComparison.OrdinalIgnoreCase)) return match.Value;
            return "<script>\n" + code.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase) + "\n</script>";
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrentFile();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderPreview();

    private void SaveCurrentFile()
    {
        if (string.IsNullOrWhiteSpace(_filePath)) return;
        try
        {
            File.WriteAllText(_filePath, CodeEditor.Text, new UTF8Encoding(false));
            _dirty = false;
            DirtyLabel.Text = "Guardado";
            StatusLabel.Text = $"Guardado · {Path.GetFileName(_filePath)}";
            SchedulePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Não foi possível guardar o ficheiro.\n\n{ex.Message}", "Erro ao guardar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmPendingChanges()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this, $"Guardar alterações em {Path.GetFileName(_filePath)}?", "Alterações por guardar", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No) return true;
        SaveCurrentFile();
        return !_dirty;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveCurrentFile();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RenderPreview();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private static bool IsHtml(string path) => Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".htm", StringComparison.OrdinalIgnoreCase);

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _previewTimer.Stop();
        if (!ConfirmPendingChanges()) e.Cancel = true;
    }
}
