namespace Citrus.Core.Presentation;

/// <summary>Physical value and velocity (units/second), independent of presentation clamping.</summary>
public readonly record struct SpringState(double Value, double Velocity, double Target);

/// <summary>Response is a natural frequency parameter, not a duration. Only critical damping is supported.</summary>
public readonly record struct SpringParameters(double ResponseSeconds, double ValueEpsilon, double VelocityEpsilon)
{
    public static SpringParameters Default => new(.34, .001, .01);
    public static SpringParameters Press => new(.30, .001, .01);
    public static SpringParameters Reveal => new(.38, .001, .01);
}

public static class CriticalSpring
{
    public static SpringState Advance(SpringState state, SpringParameters parameters, double elapsedSeconds)
    {
        Validate(state, parameters);
        Finite(elapsedSeconds, nameof(elapsedSeconds));
        if (elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds > .25 || IsSettled(state, parameters)) return new(state.Target, 0, state.Target);
        if (elapsedSeconds == 0) return state;
        double omega = 2 * Math.PI / parameters.ResponseSeconds;
        double displacement = state.Value - state.Target;
        double c = state.Velocity + omega * displacement;
        double decay = Math.Exp(-omega * elapsedSeconds);
        var next = new SpringState(state.Target + (displacement + c * elapsedSeconds) * decay,
            (state.Velocity - omega * c * elapsedSeconds) * decay, state.Target);
        Validate(next, parameters);
        return IsSettled(next, parameters) ? new(state.Target, 0, state.Target) : next;
    }

    public static SpringState Retarget(SpringState state, double target)
    {
        ValidateState(state);
        Finite(target, nameof(target));
        return state with { Target = target };
    }

    public static bool IsSettled(SpringState state, SpringParameters parameters)
    {
        Validate(state, parameters);
        return Math.Abs(state.Value - state.Target) <= parameters.ValueEpsilon &&
            Math.Abs(state.Velocity) <= parameters.VelocityEpsilon;
    }

    private static void Validate(SpringState state, SpringParameters parameters)
    {
        ValidateState(state);
        Finite(parameters.ResponseSeconds, nameof(parameters.ResponseSeconds));
        Finite(parameters.ValueEpsilon, nameof(parameters.ValueEpsilon));
        Finite(parameters.VelocityEpsilon, nameof(parameters.VelocityEpsilon));
        if (parameters.ResponseSeconds is < .05 or > 2) throw new ArgumentOutOfRangeException(nameof(parameters.ResponseSeconds));
        if (parameters.ValueEpsilon <= 0) throw new ArgumentOutOfRangeException(nameof(parameters.ValueEpsilon));
        if (parameters.VelocityEpsilon <= 0) throw new ArgumentOutOfRangeException(nameof(parameters.VelocityEpsilon));
    }

    private static void ValidateState(SpringState state)
    {
        Finite(state.Value, nameof(state.Value));
        Finite(state.Velocity, nameof(state.Velocity));
        Finite(state.Target, nameof(state.Target));
    }

    private static void Finite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}
