using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RadialConvert.UI;

/// <summary>
/// Borderless, transparent, topmost overlay centred on a screen point, with a captured backdrop
/// for the glass effect and a springy open / quick fade close.
/// </summary>
internal abstract class GlassWindow : Window
{
    /// <summary>Extra backdrop captured around the window so the blur has real pixels at the edges.</summary>
    protected const double BackdropMargin = 18;

    readonly Native.RECT _bounds;
    readonly bool _activate;
    bool _closing;

    protected GlassWindow(double width, double height, Native.POINT anchor, bool activate, GlassBackdrop? backdrop)
    {
        _activate = activate;
        Scale = Native.ScaleAt(anchor);

        int pw = (int)Math.Round(width * Scale), ph = (int)Math.Round(height * Scale);
        Native.RECT work = Native.WorkAreaAt(anchor);
        int x = Math.Clamp(anchor.X - pw / 2, work.Left, Math.Max(work.Left, work.Right - pw));
        int y = Math.Clamp(anchor.Y - ph / 2, work.Top, Math.Max(work.Top, work.Bottom - ph));
        _bounds = new Native.RECT { Left = x, Top = y, Right = x + pw, Bottom = y + ph };
        CenterOnScreen = new Native.POINT { X = x + pw / 2, Y = y + ph / 2 };

        int margin = (int)Math.Round(BackdropMargin * Scale);
        Backdrop = backdrop ?? GlassBackdrop.Capture(x - margin, y - margin, pw + margin * 2, ph + margin * 2);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = activate;
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Radial Convert";

        Root = new Grid
        {
            Width = width, Height = height, Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.82, 0.82),
        };
        Content = Root;
        Loaded += (_, _) => AnimateIn();
    }

    /// <summary>Physical-pixel centre of the window on screen.</summary>
    public Native.POINT CenterOnScreen { get; }

    protected double Scale { get; }

    protected GlassBackdrop Backdrop { get; }

    protected Grid Root { get; }

    protected bool IsClosing => _closing;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW |
                                (_activate ? 0 : Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT));
        // Twice: the first move may land on a monitor with another DPI, which makes WPF resize the window.
        Native.Move(hwnd, _bounds.Left, _bounds.Top);
        Native.Move(hwnd, _bounds.Left, _bounds.Top);
        if (_activate)
            Native.ForceForeground(hwnd);
    }

    void AnimateIn()
    {
        var scale = (ScaleTransform)Root.RenderTransform;
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = spring });
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var duration = TimeSpan.FromMilliseconds(110);
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) => Close();
        var scale = (ScaleTransform)Root.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, duration));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Renders the fully-open overlay over its (unblurred) backdrop to a PNG, for design checks.</summary>
    public void RenderPreview(string pngPath)
    {
        Content = null;
        Root.Opacity = 1;
        Root.RenderTransform = Transform.Identity;

        var backdrop = new Image
        {
            Source = Backdrop.Image, Stretch = Stretch.Fill,
            Width = Width + BackdropMargin * 2, Height = Height + BackdropMargin * 2,
        };
        Canvas.SetLeft(backdrop, -BackdropMargin);
        Canvas.SetTop(backdrop, -BackdropMargin);
        var composite = new Grid
        {
            Width = Width, Height = Height, ClipToBounds = true,
            Children = { new Canvas { Children = { backdrop } }, Root },
        };
        composite.Measure(new Size(Width, Height));
        composite.Arrange(new Rect(0, 0, Width, Height));

        var bitmap = new RenderTargetBitmap((int)(Width * 2), (int)(Height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(composite);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(pngPath);
        encoder.Save(stream);
    }
}
