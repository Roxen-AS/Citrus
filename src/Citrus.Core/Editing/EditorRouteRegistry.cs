namespace Citrus.Core.Editing;

/// <summary>The composition root supplies only renderer/host pairs that passed capability probes.</summary>
public sealed class EditorRouteRegistry
{
    private readonly Dictionary<string,EditorRoute> routes;
    public EditorRouteRegistry(IEnumerable<EditorRoute>? enabledRoutes = null) => routes = (enabledRoutes ?? []).ToDictionary(r => r.OperationId, StringComparer.Ordinal);
    public bool TryGet(string operationId, out EditorRoute route) => routes.TryGetValue(operationId,out route!);
}
