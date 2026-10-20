using System.Windows;
using System.Windows.Input;

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

    public DialogoLocalizar(string initialText = "")
    {
        InitializeComponent();
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

    private void Raise(EventHandler<FindEventArgs>? handler) => handler?.Invoke(this,
        new FindEventArgs(FindBox.Text, ReplaceBox.Text, MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true));
}
