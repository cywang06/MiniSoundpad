using System.Windows;
using Forms = System.Windows.Forms;

namespace MiniSoundpad.Services;

/// <summary>
/// 任务栏右下角（通知区域）的托盘图标。WPF 自己没有托盘控件，借用 WinForms 的 NotifyIcon。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _iconImage;

    public event Action? ShowRequested;
    public event Action? StopAllRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
                  ?? throw new InvalidOperationException("找不到图标资源 Assets/app.ico");
        using (var s = res.Stream)
            // 按系统的小图标尺寸取（100% 缩放是 16px，150% 是 24px），这样不会糊
            _iconImage = new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);

        var menu = new Forms.ContextMenuStrip();
        var show = menu.Items.Add("显示主窗口", null, (_, _) => ShowRequested?.Invoke());
        show.Font = new System.Drawing.Font(show.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("全部停止", null, (_, _) => StopAllRequested?.Invoke());
        menu.Items.Add("设置…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Text = "Mini Soundpad",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke();
        };
    }

    /// <summary>鼠标悬停时显示的文字（系统限制最长 127 字）。</summary>
    public void SetTooltip(string text) =>
        _icon.Text = text.Length > 120 ? text[..120] + "…" : text;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false; // 不先隐藏的话，退出后图标会残留到鼠标划过才消失
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
