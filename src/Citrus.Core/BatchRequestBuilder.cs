namespace Citrus.Core;

/// <summary>Freezes submission order; ordinary items remain independent jobs.</summary>
public static class BatchRequestBuilder
{
    public static bool RequiresDestination(IReadOnlyList<string> inputs) => inputs
        .Select(path => Path.GetDirectoryName(Path.GetFullPath(path)))
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Skip(1).Any();

    public static IReadOnlyList<OperationRequest> Build(OperationDescriptor operation, IReadOnlyList<string> inputs, OperationOptions options, string? destinationDirectory)
    {
        if (inputs.Count == 0) throw new ArgumentException("Choose at least one input.", nameof(inputs));
        var snapshot = Array.AsReadOnly(inputs.Select(Path.GetFullPath).ToArray());
        if (destinationDirectory is not null)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory) || !Path.IsPathFullyQualified(destinationDirectory))
                throw new ArgumentException("Choose an absolute destination folder.", nameof(destinationDirectory));
            destinationDirectory = Path.GetFullPath(destinationDirectory);
        }
        if (operation.IsGroup)
        {
            if (destinationDirectory is null && RequiresDestination(snapshot))
                throw new ArgumentException("Choose a destination folder for inputs from different folders.", nameof(destinationDirectory));
            return [new(operation, snapshot, options, destinationDirectory ?? Path.GetDirectoryName(snapshot[0]))];
        }
        return snapshot.Select(path => new OperationRequest(operation, Array.AsReadOnly(new[] { path }), options, destinationDirectory)).ToArray();
    }
}
