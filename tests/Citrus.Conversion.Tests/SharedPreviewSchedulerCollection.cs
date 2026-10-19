namespace Citrus.Conversion.Tests;

// These tests deliberately hold both process-wide preview leases. Running their
// separate acquisitions concurrently can deadlock two tests holding one lease each.
// Keep real cancellation assertions/timeouts; isolate only the shared test resource.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedPreviewSchedulerCollection
{
    public const string Name = "Shared preview scheduler";
}
