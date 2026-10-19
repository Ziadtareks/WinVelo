using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace WinVelo.App.Controls;

public class ResponsiveUniformGrid : UniformGrid
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(
            nameof(MinItemWidth),
            typeof(double),
            typeof(ResponsiveUniformGrid),
            new FrameworkPropertyMetadata(280.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width) && constraint.Width > 0 && MinItemWidth > 0)
        {
            int cols = Math.Max(1, (int)Math.Floor(constraint.Width / MinItemWidth));
            if (Columns != cols)
            {
                Columns = cols;
            }
        }
        return base.MeasureOverride(constraint);
    }
}
