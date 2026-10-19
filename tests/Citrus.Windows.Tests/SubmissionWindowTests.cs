using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class SubmissionWindowTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task Canceling_real_options_or_group_window_submits_no_work(bool group) => desktop.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "Citrus dialog " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string input = Path.Combine(root, "source.png"); await File.WriteAllTextAsync(input, "untouched");
            IConversionEngine engine = group ? new ArchiveEngine() : new ImageEngine();
            var operation = engine.Operations.Single(o => o.Id == (group ? "archive.zip" : "image.resize"));
            var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
            var jobs = await service.SubmitAsync(operation, [input], (draft, _) => Task.FromResult(Show(draft, dialog => Click(dialog, "CancelButton"))));
            Assert.Empty(jobs); Assert.Equal([input], Directory.GetFiles(root)); Assert.Equal("untouched", await File.ReadAllTextAsync(input));
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task Ordered_group_dialog_changes_real_archive_entry_order() => desktop.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "Citrus order " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.txt"); await File.WriteAllTextAsync(a, "A"); await File.WriteAllTextAsync(b, "B");
            var engine = new ArchiveEngine();
            var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
            var jobs = await service.SubmitAsync(engine.Operations.Single(o => o.Id == "archive.zip"), [a, b], (draft, _) => Task.FromResult(Show(draft, dialog =>
            {
                Click(dialog, "MoveDownButton"); Click(dialog, "CreateButton");
            })));
            var job = Assert.Single(jobs); await job.Completion;
            Assert.Equal(ConversionJobState.Succeeded, job.State); Assert.Equal([b, a], job.Request.Inputs);
            using var zip = ZipFile.OpenRead(Assert.Single(job.Outputs)); Assert.Equal(new[] { "b.txt", "a.txt" }, zip.Entries.Select(e => e.Name));
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task Invalid_trim_stays_open_then_valid_trim_is_returned() => desktop.RunAsync(() =>
    {
        string input = Path.GetFullPath("short.mp4");
        var operation = new OperationDescriptor("video.trim", "Trim", OperationMode.Tools, ".mp4", false, true);
        var draft = new SubmissionDraft(operation, [input], new(), null, [new(input, 4, null, null)]);
        var choice = Show(draft, dialog =>
        {
            ((TextBox)dialog.FindName("EndInput")).Text = "5"; Click(dialog, "CreateButton");
            Assert.True(dialog.IsVisible); Assert.Contains("4 seconds", ((TextBlock)dialog.FindName("ErrorText")).Text);
            ((TextBox)dialog.FindName("EndInput")).Text = "3"; Click(dialog, "CreateButton");
        });
        Assert.NotNull(choice); Assert.Equal(3, choice.Options.EndSeconds);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Resize_retry_can_clear_an_existing_optional_dimension() => desktop.RunAsync(() =>
    {
        var operation = new ImageEngine().Operations.Single(o => o.Id == "image.resize");
        var choice = Show(new(operation, [Path.GetFullPath("image.png")], new() { Width = 100, Height = 100 }, null, []), dialog =>
        {
            ((TextBox)dialog.FindName("HeightInput")).Text = "";
            Click(dialog, "CreateButton");
        });
        Assert.NotNull(choice); Assert.Equal(100, choice.Options.Width); Assert.Null(choice.Options.Height);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Mixed_folder_group_stays_open_without_explicit_destination() => desktop.RunAsync(() =>
    {
        var operation = new ArchiveEngine().Operations.Single(o => o.Id == "archive.zip");
        var choice = Show(new(operation, [Path.GetFullPath("one/a.txt"), Path.GetFullPath("two/b.txt")], new(), null, []), dialog =>
        {
            Click(dialog, "CreateButton"); Assert.True(dialog.IsVisible);
            Assert.Contains("destination", ((TextBlock)dialog.FindName("ErrorText")).Text);
            Click(dialog, "CancelButton");
        });
        Assert.Null(choice); return Task.CompletedTask;
    });

    private static SubmissionChoice? Show(SubmissionDraft draft, Action<ToolOptionsWindow> action)
    {
        var dialog = new ToolOptionsWindow(draft);
        Exception? failure = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try { action(dialog); }
            catch (Exception error) { failure = error; dialog.Close(); }
        }));
        try
        {
            bool? accepted = dialog.ShowDialog();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return accepted == true ? dialog.Choice : null;
        }
        finally { dialog.Close(); }
    }
    private static void Click(ToolOptionsWindow dialog, string name) => ((Button)dialog.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
