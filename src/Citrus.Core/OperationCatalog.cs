namespace Citrus.Core;

public sealed class OperationCatalog(IEnumerable<IConversionEngine> engines)
{
    private readonly IConversionEngine[] engines = engines.ToArray();

    public IReadOnlyList<OperationDescriptor> GetOperations(IReadOnlyList<string> paths, OperationMode mode)
    {
        if (paths.Count == 0) return [];
        return engines.SelectMany(engine => engine.Operations)
            .Where(operation => operation.Mode == mode)
            .DistinctBy(operation => operation.Id)
            .Where(operation => operation.IsGroup
                ? Supports(operation, paths)
                : paths.All(path => Supports(operation, [path])))
            .ToArray();
    }

    private bool Supports(OperationDescriptor operation, IReadOnlyList<string> paths) =>
        engines.Any(engine => engine.Operations.Any(supported => supported.Id == operation.Id) &&
            engine.CanExecute(new(operation, paths, new(), null)));
}
