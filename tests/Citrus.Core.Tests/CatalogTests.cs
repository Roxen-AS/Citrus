using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class CatalogTests
{
    private static readonly OperationDescriptor Png = new("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false);
    private static readonly OperationDescriptor Resize = new("image.resize", "Resize", OperationMode.Tools, null, false, true);
    private static readonly OperationDescriptor Merge = new("pdf.merge", "Merge PDF", OperationMode.Tools, ".pdf", true, false);

    [Fact]
    public void Ordinary_operations_are_intersected_across_inputs_and_filtered_by_mode()
    {
        var engine = new TestEngine([Png, Resize], r => r.Inputs.Count == 1 && r.Inputs[0].EndsWith(".png"));
        var catalog = new OperationCatalog([engine]);
        Assert.Equal([Png], catalog.GetOperations(["a.png", "b.png"], OperationMode.Convert));
        Assert.Equal([Resize], catalog.GetOperations(["a.png"], OperationMode.Tools));
        Assert.Empty(catalog.GetOperations(["a.png", "b.mp3"], OperationMode.Convert));
        Assert.Empty(catalog.GetOperations([], OperationMode.Convert));
    }

    [Fact]
    public void Different_registered_engines_can_supply_same_operation_for_different_inputs()
    {
        var catalog = new OperationCatalog([
            new TestEngine([Png], r => r.Inputs.All(p => p.EndsWith(".jpg"))),
            new TestEngine([Png], r => r.Inputs.All(p => p.EndsWith(".webp")))]);
        Assert.Equal([Png], catalog.GetOperations(["a.jpg", "b.webp"], OperationMode.Convert));
    }

    [Fact]
    public void Group_operations_use_whole_ordered_selection()
    {
        var catalog = new OperationCatalog([new TestEngine([Merge], r => r.Inputs.SequenceEqual(["b.pdf", "a.pdf"]))]);
        Assert.Equal([Merge], catalog.GetOperations(["b.pdf", "a.pdf"], OperationMode.Tools));
        Assert.Empty(catalog.GetOperations(["a.pdf", "b.pdf"], OperationMode.Tools));
        Assert.Empty(catalog.GetOperations(["a.pdf"], OperationMode.Tools));
    }

    [Fact]
    public void Option_defaults_match_shared_contract()
    {
        var options = new OperationOptions();
        Assert.Null(options.Width); Assert.Null(options.Height); Assert.Null(options.EndSeconds); Assert.Null(options.PageSelection);
        Assert.Equal(85, options.Quality); Assert.Equal(90, options.RotationDegrees); Assert.Equal(0, options.StartSeconds);
        Assert.Equal(1, options.Speed); Assert.Equal(-16, options.NormalizeLufs); Assert.True(options.PreserveAspectRatio);
    }
}

internal sealed class TestEngine(IReadOnlyList<OperationDescriptor> operations, Func<OperationRequest, bool> canExecute,
    Func<OperationRequest, IProgress<ConversionProgress>?, CancellationToken, Task<ConversionResult>>? execute = null) : IConversionEngine
{
    public IReadOnlyList<OperationDescriptor> Operations => operations;
    public bool CanExecute(OperationRequest request) => canExecute(request);
    public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken) =>
        execute is null ? Task.FromResult(new ConversionResult([request.Inputs[0] + ".out"])) : execute(request, progress, cancellationToken);
}
