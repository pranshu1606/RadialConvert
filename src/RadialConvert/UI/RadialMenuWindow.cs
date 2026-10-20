using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using RadialConvert.Conversion;

namespace RadialConvert.UI;

/// <summary>
/// Small glass ring of conversion choices centred on the cursor. The option in the direction of the
/// mouse (outside a small dead zone) is highlighted by a liquid "droplet" that glides between slices.
/// <para>Hold mode (Ctrl held): click-through and never takes focus; the caller reads
/// <see cref="Selected"/> when Ctrl is released.</para>
/// <para>Click mode (tray "Convert a file…"): click to choose, Esc or clicking away cancels.</para>
/// </summary>
internal sealed class RadialMenuWindow : GlassWindow
{
    const double Radius = 100, HubRadius = 38, Pad = 22, Size = (Radius + Pad) * 2, C = Size / 2;
    const double DeadZone = 24;

    readonly IReadOnlyList<ConversionOption> _options;
    readonly bool _holdMode;
    readonly double _sweep;
    readonly RotateTransform _rotation = new(0, C, C);
    readonly FrameworkElement _highlight;
    readonly List<TextBlock> _labels = new();
    readonly TextBlock _hubTitle, _hubHint;
    readonly Brush _text, _muted;
    int _index = -1;
    double _angle, _highlightOpacity;

    public RadialMenuWindow(IReadOnlyList<string> files, IReadOnlyList<ConversionOption> options, bool holdMode,
                            Native.POINT? anchor = null, GlassBackdrop? backdrop = null)
        : base(Size, Size, anchor ?? Native.CursorPosition(), activate: !holdMode, backdrop)
    {
        Files = files;
        _options = options;
        _holdMode = holdMode;
        _sweep = 360.0 / options.Count;

        bool dark = Backdrop.IsDark;
        _text = Glass.TextBrush(dark);
        _muted = Glass.MutedBrush(dark);

        Root.Children.Add(Glass.Surface(new EllipseGeometry(new Point(C, C), Radius, Radius), Size, Size, Backdrop, BackdropMargin));

        var overlay = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false };
        if (options.Count > 1)
        {
            var separator = Glass.Frozen(new SolidColorBrush(dark ? Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0)));
            for (int i = 0; i < options.Count; i++)
            {
                double a = i * _sweep + _sweep / 2;
                Point from = PointAt(a, HubRadius + 8), to = PointAt(a, Radius - 10);
                overlay.Children.Add(new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = separator, StrokeThickness = 1,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                });
            }
        }

        _highlight = BuildHighlight();
        overlay.Children.Add(_highlight);

        for (int i = 0; i < options.Count; i++)
        {
            var label = new TextBlock
            {
                Text = options[i].Label, Foreground = _text, FontWeight = FontWeights.SemiBold,
                FontSize = options[i].Label.Length > 5 ? 10.5 : 12.5, Effect = Glass.TextHalo(dark),
            };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Point at = PointAt(i * _sweep, (HubRadius + Radius) / 2 + 3);
            Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, at.Y - label.DesiredSize.Height / 2);
            overlay.Children.Add(label);
            _labels.Add(label);
        }
        Root.Children.Add(overlay);

        // The hub is its own, slightly frostier bubble of glass.
        Root.Children.Add(Glass.Surface(new EllipseGeometry(new Point(C, C), HubRadius, HubRadius), Size, Size, Backdrop,
                                        BackdropMargin, tint: 0.08, blur: 22));
        _hubTitle = new TextBlock
        {
            Text = files.Count == 1 ? System.IO.Path.GetExtension(files[0]).TrimStart('.').ToUpperInvariant() : $"{files.Count} files",
            Foreground = _text, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
        };
        _hubHint = new TextBlock
        {
            Foreground = _muted, FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, MaxWidth = HubRadius * 2 - 12, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Root.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Effect = Glass.TextHalo(dark), Children = { _hubTitle, _hubHint },
        });
        UpdateHub();

        System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) => System.Windows.Media.CompositionTarget.Rendering -= OnFrame;

        if (!holdMode)
        {
            MouseLeftButtonUp += (_, _) => { if (Selected is { } option) { Chosen?.Invoke(option); CloseAnimated(); } };
            MouseRightButtonUp += (_, _) => CloseAnimated();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
            Deactivated += (_, _) => CloseAnimated();
        }
    }

    public IReadOnlyList<string> Files { get; }

    /// <summary>The option the mouse currently points at, or null inside the dead zone.</summary>
    public ConversionOption? Selected => _index >= 0 ? _options[_index] : null;

    /// <summary>Click mode only.</summary>
    public event Action<ConversionOption>? Chosen;

    /// <summary>For previews: show the highlight resting on an option.</summary>
    public void PreviewSelect(int index)
    {
        SetIndex(index);
        _angle = index * _sweep;
        _rotation.Angle = _angle;
        _highlightOpacity = 1;
        _highlight.Opacity = 1;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (IsClosing) return;

        Native.POINT p = Native.CursorPosition();
        double dx = (p.X - CenterOnScreen.X) / Scale, dy = (p.Y - CenterOnScreen.Y) / Scale;
        int index = -1;
        if (Math.Sqrt(dx * dx + dy * dy) >= DeadZone)
        {
            double angle = Math.Atan2(dx, -dy) * 180 / Math.PI; // 0° = up, clockwise
            if (angle < 0) angle += 360;
            index = (int)((angle + _sweep / 2) % 360 / _sweep) % _options.Count;
        }
        if (index != _index)
            SetIndex(index);

        // Glide toward the target slice the short way round; snap when appearing from nothing.
        if (_index >= 0)
        {
            double target = _index * _sweep;
            if (_highlightOpacity < 0.05)
                _angle = target;
            else
                _angle += (((target - _angle) % 360 + 540) % 360 - 180) * 0.32;
            _rotation.Angle = _angle;
        }
        _highlightOpacity += ((_index >= 0 ? 1 : 0) - _highlightOpacity) * 0.3;
        _highlight.Opacity = _highlightOpacity;
    }

    void SetIndex(int index)
    {
        _index = index;
        for (int i = 0; i < _labels.Count; i++)
        {
            bool on = i == index;
            _labels[i].Foreground = on ? Brushes.White : _text;
            _labels[i].FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold;
        }
        UpdateHub();
    }

    void UpdateHub() =>
        _hubHint.Text = _index >= 0 ? $"→ {_options[_index].Label}"
                      : _holdMode ? "drag to pick" : "pick a format";

    /// <summary>A rounded ring segment centred on 0° (straight up); rotated to the selected slice.</summary>
    FrameworkElement BuildHighlight()
    {
        const double round = 8; // corner rounding, applied by widening with a round-joined pen
        double inner = HubRadius + 5 + round / 2, outer = Radius - 5 - round / 2;
        double middle = (inner + outer) / 2;
        double half = Math.Min(179.5, _sweep / 2 - 1.5 - round / 2 / middle * 180 / Math.PI);

        Geometry core = RingSegment(-half, half, inner, outer);
        Geometry shape = Geometry.Combine(core, core.GetWidenedPathGeometry(
            new Pen(Brushes.Black, round) { LineJoin = PenLineJoin.Round }), GeometryCombineMode.Union, null);
        shape.Freeze();

        return new Grid
        {
            Width = Size, Height = Size, Opacity = 0, RenderTransform = _rotation,
            Effect = new DropShadowEffect { Color = Glass.Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.65 },
            Children =
            {
                new Path { Data = shape, Fill = Glass.AccentFill(0xE6) },
                new Path
                {
                    Data = shape, StrokeThickness = 1.2, Stroke = Glass.Rim(),
                    Fill = Glass.Frozen(new LinearGradientBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90)),
                },
            },
        };
    }

    static Geometry RingSegment(double a0, double a1, double inner, double outer)
    {
        bool large = a1 - a0 > 180;
        var figure = new PathFigure { StartPoint = PointAt(a0, outer), IsClosed = true };
        figure.Segments.Add(new ArcSegment(PointAt(a1, outer), new System.Windows.Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(PointAt(a1, inner), true));
        figure.Segments.Add(new ArcSegment(PointAt(a0, inner), new System.Windows.Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        return new PathGeometry(new[] { figure });
    }

    /// <summary>0° is straight up, angles grow clockwise.</summary>
    static Point PointAt(double degrees, double radius)
    {
        double rad = degrees * Math.PI / 180;
        return new Point(C + radius * Math.Sin(rad), C - radius * Math.Cos(rad));
    }
}
