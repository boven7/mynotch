using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MyNotch;

public partial class NotchWindow : Window
{
    const double CompactW = 200, CompactH = 34, ExpandedW = 460, ExpandedH = 256, MaxRadius = 28;
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    static readonly Color[] AccentChoices =
    [
        Color.FromRgb(0x00, 0x78, 0xD4), Color.FromRgb(0x87, 0x64, 0xB8), Color.FromRgb(0xE3, 0x00, 0x8C),
        Color.FromRgb(0xE8, 0x11, 0x23), Color.FromRgb(0xF7, 0x63, 0x0C), Color.FromRgb(0x10, 0x89, 0x3E),
    ];

    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer _brightDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    GlobalSystemMediaTransportControlsSessionManager? _media;
    GlobalSystemMediaTransportControlsSession? _session;
    Radio? _wifi, _bt;
    Backdrop? _backdrop;
    bool _expanded, _syncing; // _syncing: sliders are being filled from the system; don't write back

    public NotchWindow()
    {
        InitializeComponent();

        foreach (var c in AccentChoices)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), Background = new SolidColorBrush(c) };
            b.Click += (_, _) => { SystemTheme.SetAccent(c); App.RefreshAccent(); };
            Swatches.Children.Add(b);
        }

        _clock.Tick += (_, _) => UpdateClock();
        _collapseDelay.Tick += (_, _) =>
        {
            _collapseDelay.Stop();
            if (WifiPasswordRow.Visibility != Visibility.Visible) Collapse(); // typing a password
        };
        _brightDebounce.Tick += (_, _) =>
        {
            _brightDebounce.Stop();
            int v = (int)BrightSlider.Value;
            Task.Run(() => Brightness.Set(v)); // WMI is slow; keep it off the UI thread
        };
        App.FullscreenChanged += on => Visibility = on ? Visibility.Hidden : Visibility.Visible;
        Pill.SizeChanged += (_, _) => UpdateShape(); // follows every frame of the expand/collapse animation
        App.LookChanged += ApplyLook;

        InitBattery(); // also calls UpdateClock
        _clock.Start();
        _ = InitMediaAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Tool window: hidden from Alt+Tab. No-activate: clicking the notch doesn't steal focus from the current app.
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
        _backdrop = new Backdrop(Pill, BackdropImage);
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        ApplyLook();
    }

    // ---------- Look: style, opacity, position ----------

    void ApplyLook()
    {
        var s = Settings.Current;
        Backdrop.ExcludeFromCapture(this, !s.ShowInScreenshots);
        _backdrop!.Enabled = s.UseBlur;
        Tint.Background = new SolidColorBrush(App.Dark
            ? Color.FromArgb(s.TintAlpha, 0, 0, 0)
            : Color.FromArgb(s.TintAlpha, 0xF7, 0xF7, 0xF7));
        Top = s.Floating ? 8 : 0;
        UpdateShape();
    }

    /// <summary>
    /// Clips Pill to a pill shape (anti-aliased corners, unlike a window region). Attached mode pushes
    /// the top corners outside so the notch sits flush with the screen edge.
    /// </summary>
    void UpdateShape()
    {
        double w = Pill.ActualWidth, h = Pill.ActualHeight;
        if (w == 0) return;
        double r = Math.Min(h / 2, MaxRadius);
        Pill.Clip = new RectangleGeometry(Settings.Current.Floating ? new Rect(0, 0, w, h) : new Rect(0, -r, w, h + r), r, r);
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        CompactClock.Text = now.ToString("HH:mm", En);
        HeaderClock.Text = now.ToString("dddd, d MMMM · HH:mm", En) + _batteryHeader;
    }

    // ---------- Expand / collapse ----------

    void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseDelay.Stop();
        if (!_expanded) Expand();
    }

    void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _collapseDelay.Start();

    void Expand()
    {
        _expanded = true;
        Animate(ExpandedW, ExpandedH);
        Fade(Compact, 0); Fade(Expanded, 1);
        _ = RefreshControlsAsync();
    }

    void Collapse()
    {
        _expanded = false;
        Animate(CompactW, CompactH);
        Fade(Expanded, 0); Fade(Compact, 1);
        ShowSettings(false);
        HideWifi();
        HideBt();
    }

    void Animate(double w, double h)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(260);
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    static void Fade(UIElement el, double to)
    {
        if (to > 0) el.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 220 : 120));
        if (to == 0) anim.Completed += (_, _) => { if (el.Opacity == 0) el.Visibility = Visibility.Collapsed; };
        el.BeginAnimation(OpacityProperty, anim);
    }

    // ---------- Settings panel ----------

    // Same as quitting from the dock: OnExit restores the taskbar.
    void Power_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    void Gear_Click(object sender, RoutedEventArgs e) => ShowSettings(SettingsPanel.Visibility != Visibility.Visible);

    void ShowSettings(bool show)
    {
        if (show) { HideWifi(); HideBt(); }
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MainPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        GearBtn.Content = show ? "\uE711" : "\uE713"; // close / gear
        if (show) LoadSettingsUi();
    }

    void LoadSettingsUi()
    {
        var s = Settings.Current;
        _syncing = true;
        StyleSolid.IsChecked = s.Style == IslandStyle.Solid;
        StyleTransparent.IsChecked = s.Style == IslandStyle.Transparent;
        StyleBlur.IsChecked = s.Style == IslandStyle.Blur;
        OpacitySlider.Value = s.Opacity;
        OpacitySlider.IsEnabled = s.Style != IslandStyle.Solid;
        PosFloat.IsChecked = s.Floating;
        PosAttach.IsChecked = !s.Floating;
        MagnifyChip.IsChecked = s.Magnify;
        ShotChip.IsChecked = s.ShowInScreenshots;
        IconsChip.IsChecked = s.CustomIcons;
        WinAccent.IsChecked = SystemTheme.AccentOnWindows;
        WinTransparency.IsChecked = SystemTheme.Transparency;
        _syncing = false;
    }

    void Change(Action<Settings> edit)
    {
        edit(Settings.Current);
        Settings.Save(); // triggers ApplyLook in notch & dock
        LoadSettingsUi();
    }

    void Style_Click(object sender, RoutedEventArgs e) =>
        Change(s => s.Style = Enum.Parse<IslandStyle>((string)((FrameworkElement)sender).Tag));

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) Change(s => s.Opacity = (int)e.NewValue);
    }

    void PosFloat_Click(object sender, RoutedEventArgs e) => Change(s => s.Floating = true);
    void PosAttach_Click(object sender, RoutedEventArgs e) => Change(s => s.Floating = false);
    void Magnify_Click(object sender, RoutedEventArgs e) => Change(s => s.Magnify = MagnifyChip.IsChecked == true);
    async void Icons_Click(object sender, RoutedEventArgs e)
    {
        bool on = IconsChip.IsChecked == true;
        IconsChip.IsEnabled = false;
        // UAC + Explorer restart: run off the UI thread
        bool ok = await Task.Run(() => SystemIconInstaller.Set(on));
        IconsChip.IsEnabled = true;
        if (ok) Change(s => s.CustomIcons = on);
        else LoadSettingsUi(); // UAC declined: reset the chip
    }

    void Shot_Click(object sender, RoutedEventArgs e) => Change(s => s.ShowInScreenshots = ShotChip.IsChecked == true);

    void WinAccent_Click(object sender, RoutedEventArgs e) => SystemTheme.AccentOnWindows = WinAccent.IsChecked == true;
    void WinTransparency_Click(object sender, RoutedEventArgs e) => SystemTheme.Transparency = WinTransparency.IsChecked == true;

    // ---------- Quick toggles ----------

    async Task RefreshControlsAsync()
    {
        _syncing = true;
        VolumeSlider.Value = (Audio.Volume ?? 0) * 100;
        MuteBtn.Content = Audio.Muted ? "\uE74F" : "\uE767";
        DarkToggle.IsChecked = SystemTheme.IsDark;
        _syncing = false;

        _wifi ??= await Radios.FindAsync(RadioKind.WiFi);
        _bt ??= await Radios.FindAsync(RadioKind.Bluetooth);
        WifiToggle.IsEnabled = _wifi != null;
        WifiToggle.IsChecked = _wifi?.State == RadioState.On;
        BtToggle.IsEnabled = _bt != null;
        BtToggle.IsChecked = _bt?.State == RadioState.On;

        var bright = await Task.Run(Brightness.Get);
        _syncing = true;
        BrightSlider.IsEnabled = BrightIcon.IsEnabled = bright != null; // external monitors: not supported
        BrightSlider.Value = bright ?? 0;
        _syncing = false;
    }

    void Dark_Click(object sender, RoutedEventArgs e) => SystemTheme.SetDark(DarkToggle.IsChecked == true);

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) Audio.Volume = e.NewValue / 100;
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        Audio.Muted = !Audio.Muted;
        MuteBtn.Content = Audio.Muted ? "\uE74F" : "\uE767";
    }

    void Bright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _brightDebounce.Stop();
        _brightDebounce.Start();
    }

    // ---------- Media (Spotify, browsers, etc. via Windows media controls) ----------

    async Task InitMediaAsync()
    {
        _media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _media.CurrentSessionChanged += (_, _) => Dispatcher.InvokeAsync(AttachSession);
        AttachSession();
    }

    void AttachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaProps;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = _media?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaProps;
            _session.PlaybackInfoChanged += OnPlayback;
        }
        _ = UpdateMediaAsync();
    }

    void OnMediaProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) => Dispatcher.InvokeAsync(UpdateMediaAsync);
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) => Dispatcher.InvokeAsync(UpdateMediaAsync);

    async Task UpdateMediaAsync()
    {
        var s = _session;
        try
        {
            var props = s == null ? null : await s.TryGetMediaPropertiesAsync();
            bool playing = s?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool hasTrack = !string.IsNullOrEmpty(props?.Title);

            TrackTitle.Text = hasTrack ? props!.Title : "Nothing playing";
            TrackArtist.Text = props?.Artist ?? "";
            PlayBtn.Content = playing ? "\uE769" : "\uE768";
            CompactTrack.Text = playing && hasTrack ? props!.Title : "";
            PlayingDot.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            if (props?.Thumbnail is { } thumb) Art.Background = await LoadArtAsync(thumb);
            else Art.SetResourceReference(Border.BackgroundProperty, "IslandSoft");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The player can close its session mid-update; wait for the next event.
        }
    }

    static async Task<Brush> LoadArtAsync(IRandomAccessStreamReference thumb)
    {
        using var ras = await thumb.OpenReadAsync();
        var ms = new MemoryStream();
        await ras.AsStreamForRead().CopyToAsync(ms);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 112;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
    }

    void Prev_Click(object sender, RoutedEventArgs e) => _ = _session?.TrySkipPreviousAsync();
    void Play_Click(object sender, RoutedEventArgs e) => _ = _session?.TryTogglePlayPauseAsync();
    void Next_Click(object sender, RoutedEventArgs e) => _ = _session?.TrySkipNextAsync();
}
