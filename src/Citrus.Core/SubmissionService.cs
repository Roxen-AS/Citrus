namespace Citrus.Core;

public sealed record InputInspection(string Path, double? DurationSeconds, int? PageCount, string? Error);
public sealed record SubmissionDraft(OperationDescriptor Operation, IReadOnlyList<string> Inputs, OperationOptions Options, string? DestinationDirectory, IReadOnlyList<InputInspection> Inspections);
public sealed record SubmissionChoice(IReadOnlyList<string> Inputs, OperationOptions Options, string? DestinationDirectory);

/// <summary>Shared wheel/picker/drop/retry path. Confirmation and all validation precede enqueueing.</summary>
public sealed class SubmissionService(JobQueue queue, Func<SubmissionDraft, CancellationToken, Task<IReadOnlyList<InputInspection>>> inspect)
{
    public async Task<IReadOnlyList<ConversionJob>> SubmitAsync(OperationDescriptor operation, IReadOnlyList<string> paths,
        Func<SubmissionDraft, CancellationToken, Task<SubmissionChoice?>> confirm,
        OperationOptions? options = null, string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var snapshot = Array.AsReadOnly(paths.Select(Path.GetFullPath).ToArray());
        if (snapshot.Count == 0) throw new ArgumentException("Choose at least one input.", nameof(paths));
        var draft = new SubmissionDraft(operation, snapshot, options ?? new(), destinationDirectory, []);
        draft = draft with { Inspections = await inspect(draft, cancellationToken) };
        cancellationToken.ThrowIfCancellationRequested();
        var choice = operation.NeedsOptions || operation.IsGroup
            ? await confirm(draft, cancellationToken) : new(snapshot, draft.Options, destinationDirectory);
        if (choice is null) return [];
        cancellationToken.ThrowIfCancellationRequested();
        // The dialog may reorder the selection but cannot replace inputs after their inspection.
        if (!snapshot.Order(StringComparer.Ordinal).SequenceEqual(choice.Inputs.Order(StringComparer.Ordinal)))
            throw new ArgumentException("The confirmed inputs must match the inspected selection.");
        if (OperationOptionsValidator.GetError(operation, choice.Options, draft.Inspections) is string error)
            throw new ArgumentException(error, nameof(options));
        var requests = BatchRequestBuilder.Build(operation, choice.Inputs, choice.Options, choice.DestinationDirectory);
        var jobs = new List<ConversionJob>();
        foreach (var request in requests)
        {
            var failure = draft.Inspections.FirstOrDefault(i => i.Error is not null && request.Inputs.Contains(i.Path));
            jobs.Add(failure is null ? queue.Enqueue(request) : queue.EnqueueFailure(request, new IOException(failure.Error)));
        }
        return jobs.AsReadOnly();
    }
}
