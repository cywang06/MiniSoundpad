using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MiniSoundpad.Views;

/// <summary>
/// 简单的波形显示 + 选区。拖动鼠标移动离点击位置最近的那条边界（开始/结束）。
/// Start/End/Playhead 都是 0..1 的比例。
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    private float[] _peaks = Array.Empty<float>();
    private double _start, _end = 1, _playhead = -1;
    private bool _draggingStart;

    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)));
    private static readonly Pen WavePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 1));
    private static readonly Brush Shade = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)));
    private static readonly Pen EdgePen = Freeze(new Pen(Brushes.Gold, 2));
    private static readonly Pen PlayPen = Freeze(new Pen(Brushes.White, 1));

    /// <summary>用户拖动了边界：(是开始边界?, 新比例)</summary>
    public event Action<bool, double>? EdgeDragged;

    public double Start { get => _start; set { _start = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
    public double End { get => _end; set { _end = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
    public double Playhead { get => _playhead; set { _playhead = value; InvalidateVisual(); } }

    public void SetSamples(float[] interleaved, int channels)
    {
        const int bins = 4000;
        int frames = interleaved.Length / channels;
        _peaks = new float[bins];
        if (frames == 0) { InvalidateVisual(); return; }
        for (int b = 0; b < bins; b++)
        {
            long f0 = (long)b * frames / bins, f1 = Math.Max(f0 + 1, (long)(b + 1) * frames / bins);
            float max = 0;
            for (long f = f0; f < f1 && f < frames; f++)
                for (int c = 0; c < channels; c++)
                    max = Math.Max(max, Math.Abs(interleaved[f * channels + c]));
            _peaks[b] = Math.Min(max, 1f);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, mid = h / 2;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        if (w < 2 || _peaks.Length == 0) return;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int x = 0; x < (int)w; x++)
            {
                int b0 = (int)(x / w * _peaks.Length);
                int b1 = Math.Max(b0 + 1, (int)((x + 1) / w * _peaks.Length));
                float p = 0;
                for (int b = b0; b < b1 && b < _peaks.Length; b++) p = Math.Max(p, _peaks[b]);
                double y = Math.Max(0.5, p * (mid - 2));
                ctx.BeginFigure(new Point(x + 0.5, mid - y), false, false);
                ctx.LineTo(new Point(x + 0.5, mid + y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, WavePen, geo);

        double xs = _start * w, xe = _end * w;
        dc.DrawRectangle(Shade, null, new Rect(0, 0, xs, h));
        dc.DrawRectangle(Shade, null, new Rect(xe, 0, Math.Max(0, w - xe), h));
        dc.DrawLine(EdgePen, new Point(xs, 0), new Point(xs, h));
        dc.DrawLine(EdgePen, new Point(xe, 0), new Point(xe, h));

        if (_playhead >= 0)
            dc.DrawLine(PlayPen, new Point(_playhead * w, 0), new Point(_playhead * w, h));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        double f = Fraction(e);
        _draggingStart = Math.Abs(f - _start) <= Math.Abs(f - _end);
        CaptureMouse();
        EdgeDragged?.Invoke(_draggingStart, f);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) EdgeDragged?.Invoke(_draggingStart, Fraction(e));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    private double Fraction(MouseEventArgs e) =>
        ActualWidth <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
