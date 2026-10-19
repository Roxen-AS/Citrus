using Citrus.Windows.Tests;

namespace Citrus.Core.Tests;

public sealed class AvifControlCleanupTests
{
    [Fact]
    public void Sharing_violation_can_unlock_before_deadline_and_records_immediate_failure()
    {
        var trace = new List<string>();
        TimeSpan elapsed = TimeSpan.Zero;
        int attempts = 0;
        AvifControlCleanup.RequireExclusiveAfterExit(() =>
        {
            if (++attempts < 3) throw SharingViolation();
        }, trace.Add, () => elapsed, delay => elapsed += delay);
        Assert.Equal(3, attempts);
        Assert.Contains(trace, line => line.Contains("attempt=1") && line.Contains("IMMEDIATE_FAILURE") && line.Contains("hresult=0x80070020"));
        Assert.Contains(trace, line => line.Contains("attempt=3") && line.Contains("elapsed_ms=50") && line.Contains("EVENTUAL_UNLOCK"));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070021))]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x80130020))]
    public void Other_io_failures_are_rethrown_without_retry(int hresult)
    {
        var failure = new IOException("unrelated failure", hresult);
        int attempts = 0;
        var trace = new List<string>();
        var actual = Assert.Throws<IOException>(() => AvifControlCleanup.RequireExclusiveAfterExit(
            () => { attempts++; throw failure; }, trace.Add, () => TimeSpan.Zero,
            _ => throw new InvalidOperationException("Must not delay.")));
        Assert.Same(failure, actual);
        Assert.Equal(1, attempts);
        Assert.Contains(trace, line => line.Contains("TERMINAL_FAILURE") && line.Contains($"hresult=0x{hresult:X8}"));
    }

    [Fact]
    public void Deadline_rethrows_original_failure_and_does_not_probe_after_two_seconds()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        var original = SharingViolation();
        var trace = new List<string>();
        int attempts = 0;
        var actual = Assert.Throws<IOException>(() => AvifControlCleanup.RequireExclusiveAfterExit(
            () => { attempts++; throw attempts == 1 ? original : SharingViolation(); }, trace.Add,
            () => elapsed, delay => elapsed += delay));
        Assert.Same(original, actual);
        Assert.Equal(TimeSpan.FromSeconds(2), elapsed);
        Assert.Equal(80, attempts);
        Assert.Contains(trace, line => line.Contains("DEADLINE_EXCEEDED") && line.Contains("elapsed_ms=2000"));
    }

    [Fact]
    public void Immediate_success_has_no_delay_and_is_distinct_from_eventual_unlock()
    {
        var trace = new List<string>();
        AvifControlCleanup.RequireExclusiveAfterExit(() => { }, trace.Add, () => TimeSpan.Zero,
            _ => throw new InvalidOperationException("Must not delay."));
        Assert.Contains(trace, line => line.Contains("attempt=1") && line.Contains("IMMEDIATE_UNLOCK"));
        Assert.DoesNotContain(trace, line => line.Contains("EVENTUAL_UNLOCK"));
    }

    private static IOException SharingViolation() => new("sharing violation", unchecked((int)0x80070020));
}
