namespace Citrus.Windows.Updates;

/// <summary>Handoff remains on the caller's dispatcher, with every admission/consent boundary checked after verification.</summary>
internal static class UpdateApplyGate
{
    internal static async Task<bool> VerifyAndApplyAsync(Func<bool> canApply, Func<CancellationToken, Task<bool>> verify,
        Action launch, CancellationToken ct)
    {
        if (!canApply()) return false;
        if (!await verify(ct)) return false;
        ct.ThrowIfCancellationRequested();
        if (!canApply()) return false;
        launch();
        return true;
    }
}
