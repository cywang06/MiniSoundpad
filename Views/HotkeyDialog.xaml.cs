using System.Windows;
using System.Windows.Input;
using MiniSoundpad.Models;

namespace MiniSoundpad.Views;

public partial class HotkeyDialog : Window
{
    /// <summary>确定后的结果；null 表示清除快捷键。</summary>
    public Hotkey? Result { get; private set; }

    public HotkeyDialog(string target, Hotkey? current)
    {
        InitializeComponent();
        TargetText.Text = target;
        Result = current;
        if (current != null) KeyText.Text = current.ToString();
        Loaded += (_, _) => { Focus(); Keyboard.Focus(this); };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // 所有按键都拿来当快捷键，不让它们去触发按钮
        var key = e.Key switch
        {
            Key.System => e.SystemKey,          // Alt 组合键 / F10
            Key.ImeProcessed => e.ImeProcessedKey, // 中文输入法开着时
            _ => e.Key
        };

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return; // 只按了修饰键，继续等

        Result = new Hotkey(Keyboard.Modifiers, key);
        KeyText.Text = Result.ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
