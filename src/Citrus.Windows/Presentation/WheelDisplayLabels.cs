using Citrus.Core;
namespace Citrus.Windows.Presentation;
internal static class WheelDisplayLabels
{
    internal static string Petal(OperationDescriptor operation) => operation.Id == "image.convert.gif" ? "GIF" : operation.Label;
}
