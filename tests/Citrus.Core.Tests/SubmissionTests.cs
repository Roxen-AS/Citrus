using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class SubmissionTests
{
    private static readonly OperationDescriptor Convert = new("test.copy", "Copy", OperationMode.Convert, ".out", false, false);
    private static readonly OperationDescriptor Group = new("archive.zip", "ZIP", OperationMode.Convert, ".zip", true, false);
    private static readonly OperationDescriptor Trim = new("video.trim", "Trim", OperationMode.Tools, ".mp4", false, true);
    private static string PathFor(string name) => Path.GetFullPath(name);

    [Fact]
    public void Ordered_group_is_a_snapshot_and_defaults_to_first_directory()
    {
        var paths = new List<string> { PathFor("b.pdf"), PathFor("a.pdf") };
        var request = Assert.Single(BatchRequestBuilder.Build(Group, paths, new(), null));
        paths.Reverse(); paths.Clear();
        Assert.Equal(new[] { PathFor("b.pdf"), PathFor("a.pdf") }, request.Inputs);
        Assert.Equal(Path.GetDirectoryName(PathFor("b.pdf")), request.DestinationDirectory);
    }

    [Fact]
    public void Mixed_directory_group_requires_explicit_destination_but_ordinary_batch_does_not()
    {
        string[] paths = [PathFor("one/a.pdf"), PathFor("two/b.pdf")];
        Assert.True(BatchRequestBuilder.RequiresDestination(paths));
        Assert.Throws<ArgumentException>(() => BatchRequestBuilder.Build(Group, paths, new(), null));
        Assert.Equal(PathFor("chosen"), Assert.Single(BatchRequestBuilder.Build(Group, paths, new(), PathFor("chosen"))).DestinationDirectory);
        Assert.All(BatchRequestBuilder.Build(Convert, paths, new(), null), r => Assert.Null(r.DestinationDirectory));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Canceling_options_or_group_confirmation_enqueues_nothing(bool group)
    {
        int calls = 0;
        var engine = new TestEngine([group ? Group : Trim], _ => true, (_, _, _) => { calls++; return Task.FromResult(new ConversionResult([])); });
        var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
        var jobs = await service.SubmitAsync(group ? Group : Trim, [PathFor("a.mp4")], (_, _) => Task.FromResult<SubmissionChoice?>(null));
        Assert.Empty(jobs); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Ordinary_batch_keeps_success_when_another_item_fails()
    {
        var engine = new TestEngine([Convert], _ => true, (r, _, _) => r.Inputs[0].EndsWith("bad")
            ? Task.FromException<ConversionResult>(new IOException("Unreadable")) : Task.FromResult(new ConversionResult([r.Inputs[0] + ".out"])));
        var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
        var jobs = await service.SubmitAsync(Convert, [PathFor("good"), PathFor("bad")], (_, _) => throw new InvalidOperationException("No dialog needed"));
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal(ConversionJobState.Succeeded, jobs[0].State); Assert.Equal(ConversionJobState.Failed, jobs[1].State);
        Assert.Equal(PathFor("good") + ".out", Assert.Single(jobs[0].Outputs));
    }

    [Fact]
    public async Task Inspection_failure_is_per_item_and_does_not_reach_engine()
    {
        var executed = new List<string>();
        var engine = new TestEngine([Trim], _ => true, (r, _, _) => { lock (executed) executed.Add(r.Inputs[0]); return Task.FromResult(new ConversionResult([])); });
        var service = new SubmissionService(new JobQueue([engine]), (draft, _) => Task.FromResult<IReadOnlyList<InputInspection>>([
            new(draft.Inputs[0], 10, null, null), new(draft.Inputs[1], null, null, "Corrupt media") ]));
        var jobs = await service.SubmitAsync(Trim, [PathFor("good"), PathFor("bad")], (draft, _) => Task.FromResult<SubmissionChoice?>(new(draft.Inputs, new() { EndSeconds = 5 }, null)));
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal([PathFor("good")], executed); Assert.Equal(ConversionJobState.Failed, jobs[1].State);
        Assert.Contains("Corrupt", jobs[1].Error!.Message);
    }

    [Fact]
    public async Task Invalid_trim_end_against_any_inspectable_input_blocks_whole_submission()
    {
        var engine = new TestEngine([Trim], _ => true);
        var service = new SubmissionService(new JobQueue([engine]), (draft, _) => Task.FromResult<IReadOnlyList<InputInspection>>([
            new(draft.Inputs[0], 20, null, null), new(draft.Inputs[1], 4, null, null)]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(Trim, [PathFor("long"), PathFor("short")],
            (draft, _) => Task.FromResult<SubmissionChoice?>(new(draft.Inputs, new() { EndSeconds = 5 }, null))));
    }

    [Fact]
    public async Task Confirmation_reordering_is_snapshotted_and_cannot_swap_inspected_inputs()
    {
        var engine = new TestEngine([Group], _ => true);
        var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
        var ordered = new List<string> { PathFor("b.txt"), PathFor("a.txt") };
        var jobs = await service.SubmitAsync(Group, [PathFor("a.txt"), PathFor("b.txt")], (_, _) => Task.FromResult<SubmissionChoice?>(new(ordered, new(), null)));
        ordered.Clear();
        var job = Assert.Single(jobs); await job.Completion;
        Assert.Equal(new[] { PathFor("b.txt"), PathFor("a.txt") }, job.Request.Inputs);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(Group, [PathFor("a.txt")], (_, _) =>
            Task.FromResult<SubmissionChoice?>(new([PathFor("different.txt")], new(), null))));
    }

    [Fact]
    public async Task Cancellation_after_confirmation_prevents_all_jobs()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new SubmissionService(new JobQueue([]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubmitAsync(Group, [PathFor("a.txt")], (draft, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<SubmissionChoice?>(new(draft.Inputs, new(), null));
        }, cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("image.resize", "width")][InlineData("image.compress", "quality")][InlineData("video.speed", "speed")]
    [InlineData("audio.normalize", "normalize")][InlineData("video.trim", "trim")][InlineData("image.rotate", "rotate")]
    public void Invalid_parameters_are_rejected(string id, string field)
    {
        var options = field switch { "width" => new OperationOptions { Width = 0 }, "quality" => new() { Quality = 101 },
            "speed" => new() { Speed = double.NaN }, "normalize" => new() { NormalizeLufs = -4 },
            "trim" => new() { StartSeconds = 3, EndSeconds = 2 }, _ => new() { RotationDegrees = 45 } };
        Assert.NotNull(OperationOptionsValidator.GetError(new(id, id, OperationMode.Tools, null, false, true), options, []));
    }

    [Theory]
    [InlineData("1,4", 3)] [InlineData("2-1", 4)] [InlineData("1,1", 4)] [InlineData("0", 4)]
    public void Page_selection_is_validated_against_real_page_count(string pages, int count) =>
        Assert.NotNull(OperationOptionsValidator.GetError(new("pdf.split", "Split", OperationMode.Tools, ".pdf", false, true),
            new() { PageSelection = pages }, [new(PathFor("a.pdf"), null, count, null)]));

    [Fact]
    public void Valid_boundaries_and_page_order_are_accepted()
    {
        Assert.Null(OperationOptionsValidator.GetError(Trim, new() { StartSeconds = 0, EndSeconds = 4 }, [new("a", 4, null, null)]));
        foreach (double speed in new[] { .25, 4 }) Assert.Null(OperationOptionsValidator.GetError(new("video.speed", "Speed", OperationMode.Tools, null, false, true), new() { Speed = speed }, []));
        Assert.Null(OperationOptionsValidator.GetError(new("pdf.split", "Split", OperationMode.Tools, null, false, true), new() { PageSelection = "3,1-2" }, [new("a", null, 3, null)]));
    }
}
