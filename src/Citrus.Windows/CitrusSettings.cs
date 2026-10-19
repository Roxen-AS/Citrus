using Citrus.Core;

namespace Citrus.Windows;

public enum ThemeChoice { System, Light, Dark }
public sealed record CitrusSettings
{
    public bool SoundEnabled { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public bool ReduceTransparency { get; init; }
    public bool LaunchAtSignIn { get; init; }
    // Fresh installations start with the neutral light presentation; saved choices are retained.
    public ThemeChoice Theme { get; init; } = ThemeChoice.Light;
    public bool RevealCompletedOutputs { get; init; }
    public bool GesturesEnabled { get; init; } = true;
    public OperationMode ShiftMode { get; init; } = OperationMode.Convert;
    public OperationMode AltMode { get; init; } = OperationMode.Tools;
    public OperationMode ControlMode { get; init; } = OperationMode.Actions;
    public OperationMode Remap(OperationMode original) => original switch
    { OperationMode.Convert => ShiftMode, OperationMode.Tools => AltMode, _ => ControlMode };
    public void Validate()
    {
        if (!Enum.IsDefined(Theme) || !Enum.IsDefined(ShiftMode) || !Enum.IsDefined(AltMode) || !Enum.IsDefined(ControlMode) ||
            new[] { ShiftMode, AltMode, ControlMode }.Distinct().Count() != 3)
            throw new ArgumentException("Assign Convert, Tools and Actions to different gesture keys and choose a valid theme.");
    }
}

