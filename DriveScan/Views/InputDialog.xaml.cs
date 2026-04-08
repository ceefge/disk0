using System.Windows;

namespace DriveScan.Views;

public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string label, string title)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        InputBox.Focus();
    }

    private void OK_Click(object s, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }
}
