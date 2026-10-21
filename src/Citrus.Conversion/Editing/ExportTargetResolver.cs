using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing;

public sealed class ExportTargetResolver
{
    public ExportTarget Resolve(OperationRequest request) => EditorExportPolicy.Resolve(request);
}
