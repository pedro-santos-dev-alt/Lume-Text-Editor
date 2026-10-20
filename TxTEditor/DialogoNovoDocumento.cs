using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EditorDeTextoSimples;

internal sealed class DialogoNovoDocumento : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBlock _validation;

    public string DocumentName { get; private set; } = "";

    public DialogoNovoDocumento(bool isDarkTheme)
    {
        var background = Brush(isDarkTheme ? "#1A2030" : "#FFFFFF");
        var foreground = Brush(isDarkTheme ? "#EEF1FC" : "#24293A");
        var secondary = Brush(isDarkTheme ? "#AAB4CD" : "#697187");
        var accent = Brush(isDarkTheme ? "#A99BFF" : "#6257E8");
        var border = Brush(isDarkTheme ? "#30384D" : "#E1E5EF");

        Title = "Novo documento — Lume";
        Width = 450;
        Height = 265;
        MinWidth = 400;
        MinHeight = 265;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = background;
        Foreground = foreground;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var layout = new Grid { Margin = new Thickness(24, 19, 24, 20) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Como se vai chamar?",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            Margin = new Thickness(0, 0, 0, 6)
        };
        layout.Children.Add(heading);

        var description = new TextBlock
        {
            Text = "Escolhe um título para o teu documento.",
            FontSize = 12,
            Foreground = secondary,
            Margin = new Thickness(0, 0, 0, 17)
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        _nameBox = new TextBox
        {
            Height = 39,
            Padding = new Thickness(11, 8, 11, 7),
            FontSize = 14,
            Foreground = foreground,
            Background = Brush(isDarkTheme ? "#222A3E" : "#F7F8FC"),
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CaretBrush = accent,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 100
        };
        _nameBox.KeyDown += NameBox_KeyDown;
        Grid.SetRow(_nameBox, 2);
        layout.Children.Add(_nameBox);

        _validation = new TextBlock
        {
            Foreground = Brush("#D04F64"),
            FontSize = 11,
            Margin = new Thickness(2, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_validation, 3);
        layout.Children.Add(_validation);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var cancel = MakeButton("Cancelar", isPrimary: false, background, foreground, border, accent);
        cancel.Click += (_, _) => { DialogResult = false; };
        var create = MakeButton("Criar documento", isPrimary: true, accent, Brush("#FFFFFF"), accent, accent);
        create.Margin = new Thickness(8, 0, 0, 0);
        create.Click += (_, _) => CreateDocument();
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        Grid.SetRow(buttons, 4);
        layout.Children.Add(buttons);

        Content = layout;
        Loaded += (_, _) => _nameBox.Focus();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateDocument();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void CreateDocument()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _validation.Text = "Escreve um título para continuar.";
            _validation.Visibility = Visibility.Visible;
            _nameBox.Focus();
            return;
        }

        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            _validation.Text = "O título contém caracteres que não podem ser usados num nome de ficheiro.";
            _validation.Visibility = Visibility.Visible;
            _nameBox.Focus();
            return;
        }

        DocumentName = name;
        DialogResult = true;
    }

    private static Button MakeButton(string text, bool isPrimary, Brush background, Brush foreground, Brush border, Brush accent)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = isPrimary ? 132 : 84,
            Height = 36,
            Padding = new Thickness(13, 6, 13, 6),
            Background = background,
            Foreground = foreground,
            BorderBrush = isPrimary ? accent : border,
            BorderThickness = new Thickness(1),
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Medium,
            Cursor = Cursors.Hand,
            IsDefault = isPrimary,
            IsCancel = !isPrimary
        };
        return button;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
