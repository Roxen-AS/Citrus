using Citrus.Core;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Windows.Presentation;
using Citrus.Core.Presentation;
using System.Globalization;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows;

/// <summary>One retained target. Hover changes brushes/opacity only; geometry and layout stay fixed.</summary>
public sealed class WheelPetal : Canvas
{
    private readonly ShapePath selectedSurface, icon, specular;
    private readonly StreamGeometry outline;
    private readonly TextBlock label;
    private readonly bool formatLabel;
    public WheelPetal(OperationDescriptor operation, int index, int count)
    {
        Width = WheelGeometry.Width; Height = WheelGeometry.Width;
        IsHitTestVisible = false;
        var points = WheelGeometry.Petal(index,count);
        var shape = new StreamGeometry();
        using (var context = shape.Open())
        {
            context.BeginFigure(new Point(points[0].X,points[0].Y),true,true);
            context.PolyLineTo(points.Skip(1).Select(p => new Point(p.X,p.Y)).ToArray(),true,false);
        }
        shape.Freeze();
        outline = shape;
        var background = new ShapePath { Data = shape, StrokeThickness = 1 };
        background.SetResourceReference(ShapePath.FillProperty,"PetalBackground");
        background.SetResourceReference(ShapePath.StrokeProperty,"BorderBrush");
        selectedSurface = new ShapePath { Data = shape, Opacity = 0, StrokeThickness = 1 };
        selectedSurface.SetResourceReference(ShapePath.FillProperty,"SelectedPetal");
        selectedSurface.SetResourceReference(ShapePath.StrokeProperty,"BorderBrush");
        Children.Add(background); Children.Add(selectedSurface);
        specular = new ShapePath { Data = shape, Stroke = Brushes.White, StrokeThickness = .75, Opacity = 0 };
        Children.Add(specular);
        Unloaded += (_, _) => CancelEmphasis();
        formatLabel = operation.Mode == OperationMode.Convert && !string.IsNullOrEmpty(operation.OutputExtension);
        var center = WheelGeometry.At(122,index*360d/count);
        icon = new ShapePath { Data = (Geometry)Application.Current.FindResource(IconKey(operation)), Width=24, Height=24,
            Stretch=Stretch.Uniform, StrokeThickness=1.6, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round, StrokeLineJoin=PenLineJoin.Round };
        label = new TextBlock { Text=WheelDisplayLabels.Petal(operation), Width=72, FontSize=formatLabel ? 14 : 11, FontWeight=FontWeights.SemiBold, TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis, MaxHeight=formatLabel ? 38 : 30, TextWrapping=TextWrapping.NoWrap };
        SetLeft(icon,center.X-12); SetTop(icon,center.Y-23);
        SetLeft(label,center.X-36); SetTop(label,formatLabel ? center.Y-10 : center.Y+6);
        icon.Visibility = formatLabel ? Visibility.Hidden : Visibility.Visible;
        Children.Add(icon); Children.Add(label);
        System.Windows.Automation.AutomationProperties.SetName(this,operation.Label);
        ToolTip = operation.Label;
        SetSelected(false,false);
    }
    /// <summary>The filled rounded outline owns native boundaries; its decorative stroke is not a target.</summary>
    public bool ContainsPoint(Point point) => outline.FillContains(point, .001, ToleranceType.Absolute);

    public void SetSelected(bool selected, bool motion)
    {
        selectedSurface.Opacity = selected ? 1 : 0;
        icon.SetResourceReference(ShapePath.StrokeProperty,selected ? "SelectedPetalText" : "PrimaryText");
        label.SetResourceReference(TextBlock.ForegroundProperty,selected ? "SelectedPetalText" : "PrimaryText");
        if (motion && !SystemParameters.HighContrast && PressFeedback.GetAnimator(this) is { } animator)
        {
            if (!animator.HasChannel(this, "specular")) animator.SetImmediate(this, "specular", specular.Opacity, ApplySpecular);
            animator.Retarget(this, "specular", selected ? .35 : 0, SpringParameters.Default, ApplySpecular);
        }
        else CancelEmphasis();
    }
    private void ApplySpecular(double value) => specular.Opacity = Math.Clamp(value, 0, 1);
    internal void ApplyTextScale(double scale)
    {
        label.FontSize = (formatLabel ? 14 : 11) * scale;
        var measured = new FormattedText(label.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(label.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), label.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (formatLabel)
        {
            // Keep concise format targets useful at larger text sizes inside fixed hit geometry.
            if (measured.Width > label.Width) label.FontSize *= label.Width / measured.Width;
            SetTop(label, GetTop(icon) + 23 - label.FontSize * .7);
            label.Visibility = Visibility.Visible;
        }
        else label.Visibility = scale <= 1.25 || measured.Width <= label.Width && measured.Height <= label.MaxHeight ? Visibility.Visible : Visibility.Hidden;
    }
    internal void CancelEmphasis()
    {
        PressFeedback.GetAnimator(this)?.CancelOwner(this, false);
        specular.Opacity = 0;
    }
    private static string IconKey(OperationDescriptor operation)
    {
        string id = operation.Id.ToLowerInvariant();
        foreach (string name in new[] { "crop","rotate","resize","copy","move","archive","audio","video" })
            if (id.Contains(name,StringComparison.Ordinal)) return "Icon." + char.ToUpperInvariant(name[0])+name[1..];
        if (id.Contains("reveal",StringComparison.Ordinal) || id.Contains("float",StringComparison.Ordinal)) return "Icon.Folder";
        if (id.Contains("image",StringComparison.Ordinal)) return "Icon.Image";
        if (id.Contains("text",StringComparison.Ordinal)) return "Icon.Text";
        return operation.Mode switch { OperationMode.Convert => "Icon.Convert", OperationMode.Tools => "Icon.Tools", _ => "Icon.File" };
    }
}
