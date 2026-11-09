using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Inklane;

public sealed partial class MainWindow
{
    private FrameworkElement? _startupBrand;
    private TranslateTransform? _startupBrandOffset;
    private bool _startupBrandRequested, _startupBrandConsumed;
    private bool _startupBrandWatchingPreferences;

    internal void PrepareStartupBrandMotion()
    {
        _startupBrandRequested = !_startupBrandConsumed;
        if (!_startupBrandRequested || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || _startupBrand == null) return;
        WatchStartupPreferences();
        // Prepare the first frame so the mark never jumps backward after appearing.
        _startupBrand.Opacity = .85;
        _startupBrand.RenderTransform = new TranslateTransform(-3, 0);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_startupBrandRequested) return;
        _startupBrandRequested = false;
        BeginStartupBrandMotion(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast);
    }

    private void BeginStartupBrandMotion(bool allowMotion)
    {
        if (_startupBrandConsumed) return;
        _startupBrandConsumed = true;
        if (!allowMotion || !_onHome || _startupBrand == null) { StopStartupBrandMotion(); return; }
        WatchStartupPreferences();
        // Render-only movement: no splash, layout change, input gate, or wait.
        // The ordinary static brand is the base value before and after this clock.
        var duration = TimeSpan.FromMilliseconds(280);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _startupBrandOffset = new TranslateTransform();
        _startupBrand.RenderTransform = _startupBrandOffset;
        var settle = new DoubleAnimation(-3, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var reveal = new DoubleAnimation(.85, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        reveal.Completed += (_, _) => StopStartupBrandMotion();
        _startupBrandOffset.BeginAnimation(TranslateTransform.XProperty, settle);
        _startupBrand.BeginAnimation(OpacityProperty, reveal);
    }

    private void WatchStartupPreferences()
    {
        if (_startupBrandWatchingPreferences) return;
        SystemParameters.StaticPropertyChanged += StartupPreferenceChanged;
        _startupBrandWatchingPreferences = true;
    }

    private void StartupPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)
            && e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        // An accessibility preference changing during arrival ends the reveal,
        // including its prepared first frame. It must never restart afterward.
        void Cancel()
        {
            _startupBrandRequested = false;
            _startupBrandConsumed = true;
            StopStartupBrandMotion();
        }
        if (Dispatcher.CheckAccess()) Cancel();
        else Dispatcher.BeginInvoke(Cancel, DispatcherPriority.Send);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopStartupBrandMotion();
        base.OnClosed(e);
    }

    private void StopStartupBrandMotion()
    {
        if (_startupBrandWatchingPreferences)
        {
            SystemParameters.StaticPropertyChanged -= StartupPreferenceChanged;
            _startupBrandWatchingPreferences = false;
        }
        if (_startupBrand == null) return;
        _startupBrand.BeginAnimation(OpacityProperty, null);
        _startupBrand.Opacity = 1;
        _startupBrandOffset?.BeginAnimation(TranslateTransform.XProperty, null);
        _startupBrand.RenderTransform = Transform.Identity;
        _startupBrandOffset = null;
    }
}
