using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;

namespace Citrus.Windows.Tests;

/// <summary>Bounded post-exit observation for the filename negative control only.</summary>
internal static class AvifControlCleanup
{
    internal static void RequireExclusiveAfterExit(Action requireExclusive, Action<string> trace)
    {
        var clock = Stopwatch.StartNew();
        RequireExclusiveAfterExit(requireExclusive, trace, () => clock.Elapsed, Thread.Sleep);
    }

    // Clock/delay injection keeps exception selection and deadline coverage portable;
    // the native caller still performs the actual exclusive open and nonempty-file check.
    internal static void RequireExclusiveAfterExit(Action requireExclusive, Action<string> trace,
        Func<TimeSpan> elapsed, Action<TimeSpan> delay)
    {
        var budget = TimeSpan.FromSeconds(2);
        ExceptionDispatchInfo? original = null;
        int attempt = 0;
        void Record(string outcome, int hresult) => trace(FormattableString.Invariant(
            $"FILENAME_POST_EXIT:attempt={attempt};elapsed_ms={elapsed().TotalMilliseconds:F3};hresult=0x{hresult:X8};{outcome}"));
        void Deadline()
        {
            Record("DEADLINE_EXCEEDED", original!.SourceException.HResult);
            original.Throw();
        }

        while (true)
        {
            if (original is not null && elapsed() >= budget) Deadline();
            attempt++;
            try
            {
                requireExclusive();
            }
            catch (IOException failure) when (failure.HResult == unchecked((int)0x80070020))
            {
                original ??= ExceptionDispatchInfo.Capture(failure);
                Record(attempt == 1 ? "IMMEDIATE_FAILURE" : "SHARING_VIOLATION", failure.HResult);
                var remaining = budget - elapsed();
                if (remaining <= TimeSpan.Zero) Deadline();
                delay(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
                continue;
            }
            catch (Exception failure)
            {
                Record("TERMINAL_FAILURE", failure.HResult);
                throw;
            }

            // A delayed successful open is evidence only if observed within the budget.
            if (original is not null && elapsed() > budget) Deadline();
            Record(attempt == 1 ? "IMMEDIATE_UNLOCK" : "EVENTUAL_UNLOCK", 0);
            return;
        }
    }
}
