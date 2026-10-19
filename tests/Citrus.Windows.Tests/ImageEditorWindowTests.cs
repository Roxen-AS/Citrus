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
            Click(window,"Create copies");
        });
        Assert.NotNull(batch); Assert.Single(batch.Items);
        var queue = new JobQueue([fixture.Services.Engine]); var submissions = new EditorSubmissionService(queue,fixture.Services.Routes,fixture.Services.Inspector);
        var job = Assert.Single(await submissions.SubmitAsync(batch,default)); await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded,job.State);
        using var encoded = File.OpenRead(job.Outputs.Single());
        using var output = new MagickImage(encoded,new MagickReadSettings { Format = MagickFormat.Png });
        Assert.Equal(25u,output.Width); Assert.Equal(50u,output.Height);
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
            Assert.Contains(Descendants(window).OfType<Grid>(), g => g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[1].Width.Value == 304);
            Click(window,"Cancel");
        });
        Assert.Null(batch);
    });
    private static async Task<EditorSubmissionBatch?> Show(Fixture fixture,Action<EditorHostWindow> exercise)
    {
        using var cancel = new CancellationTokenSource();
        var window = new EditorHostWindow(fixture.Services) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
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
        try { result = await window.ShowEditorAsync(fixture.Request,cancel.Token); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
        return result;
    }
    private static void Click(DependencyObject root,string content) => Descendants(root).OfType<Button>().Single(b => Equals(b.Content,content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
