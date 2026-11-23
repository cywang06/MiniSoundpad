using System.Windows;
using System.Windows.Controls;

namespace MiniSoundpad.Views;

public partial class InputDialog : Window
{
    public string Value => InputBox.Text.Trim();

    public InputDialog(string title, string prompt, string initial, IEnumerable<string>? suggestions = null)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        if (suggestions != null)
            foreach (var s in suggestions) InputBox.Items.Add(s);
        InputBox.Text = initial;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            if (InputBox.Template.FindName("PART_EditableTextBox", InputBox) is TextBox tb) tb.SelectAll();
        };
    }

    /// <summary>弹出对话框，取消返回 null。</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial = "",
                              IEnumerable<string>? suggestions = null)
    {
        var d = new InputDialog(title, prompt, initial, suggestions) { Owner = owner };
        return d.ShowDialog() == true ? d.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
