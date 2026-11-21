using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace AC_Controller;

public sealed class TemperatureDial : FrameworkElement
{
    private const double DesignWidth = 260;
    private const double DesignHeight = 180;
    private const double Radius = 94;
    private const double StartAngle = 150;
    private const double SweepAngle = 240;
    private const int Minimum = 16;
    private const int Maximum = 30;
    private static readonly Point Center = new(130, 105);
    private static readonly Brush TextInk = MakeBrush(0x13, 0x2D, 0x3A);
    private static readonly Brush SecondaryInk = MakeBrush(0x46, 0x60, 0x67);
    private static readonly Brush TrackInk = MakeBrush(0xDC, 0xE5, 0xE5);
    private static readonly Brush TickInk = MakeBrush(0x88, 0x9C, 0x9C);
    private static readonly Brush AccentInk = MakeBrush(0x00, 0x84, 0x75);
    private static readonly FontFamily Family = new("Segoe UI Variable Display, Segoe UI");
    private static readonly Geometry Track = MakeArc(1);
    private int? _celsius;
    private bool _powered;

    public TemperatureDial()
    {
        Focusable = false;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Temperatura indisponível. Sem conexão.");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public void SetReading(int? celsius, bool powered)
    {
        VerifyAccess();
        if (_celsius == celsius && _powered == powered) return;
        _celsius = celsius;
        _powered = powered;
        var name = celsius is null ? "Temperatura indisponível. Sem conexão."
            : powered ? $"Temperatura definida em {celsius.Value} graus Celsius."
            : "Temperatura indisponível. Aparelho desligado.";
        if (AutomationProperties.GetName(this) != name)
        {
            AutomationProperties.SetName(this, name);
            UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        InvalidateVisual();
    }

    internal static double GetProgress(int celsius) =>
        (Math.Clamp(celsius, Minimum, Maximum) - Minimum) / (double)(Maximum - Minimum);

    protected override AutomationPeer OnCreateAutomationPeer() => new DialPeer(this);

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(DesignWidth, availableSize.Width), Math.Min(DesignHeight, availableSize.Height));

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var scale = Math.Min(RenderSize.Width / DesignWidth, RenderSize.Height / DesignHeight);
        if (scale <= 0) return;
        var left = (RenderSize.Width - DesignWidth * scale) / 2;
        var top = (RenderSize.Height - DesignHeight * scale) / 2;
        drawing.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));

        var highContrast = SystemParameters.HighContrast;
        var ink = highContrast ? SystemColors.WindowTextBrush : TextInk;
        var secondary = highContrast ? ink : SecondaryInk;
        var track = highContrast ? SystemColors.GrayTextBrush : TrackInk;
        var tick = highContrast ? ink : TickInk;
        var accent = highContrast ? SystemColors.HighlightBrush : AccentInk;
        var active = _powered && _celsius.HasValue;
        drawing.DrawGeometry(null, MakeArcPen(track), Track);
        var tickPen = new Pen(tick, 1.3);
        for (var value = Minimum; value <= Maximum; value++)
        {
            var progress = GetProgress(value);
            var tickLength = (value - Minimum) % 2 == 0 ? 5 : 3;
            drawing.DrawLine(tickPen, OnCircle(progress, Radius - 12),
                OnCircle(progress, Radius - 12 - tickLength));
        }
        if (active)
        {
            var progress = GetProgress(_celsius!.Value);
            if (progress > 0) drawing.DrawGeometry(null, MakeArcPen(accent), MakeArc(progress));
            var knob = OnCircle(progress, Radius);
            drawing.DrawEllipse(accent, null, knob, 7, 7);
            drawing.DrawEllipse(highContrast ? SystemColors.WindowBrush : Brushes.White, null, knob, 2.8, 2.8);
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawValue(drawing, active ? _celsius : null, ink, dpi);
        var status = active ? "Temperatura definida" : _celsius.HasValue ? "Aparelho desligado" : "Sem conexão";
        DrawCentered(drawing, Format(status, 13, FontWeights.Medium, secondary, dpi), Center.X, 125);
        DrawCentered(drawing, Format("16°", 12, FontWeights.SemiBold, secondary, dpi), OnCircle(0, Radius).X, 162);
        DrawCentered(drawing, Format("30°", 12, FontWeights.SemiBold, secondary, dpi), OnCircle(1, Radius).X, 162);
        drawing.Pop();
    }

    private static void DrawValue(DrawingContext drawing, int? value, Brush ink, double dpi)
    {
        var number = Format(value?.ToString(CultureInfo.CurrentCulture) ?? "—", 57, FontWeights.SemiBold, ink, dpi);
        if (value is null)
        {
            DrawCentered(drawing, number, Center.X, 63);
            return;
        }

        var unit = Format("°C", 21, FontWeights.Medium, ink, dpi);
        const double unitGap = 2;
        var width = number.Width + unitGap + unit.Width;
        var scale = Math.Min(1, 143 / width);
        var left = Center.X - width * scale / 2;
        var top = 98 - number.Height * scale / 2;
        drawing.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
        drawing.DrawText(number, new Point(0, 0));
        drawing.DrawText(unit, new Point(number.Width + unitGap, 12));
        drawing.Pop();
    }

    private static void DrawCentered(DrawingContext drawing, FormattedText text, double x, double top) =>
        drawing.DrawText(text, new Point(x - text.Width / 2, top));

    private static FormattedText Format(string text, double size, FontWeight weight, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi);

    private static Point OnCircle(double progress, double radius)
    {
        var angle = (StartAngle + SweepAngle * progress) * Math.PI / 180;
        return new Point(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));
    }

    private static StreamGeometry MakeArc(double progress)
    {
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(OnCircle(0, Radius), false, false);
            context.ArcTo(OnCircle(progress, Radius), new Size(Radius, Radius), 0,
                SweepAngle * progress > 180, SweepDirection.Clockwise, true, true);
        }
        arc.Freeze();
        return arc;
    }

    private static Pen MakeArcPen(Brush brush) => new(brush, 10)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };

    private static Brush MakeBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private sealed class DialPeer(TemperatureDial dial) : FrameworkElementAutomationPeer(dial)
    {
        protected override string GetClassNameCore() => nameof(TemperatureDial);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
