using System.Collections.Immutable;

namespace Citrus.Core.Editing;

/// <summary>Validates the whole frozen batch before synchronous admission into the existing queue.</summary>
public sealed class EditorSubmissionService(JobQueue queue, EditorRouteRegistry routes, IEditorAdmissionInspector inspector)
{
    public async Task<IReadOnlyList<ConversionJob>> SubmitAsync(EditorSubmissionBatch batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batch);
        Require(!batch.Request.InitialPaths.IsDefaultOrEmpty && !batch.InitialSources.IsDefault && !batch.ExcludedInitialPaths.IsDefault && !batch.Imports.IsDefault && !batch.IncludedRootIds.IsDefaultOrEmpty && !batch.Items.IsDefaultOrEmpty,"Incomplete batch.");
        Require(routes.TryGet(batch.Request.Operation.Id,out var route),"Editor route is unavailable.");
        Require(Enum.IsDefined(route.Shape) && batch.Request.Operation.IsGroup == (route.Shape == EditorSubmissionShape.Group),"Descriptor and route shape disagree.");
        Require(batch.Request.EntryTool == route.EntryTool,"Wrong editor entry tool.");
        var initialPaths = batch.Request.InitialPaths.Select(EditorPathPolicy.Normalize).ToArray();
        Require(initialPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == initialPaths.Length,"Duplicate initial path.");
        var excluded = batch.ExcludedInitialPaths.Select(EditorPathPolicy.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(excluded.Count == batch.ExcludedInitialPaths.Length && excluded.All(p => initialPaths.Contains(p,StringComparer.OrdinalIgnoreCase)),"Exclusion is not an initial path.");
        int lastIndex = -1;
        foreach (var source in batch.InitialSources)
        {
            int index = Array.FindIndex(initialPaths,p => EditorPathPolicy.Same(p,source.Path));
            Require(index > lastIndex,"Initial sources must be the captured subset in initial order."); lastIndex = index;
        }
        var all = batch.InitialSources.Concat(batch.Imports.Select(i => i.Source)).ToArray();
        Require(all.All(s => s is not null && s.Id != Guid.Empty) && all.Select(s => s.Id).Distinct().Count() == all.Length,"Duplicate or empty source identity.");
        Require(all.Select(s => EditorPathPolicy.Normalize(s.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Length,"Duplicate imported path.");
        Require(batch.Imports.All(i => Enum.IsDefined(i.Role) && !initialPaths.Contains(EditorPathPolicy.Normalize(i.Source.Path),StringComparer.OrdinalIgnoreCase)),"Import cannot replace initial payload.");
        var known = all.ToDictionary(s => s.Id);
        var allowedRoots = batch.InitialSources.Select(s => s.Id).Concat(batch.Imports.Where(i => i.Role == EditorImportRole.Root).Select(i => i.Source.Id)).ToHashSet();
        var included = batch.IncludedRootIds.ToHashSet();
        Require(included.Count == batch.IncludedRootIds.Length && included.All(allowedRoots.Contains),"Unknown, duplicate or auxiliary root.");
        foreach (var path in initialPaths)
        {
            var captured = batch.InitialSources.FirstOrDefault(s => EditorPathPolicy.Same(s.Path,path));
            bool selected = captured is not null && included.Contains(captured.Id);
            Require(selected != excluded.Contains(path),"Every initial path must be included or explicitly excluded.");
        }
        Require(batch.Items.All(i => i.Id != Guid.Empty && !i.RootSourceIds.IsDefaultOrEmpty) && batch.Items.Select(i => i.Id).Distinct().Count() == batch.Items.Length,"Invalid item identities.");
        if (route.Shape == EditorSubmissionShape.Group)
            Require(batch.Items.Length == 1 && batch.Items[0].RootSourceIds.SequenceEqual(batch.IncludedRootIds),"Grouped roots must match included order exactly.");
        else
            Require(batch.Items.All(i => i.RootSourceIds.Length == 1) && batch.Items.SelectMany(i => i.RootSourceIds).SequenceEqual(batch.IncludedRootIds),"Per-root items must cover each included root once in order.");
        var requests = new List<OperationRequest>();
        foreach (var item in batch.Items)
        {
            Require(item.RootSourceIds.All(included.Contains),"Item includes an excluded root.");
            var doc = item.Document;
            ValidateDocument(doc);
            Require(EditorRecipeReferences.Roots(doc.Recipe).ToHashSet().SetEquals(item.RootSourceIds),"Recipe and item roots disagree.");
            var referenced = EditorRecipeReferences.Sources(doc.Recipe).ToHashSet();
            Require(doc.Sources.All(s => known.TryGetValue(s.Id,out var captured) && captured == s && referenced.Contains(s.Id)),"Document source is not the captured root or explicit referenced import.");
            Require(doc.Sources.Where(s => !item.RootSourceIds.Contains(s.Id)).All(s => batch.Imports.Any(i => i.Source.Id == s.Id && i.Role == EditorImportRole.Auxiliary)),"Non-root inputs must be explicit auxiliary imports.");
            var destination = doc.Export.DestinationDirectory ?? batch.Request.DestinationDirectory;
            var request = new OperationRequest(batch.Request.Operation,Array.AsReadOnly(item.RootSourceIds.Select(id => known[id].Path).ToArray()),batch.Request.InitialOptions,destination,
                doc with { Export = doc.Export with { DestinationDirectory = destination } });
            ValidateRequest(request);
            requests.Add(request);
        }
        // Inspection verifies real source identities and family bounds; no caller-provided ready flag is trusted.
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            var issues = await inspector.ValidateAsync(request,ct).ConfigureAwait(false);
            Require(!issues.IsDefault && issues.IsEmpty,string.Join("; ",issues.IsDefault ? ["Inspection did not complete."] : issues.Select(i => i.FieldPath + ": " + i.Message)));
        }
        ct.ThrowIfCancellationRequested();
        // There are no awaits or cancellation checks after this boundary: admission cannot truncate a batch.
        return requests.Select(queue.Enqueue).ToArray();
    }

    public async Task<ConversionJob> RetryAsync(OperationRequest request, string? destinationDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Require(request.Editor is not null,"Typed retry requires an editor document.");
        Require(routes.TryGet(request.Operation.Id,out var route) && request.Operation.IsGroup == (route.Shape == EditorSubmissionShape.Group),"Editor route is unavailable.");
        var destination = destinationDirectory ?? request.Editor!.Export.DestinationDirectory ?? request.DestinationDirectory;
        var frozen = request with { Inputs = Array.AsReadOnly(request.Inputs.ToArray()), DestinationDirectory = destination, Editor = request.Editor! with { Export = request.Editor.Export with { DestinationDirectory = destination } } };
        ValidateDocument(frozen.Editor); ValidateRequest(frozen);
        var issues = await inspector.ValidateAsync(frozen,ct).ConfigureAwait(false);
        Require(!issues.IsDefault && issues.IsEmpty,"Source identity or inspection failed; reload to edit.");
        ct.ThrowIfCancellationRequested();
        return queue.Enqueue(frozen);
    }

    private static void ValidateDocument(EditorDocument doc)
    {
        var issues = EditorRecipeValidator.Validate(doc);
        Require(issues.IsEmpty,string.Join("; ",issues.Select(i => i.FieldPath + ": " + i.Message)));
    }
    private static void ValidateRequest(OperationRequest request)
    {
        var doc = request.Editor!;
        EditorExportPolicy.Resolve(request);
        var roots = EditorRecipeReferences.Roots(doc.Recipe);
        Require(roots.Length == request.Inputs.Count && request.Inputs.All(path => roots.Any(id => doc.Sources.Any(s => s.Id == id && EditorPathPolicy.Same(s.Path,path)))),"Retry root identity mismatch.");
    }
    private static void Require(bool condition,string message) { if (!condition) throw new ArgumentException(message); }
}
