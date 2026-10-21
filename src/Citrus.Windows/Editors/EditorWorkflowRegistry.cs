using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors;

/// <summary>One host/admission/publication boundary shared by every typed editor family.</summary>
internal sealed class EditorWorkflowRegistry
{
    private readonly JobQueue queue;
    private readonly Dictionary<string, Workflow> workflows = new(StringComparer.Ordinal);
    private sealed record Workflow(OperationDescriptor Operation, EditorRoute Route, EditorSubmissionService Submission,
        Func<EditorOpenRequest, Window?, CancellationToken, Task<EditorSubmissionBatch?>> Open);
    internal EditorWorkflowRegistry(JobQueue queue) => this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
    internal void Register(IReadOnlyList<OperationDescriptor> operations, EditorRouteRegistry routes, IEditorAdmissionInspector inspector,
        Func<EditorOpenRequest, Window?, CancellationToken, Task<EditorSubmissionBatch?>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var enabled = operations.Where(operation => routes.TryGet(operation.Id, out _)).ToArray();
        if (enabled.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count() != enabled.Length || enabled.Any(operation => workflows.ContainsKey(operation.Id)))
            throw new ArgumentException("An editor operation must have exactly one host.", nameof(operations));
        foreach (var operation in enabled)
        {
            routes.TryGet(operation.Id, out var route);
            if (operation.IsGroup != (route.Shape == EditorSubmissionShape.Group))
                throw new ArgumentException("Editor route and operation disagree about batch ownership.", nameof(routes));
        }
        var submission = new EditorSubmissionService(queue, routes, inspector);
        foreach (var operation in enabled)
        {
            routes.TryGet(operation.Id, out var route);
            workflows.Add(operation.Id, new(operation, route, submission, open));
        }
    }
    internal bool Contains(string operationId) => workflows.ContainsKey(operationId);
    internal async Task<IReadOnlyList<ConversionJob>> OpenAndSubmitAsync(OperationDrop drop, OperationOptions options, string? destination,
        SubmissionOrigin origin, Window? owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workflow = Resolve(drop.Operation);
        var request = new EditorOpenRequest(workflow.Operation, [.. drop.Paths], workflow.Route.EntryTool, options, destination, origin);
        var batch = await workflow.Open(request, owner, ct);
        ct.ThrowIfCancellationRequested();
        return batch is null ? [] : await workflow.Submission.SubmitAsync(batch, ct);
    }
    internal Task<ConversionJob> RetryAsync(OperationRequest request, string? destination, CancellationToken ct) =>
        Resolve(request.Operation).Submission.RetryAsync(request, destination, ct);
    private Workflow Resolve(OperationDescriptor operation)
    {
        if (!workflows.TryGetValue(operation.Id, out var workflow)) throw new NotSupportedException("This editor is unavailable.");
        if (workflow.Operation != operation) throw new ArgumentException("The editor operation descriptor has changed.", nameof(operation));
        return workflow;
    }
}
