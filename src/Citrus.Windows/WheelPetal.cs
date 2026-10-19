using Citrus.Core;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows;

/// <summary>One retained target. Hover changes brushes/opacity only; geometry and layout stay fixed.</summary>
public sealed class WheelPetal : Canvas
{
    private readonly ShapePath selectedSurface, icon;
    private readonly StreamGeometry outline;
    private readonly TextBlock label;
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
        var center = WheelGeometry.At(122,index*360d/count);
        icon = new ShapePath { Data = (Geometry)Application.Current.FindResource(IconKey(operation)), Width=24, Height=24,
            Stretch=Stretch.Uniform, StrokeThickness=1.6, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round, StrokeLineJoin=PenLineJoin.Round };
        label = new TextBlock { Text=operation.Label, Width=68, FontSize=11, FontWeight=FontWeights.SemiBold, TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis, MaxHeight=16, TextWrapping=TextWrapping.NoWrap };
        SetLeft(icon,center.X-12); SetTop(icon,center.Y-23);
        SetLeft(label,center.X-34); SetTop(label,center.Y+6);
        Children.Add(icon); Children.Add(label);
        System.Windows.Automation.AutomationProperties.SetName(this,operation.Label);
        SetSelected(false,false);
    }
    /// <summary>The filled rounded outline owns native boundaries; its decorative stroke is not a target.</summary>
    public bool ContainsPoint(Point point) => outline.FillContains(point, .001, ToleranceType.Absolute);

    public void SetSelected(bool selected, bool motion)
    {
        double from = selectedSurface.Opacity;
        selectedSurface.BeginAnimation(OpacityProperty,null);
        selectedSurface.Opacity = selected ? 1 : 0;
        if (motion) selectedSurface.BeginAnimation(OpacityProperty,new DoubleAnimation(from,selectedSurface.Opacity,(Duration)FindResource("HoverDuration")));
        icon.SetResourceReference(ShapePath.StrokeProperty,selected ? "SelectedPetalText" : "PrimaryText");
        label.SetResourceReference(TextBlock.ForegroundProperty,selected ? "SelectedPetalText" : "PrimaryText");
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
