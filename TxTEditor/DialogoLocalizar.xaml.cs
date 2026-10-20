using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EditorDeTextoSimples;

public sealed class FindEventArgs(string query, string replacement, bool matchCase, bool wholeWord) : EventArgs
{
    public string Query { get; } = query;
    public string Replacement { get; } = replacement;
    public bool MatchCase { get; } = matchCase;
    public bool WholeWord { get; } = wholeWord;
}

public partial class DialogoLocalizar : Window
{
    public event EventHandler<FindEventArgs>? FindRequested;
    public event EventHandler<FindEventArgs>? ReplaceRequested;
    public event EventHandler<FindEventArgs>? ReplaceAllRequested;

    public DialogoLocalizar(string initialText = "", bool isDarkTheme = false)
    {
        InitializeComponent();
        SetTheme(isDarkTheme);
        FindBox.Text = initialText;
        FindBox.SelectAll();
        FindBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Raise(FindRequested); e.Handled = true; }
        };
        ReplaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Raise(ReplaceRequested); e.Handled = true; }
        };
        Loaded += (_, _) => { FindBox.Focus(); FindBox.SelectAll(); };
    }

    private void Find_Click(object sender, RoutedEventArgs e) => Raise(FindRequested);
    private void Replace_Click(object sender, RoutedEventArgs e) => Raise(ReplaceRequested);
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => Raise(ReplaceAllRequested);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void SetFeedback(string message, bool isSuccess = false)
    {
        FeedbackText.Text = message;
        FeedbackText.Foreground = Brush(isSuccess
            ? "#3C9A72"
            : isDarkTheme ? "#F2A6B4" : "#C34461");
    }

    private bool isDarkTheme;

    private void SetTheme(bool dark)
    {
        isDarkTheme = dark;
        var colors = dark
            ? new Dictionary<string, string>
            {
                ["DialogBackgroundBrush"] = "#171B2B", ["DialogInputBrush"] = "#222A3E",
                ["DialogBorderBrush"] = "#30384D", ["DialogTextBrush"] = "#EEF1FC",
                ["DialogMutedBrush"] = "#AAB4CD", ["DialogAccentBrush"] = "#8176F2",
                ["DialogHoverBrush"] = "#302C50"
            }
            : new Dictionary<string, string>
            {
                ["DialogBackgroundBrush"] = "#F5F6FB", ["DialogInputBrush"] = "#FFFFFF",
                ["DialogBorderBrush"] = "#E1E5EF", ["DialogTextBrush"] = "#24293A",
                ["DialogMutedBrush"] = "#697187", ["DialogAccentBrush"] = "#6257E8",
                ["DialogHoverBrush"] = "#EEEAFE"
            };

        foreach (var (key, value) in colors)
            Resources[key] = Brush(value);
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private void Raise(EventHandler<FindEventArgs>? handler) => handler?.Invoke(this,
        new FindEventArgs(FindBox.Text, ReplaceBox.Text, MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true));
}
