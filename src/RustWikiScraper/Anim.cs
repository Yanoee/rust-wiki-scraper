using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustWikiScraper;

public static class Anim
{
    public static readonly DependencyProperty FloatProperty =
        DependencyProperty.RegisterAttached("Float", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, _) => StartFloat(d)));
    public static readonly DependencyProperty FloatAmountProperty =
        DependencyProperty.RegisterAttached("FloatAmount", typeof(double), typeof(Anim), new PropertyMetadata(5.0, (d, _) => StartFloat(d)));
    public static readonly DependencyProperty FloatSecondsProperty =
        DependencyProperty.RegisterAttached("FloatSeconds", typeof(double), typeof(Anim), new PropertyMetadata(1.8, (d, _) => StartFloat(d)));
    public static readonly DependencyProperty JellyProperty =
        DependencyProperty.RegisterAttached("Jelly", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnJelly));
    public static readonly DependencyProperty SpinProperty =
        DependencyProperty.RegisterAttached("Spin", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnSpin));

    public static void SetFloat(DependencyObject d, bool v) => d.SetValue(FloatProperty, v);
    public static bool GetFloat(DependencyObject d) => (bool)d.GetValue(FloatProperty);
    public static void SetFloatAmount(DependencyObject d, double v) => d.SetValue(FloatAmountProperty, v);
    public static double GetFloatAmount(DependencyObject d) => (double)d.GetValue(FloatAmountProperty);
    public static void SetFloatSeconds(DependencyObject d, double v) => d.SetValue(FloatSecondsProperty, v);
    public static double GetFloatSeconds(DependencyObject d) => (double)d.GetValue(FloatSecondsProperty);
    public static void SetJelly(DependencyObject d, bool v) => d.SetValue(JellyProperty, v);
    public static bool GetJelly(DependencyObject d) => (bool)d.GetValue(JellyProperty);
    public static void SetSpin(DependencyObject d, bool v) => d.SetValue(SpinProperty, v);
    public static bool GetSpin(DependencyObject d) => (bool)d.GetValue(SpinProperty);

    static (ScaleTransform Scale, RotateTransform Rotate, TranslateTransform Move) Transforms(UIElement e)
    {
        if (e.RenderTransform is TransformGroup { Children: [ScaleTransform s, RotateTransform r, TranslateTransform t] })
            return (s, r, t);
        var group = new TransformGroup();
        var parts = (new ScaleTransform(), new RotateTransform(), new TranslateTransform());
        group.Children.Add(parts.Item1);
        group.Children.Add(parts.Item2);
        group.Children.Add(parts.Item3);
        e.RenderTransform = group;
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        return parts;
    }

    static void StartFloat(DependencyObject d)
    {
        if (d is not UIElement e) return;
        var move = Transforms(e).Move;
        if (!GetFloat(d)) { move.BeginAnimation(TranslateTransform.YProperty, null); return; }
        var half = GetFloatSeconds(d);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -GetFloatAmount(d), TimeSpan.FromSeconds(half))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromSeconds(-Random.Shared.NextDouble() * half * 2),
        });
    }

    static void OnJelly(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is not UIElement e) return;
        e.PreviewMouseLeftButtonDown -= Wobble;
        if ((bool)args.NewValue) e.PreviewMouseLeftButtonDown += Wobble;
    }

    static void Wobble(object sender, MouseButtonEventArgs _)
    {
        if (sender is not UIElement { IsEnabled: true } e) return;
        var scale = Transforms(e).Scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(1, 0.92, 1.06, 0.97, 1.01, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(1, 1.06, 0.94, 1.03, 0.99, 1));
    }

    static DoubleAnimationUsingKeyFrames Keys(params double[] values)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        for (var i = 0; i < values.Length; i++)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 85)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }

    static void OnSpin(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is not UIElement e) return;
        var rotate = Transforms(e).Rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, (bool)args.NewValue
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
}
