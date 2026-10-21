using System.Windows;
using System.Windows.Controls;
using Citrus.Conversion;
using Citrus.Windows.Editors;
using Citrus.Core;
using Citrus.Windows.Presentation;
using System.Windows.Media;
using ShapePath=System.Windows.Shapes.Path;
using Brushes=System.Windows.Media.Brushes;
using FlowDirection=System.Windows.FlowDirection;
using Button=System.Windows.Controls.Button;
using Point=System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class CompactEditorLayoutTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2.25d)]
    public Task Selected_hub_keeps_full_tool_names_inside_its_capsule(double textScale)=>desktop.RunAsync(()=>
    {
        foreach(string text in new[]{"COPY PATHS","NORMALIZE","METADATA","SNAPSHOT"})
        {
            var title=new TextBlock {Text=text,FontFamily=(System.Windows.Media.FontFamily)System.Windows.Application.Current.FindResource("AppFont")};
            WheelTitleTypography.ApplyHub(title,textScale);
            var ink=new FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
                new Typeface(title.FontFamily,title.FontStyle,title.FontWeight,title.FontStretch),title.FontSize,Brushes.Black,1);
            Assert.True(ink.Width<=WheelPresentationMetrics.HubCapsuleWidth,$"{text} width {ink.Width}, scale {textScale}");
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1d)]
    [InlineData(2.25d)]
    public Task Nine_video_tool_labels_and_icons_stay_inside_compact_petals(double textScale)=>desktop.RunAsync(()=>
    {
        string[] ids=["compress","metadata","mute","trim","crop","speed","snapshot","split","redact"];
        for(int index=0;index<ids.Length;index++)
        {
            var petal=new WheelPetal(new("video."+ids[index],ids[index],OperationMode.Tools,null,false,true),index,ids.Length);
            var label=petal.Children.OfType<TextBlock>().Single();
            label.FontFamily=(System.Windows.Media.FontFamily)System.Windows.Application.Current.FindResource("AppFont");
            petal.ApplyTextScale(textScale);
            var text=new FormattedText(label.Text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
                new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,Brushes.Black,1)
                {MaxTextWidth=label.Width,TextAlignment=TextAlignment.Center,LineHeight=label.LineHeight};
            var outline=((ShapePath)petal.Children[0]).Data;
            Assert.True(outline.FillContainsWithDetail(text.BuildGeometry(new Point(Canvas.GetLeft(label),Canvas.GetTop(label))))==IntersectionDetail.FullyContains,$"{ids[index]} text at slot {index}, scale {textScale}");
            var icon=petal.Children.OfType<ShapePath>().Last();
            Assert.Equal(IntersectionDetail.FullyContains,outline.FillContainsWithDetail(new RectangleGeometry(new Rect(Canvas.GetLeft(icon),Canvas.GetTop(icon),icon.Width,icon.Height))));
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(420,440)]
    [InlineData(520,464)]
    [InlineData(520,578)]
    [InlineData(520,620)]
    public Task Image_preview_options_and_action_remain_reachable_in_compact_layout(double width,double height)=>desktop.RunAsync(()=>
    {
        using var services=new ImageEditorServices(new());
        var window=new EditorHostWindow(services);
        try
        {
            Assert.True(window.Width<=520&&window.Height<=620);
            var root=(FrameworkElement)window.Content;
            root.Measure(new(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
            var preview=(FrameworkElement)window.FindName("PreviewArea");
            var options=(ScrollViewer)window.FindName("InspectorScroll");
            var save=(Button)window.FindName("ExportButton");
            Assert.True(preview.ActualWidth>=100&&preview.ActualHeight>=20);
            Assert.True(options.ViewportHeight>=40);
            var bottom=save.TranslatePoint(new Point(save.ActualWidth,save.ActualHeight),root);
            Assert.InRange(bottom.Y,0,height);Assert.InRange(bottom.X,0,width);
        }
        finally {window.Close();}
        return Task.CompletedTask;
    });
}
