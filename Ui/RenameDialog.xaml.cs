using System.Windows;

namespace Lucent.Ui;

public partial class RenameDialog : Window
{
    public string EnteredName => NameBox.Text.Trim();

    public RenameDialog(string url, string title)
    {
        InitializeComponent();

        UrlLabel.Text = url;
        NameBox.Text = title;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };

        NameBox.TextChanged += (_, _) => SaveButton.IsEnabled = EnteredName.Length > 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
