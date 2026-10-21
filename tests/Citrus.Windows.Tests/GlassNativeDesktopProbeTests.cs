using System.Reflection;
using System.Text.Json;

namespace Citrus.Windows.Tests;

public sealed class GlassNativeDesktopProbeTests
{
    private static GlassDesktopSnapshot Desktop() => new(true, true, "WinSta0", "Default", "Default", true,
        1, 1, 0, 42, 1, ThreadDesktopHandle: 10, InputDesktopHandle: 20, WindowStationHandle: 30);

    [Fact]
    public void Reopened_input_handle_is_diagnostic_and_does_not_change_a_stable_desktop_identity()
    {
        var initial = Desktop();
        var assessment = Evaluate(initial, initial with { InputDesktopHandle = 21 });
        Assert.True(assessment.Stable);
        using var diagnostic = JsonDocument.Parse(assessment.Diagnostic);
        Assert.True(diagnostic.RootElement.GetProperty("ForegroundStable").GetBoolean());
        Assert.True(diagnostic.RootElement.GetProperty("InputDesktopStable").GetBoolean());
        Assert.Equal(20, diagnostic.RootElement.GetProperty("Initial").GetProperty("InputDesktopHandle").GetInt64());
        Assert.Equal(21, diagnostic.RootElement.GetProperty("Final").GetProperty("InputDesktopHandle").GetInt64());
    }

    [Fact]
    public void Actual_foreground_change_remains_rejected_and_records_both_identities()
    {
        var initial = Desktop();
        var assessment = Evaluate(initial, initial with { Foreground = 43 });
        Assert.False(assessment.Stable);
        using var diagnostic = JsonDocument.Parse(assessment.Diagnostic);
        Assert.False(diagnostic.RootElement.GetProperty("ForegroundStable").GetBoolean());
        Assert.Equal(42, diagnostic.RootElement.GetProperty("Initial").GetProperty("Foreground").GetInt64());
        Assert.Equal(43, diagnostic.RootElement.GetProperty("Final").GetProperty("Foreground").GetInt64());
    }

    [Fact]
    public void Failed_final_actual_input_desktop_check_remains_rejected_with_eligibility_reason()
    {
        var initial = Desktop();
        var assessment = Evaluate(initial, initial with { ThreadIsInputDesktop = false });
        Assert.False(assessment.Stable);
        using var diagnostic = JsonDocument.Parse(assessment.Diagnostic);
        Assert.True(diagnostic.RootElement.GetProperty("ForegroundStable").GetBoolean());
        Assert.False(diagnostic.RootElement.GetProperty("FinalEligibility").GetProperty("Allowed").GetBoolean());
        Assert.Equal("ThreadIsNotActualInputDesktop",
            diagnostic.RootElement.GetProperty("FinalEligibility").GetProperty("Reason").GetString());
    }

    [Fact]
    public void Desktop_names_and_process_session_remain_required_to_match_exactly()
    {
        var initial = Desktop();
        foreach (var final in new[]
        {
            initial with { ThreadDesktop = "default", InputDesktop = "default" },
            initial with { InputDesktop = "Winlogon" },
            initial with { ProcessSession = 2, ActiveConsoleSession = 2, ForegroundSession = 2 }
        })
            Assert.False(Evaluate(initial, final).Stable);
    }

    private static (bool Stable, string Diagnostic) Evaluate(GlassDesktopSnapshot initial, GlassDesktopSnapshot final)
    {
        var method = typeof(GlassNativeDesktopProbe).GetMethod("EvaluateDesktopTransition", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ((bool Stable, string Diagnostic))method.Invoke(null, [initial, final])!;
    }
}
