using Avalonia;
using Avalonia.Controls;

namespace Kiriha.Utils.UI;

/// <summary>
/// AttachedProperty-обёртка над <see cref="SmoothScrollBehavior"/>:
/// <c>u:SmoothScroll.IsEnabled="True"</c> на любом ScrollViewer.
/// </summary>
public static class SmoothScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsEnabled", typeof(SmoothScroll));

    public static readonly AttachedProperty<double> WheelMultiplierProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>(
            "WheelMultiplier", typeof(SmoothScroll), 110.0);

    public static readonly AttachedProperty<double> SmoothingTimeMsProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>(
            "SmoothingTimeMs", typeof(SmoothScroll), 110.0);

    private static readonly AttachedProperty<SmoothScrollBehavior?> BehaviorProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, SmoothScrollBehavior?>(
            "Behavior", typeof(SmoothScroll));

    public static bool GetIsEnabled(ScrollViewer sv) => sv.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer sv, bool value) => sv.SetValue(IsEnabledProperty, value);

    public static double GetWheelMultiplier(ScrollViewer sv) => sv.GetValue(WheelMultiplierProperty);
    public static void SetWheelMultiplier(ScrollViewer sv, double value) => sv.SetValue(WheelMultiplierProperty, value);

    public static double GetSmoothingTimeMs(ScrollViewer sv) => sv.GetValue(SmoothingTimeMsProperty);
    public static void SetSmoothingTimeMs(ScrollViewer sv, double value) => sv.SetValue(SmoothingTimeMsProperty, value);

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            var enabled = e.NewValue is true;
            var existing = sv.GetValue(BehaviorProperty);

            if (enabled && existing is null)
            {
                var b = SmoothScrollBehavior.Attach(sv);
                b.WheelMultiplier = sv.GetValue(WheelMultiplierProperty);
                b.SmoothingTime = TimeSpan.FromMilliseconds(sv.GetValue(SmoothingTimeMsProperty));
                sv.SetValue(BehaviorProperty, b);
            }
            else if (existing != null)
            {
                existing.Enabled = enabled;
            }
        });

        WheelMultiplierProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            var b = sv.GetValue(BehaviorProperty);
            if (b != null && e.NewValue is double val)
            {
                b.WheelMultiplier = val;
            }
        });

        SmoothingTimeMsProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            var b = sv.GetValue(BehaviorProperty);
            if (b != null && e.NewValue is double val)
            {
                b.SmoothingTime = TimeSpan.FromMilliseconds(val);
            }
        });
    }
}
