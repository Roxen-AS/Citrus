using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors;
using Citrus.Windows.Presentation;
using ImageMagick;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class ImageEditorWindowTests(DesktopFixture desktop)
{
    [Fact]
    public Task Empty_validation_message_does_not_take_compact_inspector_space()=>desktop.RunAsync(async()=>
    {
        using var fixture=new Fixture();
        await Show(fixture,window=>
        {
            var message=(TextBlock)window.FindName("ErrorText");
            Assert.Empty(message.Text);Assert.Equal(Visibility.Collapsed,message.Visibility);
            message.Text="Enter a valid pixel size.";window.UpdateLayout();
            Assert.Equal(Visibility.Visible,message.Visibility);Assert.True(message.ActualHeight>0);
            message.Text="";window.UpdateLayout();Assert.Equal(Visibility.Collapsed,message.Visibility);
            Click(window,"Cancel");
        });
    });
    [Fact]
    public Task Actual_XAML_editor_title_uses_the_shared_editor_size() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture, window =>
        {
            var heading = (TextBlock)window.FindName("Heading");
            Assert.Equal((double)window.FindResource("EditorTitleSize"), heading.FontSize);
            Click(window,"Cancel");
        });
        Assert.Null(batch);
    });

    [Fact]
    public Task Canceling_real_host_returns_no_batch_and_no_job() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture,window => Click(window,"Cancel"));
        Assert.Null(batch);
        Assert.Single(Directory.GetFiles(fixture.Root));
    });

    [Fact]
    public Task Native_crop_numeric_undo_redo_accepts_once_and_exports_real_cropped_pixels() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture,window =>
        {
            ((ComboBox)window.FindName("ToolPicker")).SelectedItem = "Crop";
            var width = Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Width · pixels");
            width.Text = "25";
            width.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(25,((ImageRecipe)window.Model!.Selected!.Draft!.Recipe).Geometry.Crop!.Value.Width);
            Click(window,"Undo"); Assert.Null(((ImageRecipe)window.Model.Selected.Draft!.Recipe).Geometry.Crop);
            Click(window,"Redo"); Assert.Equal(25,((ImageRecipe)window.Model.Selected.Draft!.Recipe).Geometry.Crop!.Value.Width);
            ClickExport(window);
        });
        Assert.NotNull(batch); Assert.Single(batch.Items);
        var queue = new JobQueue([fixture.Services.Engine]); var submissions = new EditorSubmissionService(queue,fixture.Services.Routes,fixture.Services.Inspector);
        var job = Assert.Single(await submissions.SubmitAsync(batch,default)); await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded,job.State);
        using var encoded = File.OpenRead(job.Outputs.Single());
        using var output = new MagickImage(encoded,new MagickReadSettings { Format = MagickFormat.Png });
        Assert.Equal(25u,output.Width); Assert.Equal(50u,output.Height);
    });

    [Theory]
    [InlineData("20","",true,true,20,10)]
    [InlineData("200","",true,false,100,50)]
    [InlineData("20","15",false,false,20,15)]
    public Task Resize_enter_validates_and_commits_pair_once_before_export(string widthText,string heightText,bool preserveAspect,bool allowUpscale,int expectedWidth,int expectedHeight) => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture,window =>
        {
            ((ComboBox)window.FindName("ToolPicker")).SelectedItem = "Resize";
            var width = Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Width · optional pixels");
            var height = Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Height · optional pixels");
            Descendants(window).OfType<System.Windows.Controls.CheckBox>().Single(c => Equals(c.Content,"Preserve aspect ratio")).IsChecked = preserveAspect;
            Descendants(window).OfType<System.Windows.Controls.CheckBox>().Single(c => Equals(c.Content,"Allow upscaling")).IsChecked = allowUpscale;
            void Enter(TextBox box) => box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            width.Text = "invalid"; Enter(width);
            Assert.NotEmpty(((TextBlock)window.FindName("ErrorText")).Text);
            Assert.Null(((ImageRecipe)window.Model!.Selected!.Draft!.Recipe).Geometry.OutputSize);
            width.Text = widthText; height.Text = heightText; Enter(width);
            Assert.Equal(new SizeI(expectedWidth,expectedHeight),((ImageRecipe)window.Model.Selected.Draft!.Recipe).Geometry.OutputSize);
            Assert.Equal(allowUpscale,((ImageRecipe)window.Model.Selected.Draft.Recipe).Geometry.AllowUpscale);
            Assert.Equal(1,window.Model.Selected.Draft.Revision); Assert.Empty(((TextBlock)window.FindName("ErrorText")).Text);
            Click(window,"Undo"); Assert.Null(((ImageRecipe)window.Model.Selected.Draft!.Recipe).Geometry.OutputSize);
            Click(window,"Redo"); Assert.Equal(new SizeI(expectedWidth,expectedHeight),((ImageRecipe)window.Model.Selected.Draft!.Recipe).Geometry.OutputSize);
            ClickExport(window);
        });
        Assert.NotNull(batch);
        var submissions = new EditorSubmissionService(new JobQueue([fixture.Services.Engine]),fixture.Services.Routes,fixture.Services.Inspector);
        var job = Assert.Single(await submissions.SubmitAsync(batch,default)); await job.Completion; Assert.Equal(ConversionJobState.Succeeded,job.State);
        using var stream = File.OpenRead(job.Outputs.Single()); using var image = new MagickImage(stream,new MagickReadSettings { Format = MagickFormat.Png });
        Assert.Equal((uint)expectedWidth,image.Width); Assert.Equal((uint)expectedHeight,image.Height);
    });

    [Fact]
    public Task Reset_clears_invalid_numeric_text_even_when_slider_is_already_at_default() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture, window =>
        {
            var brightness = Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Brightness · -1–1");
            brightness.Text = "invalid";
            brightness.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.NotEmpty(((TextBlock)window.FindName("ErrorText")).Text);
            Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Reset Brightness")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("0", brightness.Text);
            Assert.Empty(((TextBlock)window.FindName("ErrorText")).Text);
            ClickExport(window);
        });
        Assert.NotNull(batch);
        Assert.Equal(0, ((ImageRecipe)batch.Items.Single().Document.Recipe).Adjustments.Brightness);
    });

    [Fact]
    public Task Reset_at_default_clears_invalid_text_without_discarding_redo() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        await Show(fixture, window =>
        {
            TextBox Brightness() => Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Brightness · -1–1");
            void Enter(TextBox box) => box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Brightness().Text = "0.2"; Enter(Brightness());
            Click(window, "Undo");
            Brightness().Text = "invalid"; Enter(Brightness());
            Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Reset Brightness")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("0", Brightness().Text);
            Assert.Empty(((TextBlock)window.FindName("ErrorText")).Text);
            Click(window, "Redo");
            Assert.Equal(.2, ((ImageRecipe)window.Model!.Selected!.Draft!.Recipe).Adjustments.Brightness);
            ClickExport(window);
        });
    });

    [Fact]
    public Task Minimum_window_at_double_text_keeps_export_footer_visible_and_real_preview() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        var batch = await Show(fixture,window =>
        {
            window.Width = 760; window.Height = 560;
            window.Resources["BodySize"] = 28d; window.Resources["BodyLeading"] = 40d; window.Resources["EditorTitleSize"] = 44d;
            window.UpdateLayout();
            var button = (Button)window.FindName("ExportButton");
            var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= window.ActualHeight && bounds.Right <= window.ActualWidth);
            Assert.IsAssignableFrom<BitmapSource>(((System.Windows.Controls.Image)window.FindName("PreviewImage")).Source);
            Assert.True(((FrameworkElement)window.FindName("InspectorScroll")).TranslatePoint(new System.Windows.Point(), window).Y > ((FrameworkElement)window.FindName("PreviewArea")).TranslatePoint(new System.Windows.Point(), window).Y);
            Click(window,"Cancel");
        });
        Assert.Null(batch);
    });
    [Theory]
    [InlineData("image.editor","Adjust","Save")]
    [InlineData("image.crop","Crop","Apply")]
    [InlineData("image.background","Background","Save with Background")]
    [InlineData("image.compress","Compress","Compress")]
    [InlineData("image.annotate","Annotate","Save")]
    [InlineData("image.redact","Manual masks","Save")]
    public Task Reference_image_tools_keep_white_chrome_and_reachable_actions(string id,string tool,string action) => desktop.RunAsync(async () =>
    {
        using var fixture=new Fixture();
        var operation=fixture.Services.Engine.Operations.Single(o=>o.Id==id);
        var request=fixture.Request with {Operation=operation,EntryTool=tool};
        await Show(fixture,window=>
        {
            var export=(Button)window.FindName("ExportButton");
            Assert.Equal(action,export.Content);
            Assert.Equal(Colors.White,Assert.IsType<SolidColorBrush>(window.FindResource("WindowBackground")).Color);
            foreach(double scale in new[]{1d,1.5,2d})VisualArtifacts.Capture((FrameworkElement)window.Content,"image-reference-default-"+id.Replace('.','-')+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),scale,ThemeChoice.Light);
            window.Width=window.MinWidth;window.Height=window.MinHeight;window.UpdateLayout();
            VisualArtifacts.AssertInside(export,(FrameworkElement)window.Content);
            foreach(double scale in new[]{1d,1.5,2d})VisualArtifacts.Capture((FrameworkElement)window.Content,"image-reference-minimum-"+id.Replace('.','-')+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),scale,ThemeChoice.Light);
            Click(window,"Cancel");
        },request);
    });
    private static async Task<EditorSubmissionBatch?> Show(Fixture fixture,Action<EditorHostWindow> exercise,EditorOpenRequest? request=null)
    {
        using var cancel = new CancellationTokenSource();
        var window = new EditorHostWindow(fixture.Services) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        using var themes=new ThemeManager(System.Windows.Application.Current);
        using var presentation=new WindowPresentationService(System.Windows.Application.Current,themes,()=>new CitrusSettings());
        presentation.Attach(window,(MaterialSurface)window.Content);
        Exception? failure = null; int ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 500) throw new TimeoutException("The real image editor did not publish its first preview.");
                if (((System.Windows.Controls.Image)window.FindName("PreviewImage")).Source is null) return;
                timer.Stop(); exercise(window);
            }
            catch (Exception error) { failure = error; timer.Stop(); cancel.Cancel(); }
        };
        timer.Start();
        EditorSubmissionBatch? result;
        try { result = await window.ShowEditorAsync(request??fixture.Request,cancel.Token); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
        return result;
    }
    private static void Click(DependencyObject root,string content) => Descendants(root).OfType<Button>().Single(b => Equals(b.Content,content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void ClickExport(EditorHostWindow window) => ((Button)window.FindName("ExportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child = VisualTreeHelper.GetChild(root,i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"citrus-native-image-host-"+Guid.NewGuid().ToString("N"));
        public ImageEditorServices Services { get; } = new(new());
        public EditorOpenRequest Request { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root); string path = Path.Combine(Root,"original.png");
            using var image = new MagickImage(MagickColors.Orange,100,50); image.Write(path,MagickFormat.Png);
            Request = new(new("image.editor","Adjust",OperationMode.Tools,null,false,true),[path],"Adjust",new(),null,SubmissionOrigin.Home);
        }
        public void Dispose() { Services.Dispose(); Directory.Delete(Root,true); }
    }
}
