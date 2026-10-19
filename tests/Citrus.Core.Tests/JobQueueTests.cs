using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class JobQueueTests
{
    private static readonly OperationDescriptor Operation = new("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false);
    private static OperationRequest Request(string input) => new(Operation, [input], new(), null);

    [Fact]
    public async Task Third_job_waits_while_two_execute_and_synchronous_work_is_off_caller_thread()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, executions = 0;
        var activeCounts = new System.Collections.Concurrent.ConcurrentBag<int>();
        var engine = new TestEngine([Operation], _ => true, async (request, _, token) =>
        {
            int count = Interlocked.Increment(ref active);
            Interlocked.Increment(ref executions);
            activeCounts.Add(count);
            if (count == 2) started.TrySetResult();
            await release.Task.WaitAsync(token);
            Interlocked.Decrement(ref active);
            return new([request.Inputs[0] + ".out"]);
        });
        var queue = new JobQueue([engine]);
        var jobs = new[] { queue.Enqueue(Request("1")), queue.Enqueue(Request("2")) };
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var third = queue.Enqueue(Request("3"));
        Assert.Equal(ConversionJobState.Queued, third.State);
        Assert.Equal(2, Volatile.Read(ref executions));
        release.SetResult();
        await Task.WhenAll(jobs.Append(third).Select(j => j.Completion)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, activeCounts.Max());
        Assert.All(jobs.Append(third), j => Assert.Equal(ConversionJobState.Succeeded, j.State));
    }

    [Fact]
    public async Task Engine_synchronous_work_never_blocks_enqueue()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new TestEngine([Operation], _ => true, (request, _, token) =>
        {
            started.TrySetResult();
            release.Wait(token);
            return Task.FromResult(new ConversionResult(["result"]));
        });
        var queue = new JobQueue([engine]);
        // Run the caller on a dedicated thread so an inline engine cannot hang the suite.
        var enqueue = Task.Factory.StartNew(() => queue.Enqueue(Request("source")), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await enqueue.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
        await (await enqueue).Completion.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Queued_cancellation_does_not_execute_engine()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = new List<string>();
        var engine = new TestEngine([Operation], _ => true, async (request, _, token) =>
        {
            executions.Add(request.Inputs[0]); started.TrySetResult();
            await release.Task.WaitAsync(token); return new(["done"]);
        });
        var queue = new JobQueue([engine], 1);
        var first = queue.Enqueue(Request("first"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = queue.Enqueue(Request("second"));
        second.Cancel();
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ConversionJobState.Canceled, second.State);
        release.SetResult();
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["first"], executions);
    }

    [Fact]
    public async Task Active_cancellation_is_observed_and_next_job_runs()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new TestEngine([Operation], _ => true, async (request, _, token) =>
        {
            if (request.Inputs[0] == "cancel") { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new(["result"]);
        });
        var queue = new JobQueue([engine], 1);
        var canceled = queue.Enqueue(Request("cancel"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var next = queue.Enqueue(Request("next"));
        canceled.Cancel();
        await Task.WhenAll(canceled.Completion, next.Completion).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ConversionJobState.Canceled, canceled.State);
        Assert.Empty(canceled.Outputs);
        Assert.Equal(ConversionJobState.Succeeded, next.State);
    }

    [Fact]
    public async Task Cancellation_after_output_commit_preserves_success_and_output_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "citrus-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var desired = Path.Combine(root, "photo.png");
        var committed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new OutputWriter();
        var engine = new TestEngine([Operation], _ => true, async (_, _, token) =>
        {
            var output = await writer.WriteFileAsync(desired,
                (temporary, cancellation) => File.WriteAllTextAsync(temporary, "complete", cancellation), token);
            committed.SetResult(output);
            // The committed file is already a user output; allow cancellation before returning it.
            await allowReturn.Task;
            return new([output]);
        });
        var job = new JobQueue([engine]).Enqueue(Request("source"));
        try
        {
            var output = await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("complete", await File.ReadAllTextAsync(output));
            job.Cancel();
            allowReturn.SetResult();
            await job.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ConversionJobState.Succeeded, job.State);
            Assert.Null(job.Error);
            Assert.Equal([output], job.Outputs);
            Assert.Equal("complete", await File.ReadAllTextAsync(output));
            Assert.Equal([output], Directory.GetFileSystemEntries(root));
        }
        finally
        {
            allowReturn.TrySetResult();
            await job.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failure_is_per_job_and_progress_and_results_are_notified()
    {
        var engine = new TestEngine([Operation], _ => true, (request, progress, _) =>
        {
            if (request.Inputs[0] == "bad") throw new IOException("fixture failure");
            progress?.Report(new(.5, "working"));
            return Task.FromResult(new ConversionResult(["result"]));
        });
        var queue = new JobQueue([engine], 1);
        var changes = new System.Collections.Concurrent.ConcurrentQueue<(ConversionJobState State, ConversionProgress? Progress)>();
        queue.JobChanged += job => changes.Enqueue((job.State, job.Progress));
        var failed = queue.Enqueue(Request("bad"));
        var good = queue.Enqueue(Request("good"));
        await Task.WhenAll(failed.Completion, good.Completion).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ConversionJobState.Failed, failed.State);
        Assert.IsType<IOException>(failed.Error);
        Assert.Empty(failed.Outputs);
        Assert.Equal(ConversionJobState.Succeeded, good.State);
        Assert.Equal(["result"], good.Outputs);
        Assert.Equal(.5, good.Progress?.Fraction);
        Assert.Contains(changes, c => c.State == ConversionJobState.Running && c.Progress?.Message == "working");
        Assert.Contains(changes, c => c.State == ConversionJobState.Succeeded);
    }

    [Fact]
    public async Task Unsupported_operation_is_an_actionable_job_failure()
    {
        var job = new JobQueue([]).Enqueue(Request("source"));
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ConversionJobState.Failed, job.State);
        Assert.IsType<NotSupportedException>(job.Error);
    }

    [Fact]
    public void Concurrency_must_be_positive() => Assert.Throws<ArgumentOutOfRangeException>(() => new JobQueue([], 0));
}
