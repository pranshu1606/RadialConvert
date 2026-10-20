using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RadialConvert.Conversion;

namespace RadialConvert.UI;

/// <summary>Glass card asking "Convert to PDF?" — Enter/Convert confirms, Esc/Cancel/clicking away cancels.</summary>
internal sealed class ConfirmWindow : GlassWindow
{
    const double CardWidth = 300, CardHeight = 128, Pad = 24;
    const double WindowWidth = CardWidth + Pad * 2, WindowHeight = CardHeight + Pad * 2;

    bool _done;

    public ConfirmWindow(IReadOnlyList<string> files, ConversionOption option, Native.POINT anchor, GlassBackdrop? backdrop = null)
        : base(WindowWidth, WindowHeight, anchor, activate: true, backdrop)
    {
        bool dark = Backdrop.IsDark;
        var card = new RectangleGeometry(new Rect(Pad, Pad, CardWidth, CardHeight), 26, 26);
        Root.Children.Add(Glass.Surface(card, WindowWidth, WindowHeight, Backdrop, BackdropMargin, tint: 0.06, blur: 20));

        string subject = files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} files";
        var header = new DockPanel { LastChildFill = true };
        var logo = new Image { Source = AppIcon.Logo(), Width = 38, Height = 38, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        header.Children.Add(new StackPanel
        {
            Effect = Glass.TextHalo(dark),
            Children =
            {
                new TextBlock
                {
                    Text = $"Convert to {option.Label}?", Foreground = Glass.TextBrush(dark),
                    FontSize = 16, FontWeight = FontWeights.SemiBold,
                },
                new TextBlock
                {
                    Text = subject, Foreground = Glass.MutedBrush(dark), FontSize = 12, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = files.Count == 1 ? files[0] : null,
                },
            },
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { Button("Cancel", primary: false, dark, Cancel), Button("Convert", primary: true, dark, Confirm) },
        };

        Root.Children.Add(new Grid
        {
            Margin = new Thickness(Pad + 18, Pad + 16, Pad + 16, Pad + 14),
            Children = { header, buttons },
        });

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) Confirm();
            else if (e.Key == Key.Escape) Cancel();
        };
        Deactivated += (_, _) => Cancel();
    }

    public event Action? Confirmed;

    void Confirm()
    {
        if (_done) return;
        _done = true;
        CloseAnimated();
        Confirmed?.Invoke();
    }

    void Cancel()
    {
        if (_done) return;
        _done = true;
        CloseAnimated();
    }

    static Border Button(string text, bool primary, bool dark, Action onClick)
    {
        var button = new Border
        {
            CornerRadius = new CornerRadius(15), Padding = new Thickness(18, 7, 18, 8), Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            BorderBrush = Glass.Frozen(new SolidColorBrush(Color.FromArgb(primary ? (byte)0x90 : (byte)0x70, 0xFF, 0xFF, 0xFF))),
            Background = primary
                ? Glass.AccentFill(0xF0)
                : Glass.Frozen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x26 : (byte)0x66, 0xFF, 0xFF, 0xFF))),
            Child = new TextBlock
            {
                Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = primary ? Brushes.White : Glass.TextBrush(dark),
            },
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.85;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }
}
