using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MyNotch;

/// <summary>
/// Custom glass blur: the window is excluded from capture, the screen behind <c>host</c> is captured
/// ~15x per second and blurred in WPF. The host's Clip cuts the result, so the shape is a perfect
/// pill/card (DWM effects are always rectangular).
/// </summary>
sealed class Backdrop
{
    const double Pad = 32; // DIPs captured beyond the edges so the blur doesn't fade at the borders
    readonly FrameworkElement _host;
    readonly Image _image;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(66) };

    public Backdrop(FrameworkElement host, Image image)
    {
        _host = host;
        _image = image;
        _image.Stretch = Stretch.Fill;
        Canvas.SetLeft(_image, -Pad);
        Canvas.SetTop(_image, -Pad);
        _image.Effect = new BlurEffect { Radius = 30, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _timer.Tick += (_, _) => Capture();
    }

    /// <summary>Excludes the window from screenshots/recordings; otherwise the blur would capture itself.</summary>
    public static bool ExcludeFromCapture(Window w, bool exclude) =>
        Native.SetWindowDisplayAffinity(new WindowInteropHelper(w).Handle, exclude ? 0x11u /*WDA_EXCLUDEFROMCAPTURE*/ : 0u);

    public bool Enabled
    {
        set
        {
            _image.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) { Capture(); _timer.Start(); }
            else { _timer.Stop(); _image.Source = null; }
        }
    }

    void Capture()
    {
        if (!_host.IsVisible || _host.ActualWidth == 0 || PresentationSource.FromVisual(_host) == null) return;
        var tl = _host.PointToScreen(new Point(-Pad, -Pad)); // physical pixels
        var br = _host.PointToScreen(new Point(_host.ActualWidth + Pad, _host.ActualHeight + Pad));
        _image.Source = Pixels.CaptureScreen((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
        _image.Width = _host.ActualWidth + 2 * Pad;
        _image.Height = _host.ActualHeight + 2 * Pad;
    }
}
