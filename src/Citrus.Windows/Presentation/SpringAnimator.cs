using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

/// <summary>Dispatcher-bound shared scheduler. Only active channels subscribe to WPF rendering.</summary>
public sealed class SpringAnimator : DispatcherObject, IDisposable
{
    private readonly Func<long> clock;
    private readonly Func<bool> systemMotionEnabled;
    // Ephemeron storage: an apply closure may capture its owner without rooting it through this scheduler.
    private readonly ConditionalWeakTable<object, Dictionary<string, Channel>> owners = new();
    private readonly Dictionary<long, WeakReference<Channel>> active = new();
    private long nextId;
    private long timestamp;
    private bool disposed;
    private bool motionEnabled = true;

    public SpringAnimator() : this(Stopwatch.GetTimestamp,
        () => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) { }

    internal SpringAnimator(Func<long> clock, Func<bool> systemMotionEnabled)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.systemMotionEnabled = systemMotionEnabled ?? throw new ArgumentNullException(nameof(systemMotionEnabled));
        timestamp = clock();
    }

    /// <summary>App supplies effective reduced-motion policy. Turning it off settles and releases all owners.</summary>
    public bool MotionEnabled
    {
        get => motionEnabled;
        set
        {
            VerifyAccess();
            ObjectDisposedException.ThrowIf(disposed, this);
            motionEnabled = value;
            if (!value) SettleAndClear();
        }
    }

    internal int ActiveChannelCount => active.Count;
    internal int RetainedChannelCount => owners.Sum(pair => pair.Value.Count);
    internal bool IsRenderingSubscribed { get; private set; }
    internal int RenderingSubscriptionCount => IsRenderingSubscribed ? 1 : 0;

    public void SetImmediate(object owner, string channel, double value, Action<double> apply)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateArguments(owner, channel, apply);
        var state = CriticalSpring.Retarget(new(value, 0, value), value);
        timestamp = Math.Max(timestamp, clock());
        var channels = owners.GetOrCreateValue(owner);
        if (!channels.TryGetValue(channel, out var entry))
            channels.Add(channel, entry = new(++nextId, owner));
        active.Remove(entry.Id);
        entry.State = state;
        entry.Apply = apply;
        entry.Timestamp = timestamp;
        UpdateSubscription();
        apply(value);
    }

    public void Retarget(object owner, string channel, double target, SpringParameters parameters, Action<double> apply)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateArguments(owner, channel, apply);
        // Validate before mutating or invoking any presentation callback.
        CriticalSpring.IsSettled(new(target, 0, target), parameters);
        if (!owners.TryGetValue(owner, out var channels) || !channels.TryGetValue(channel, out var entry))
            throw new InvalidOperationException("SetImmediate must initialize a channel before its first Retarget.");
        if (!motionEnabled || !systemMotionEnabled())
        {
            SettleAndClear();
            apply(target);
            return;
        }
        AdvanceAt(clock());
        // Presentation callbacks can unload/cancel an owner during advancement.
        if (!owners.TryGetValue(owner, out channels) || !channels.TryGetValue(channel, out entry)) return;
        entry.State = CriticalSpring.Retarget(entry.State, target);
        entry.Parameters = parameters;
        entry.Apply = apply;
        entry.Timestamp = timestamp;
        if (!motionEnabled || !systemMotionEnabled() || CriticalSpring.IsSettled(entry.State, parameters))
        {
            entry.State = new(target, 0, target);
            active.Remove(entry.Id);
        }
        else active[entry.Id] = new(entry);
        UpdateSubscription();
        apply(entry.State.Value);
    }

    public void CancelOwner(object owner, bool settle)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(owner);
        if (disposed || !owners.TryGetValue(owner, out var channels)) return;
        owners.Remove(owner);
        var entries = channels.Values.ToArray();
        foreach (var entry in entries) active.Remove(entry.Id);
        UpdateSubscription();
        if (settle)
            foreach (var entry in entries)
            {
                if (disposed) break;
                entry.Apply(entry.State.Target);
            }
    }

    internal bool HasChannel(object owner, string channel) => owners.TryGetValue(owner, out var channels) && channels.ContainsKey(channel);

    internal SpringState GetState(object owner, string channel) => owners.TryGetValue(owner, out var channels) &&
        channels.TryGetValue(channel, out var entry) ? entry.State : throw new InvalidOperationException("Channel is not initialized.");

    /// <summary>Same monotonic clock path used by rendering and input; stale timestamps never rewind state.</summary>
    internal void AdvanceAt(long stopwatchTimestamp)
    {
        VerifyAccess();
        if (disposed) return;
        timestamp = Math.Max(timestamp, stopwatchTimestamp);
        if (!motionEnabled || !systemMotionEnabled()) { SettleAndClear(); return; }
        foreach (var pair in active.ToArray())
        {
            if (!pair.Value.TryGetTarget(out var entry) || !entry.Owner.TryGetTarget(out var owner))
            {
                active.Remove(pair.Key);
                continue;
            }
            if (!active.ContainsKey(pair.Key)) continue; // A prior apply may cancel another owner.
            double elapsed = (timestamp - entry.Timestamp) / (double)Stopwatch.Frequency;
            if (elapsed <= 0) continue;
            entry.Timestamp = timestamp;
            entry.State = CriticalSpring.Advance(entry.State, entry.Parameters, elapsed);
            if (CriticalSpring.IsSettled(entry.State, entry.Parameters))
            {
                entry.State = new(entry.State.Target, 0, entry.State.Target);
                active.Remove(entry.Id);
            }
            entry.Apply(entry.State.Value);
            GC.KeepAlive(owner);
        }
        UpdateSubscription();
    }

    public void Dispose()
    {
        VerifyAccess();
        if (disposed) return;
        disposed = true;
        active.Clear();
        owners.Clear();
        UpdateSubscription(); // Never call back into disposed visuals.
    }

    private void OnRendering(object? sender, EventArgs args) => AdvanceAt(clock());
    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Dispatcher.CheckAccess())
        {
            if (!systemMotionEnabled()) SettleAndClear();
        }
        else Dispatcher.BeginInvoke(() => { if (!disposed && !systemMotionEnabled()) SettleAndClear(); });
    }

    private void SettleAndClear()
    {
        var entries = owners.SelectMany(pair => pair.Value.Values).ToArray();
        active.Clear();
        owners.Clear();
        UpdateSubscription();
        foreach (var entry in entries)
        {
            if (disposed) break;
            entry.Apply(entry.State.Target);
        }
    }

    private void UpdateSubscription()
    {
        bool needed = !disposed && active.Count > 0;
        if (needed == IsRenderingSubscribed) return;
        if (needed)
        {
            CompositionTarget.Rendering += OnRendering;
            SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        }
        IsRenderingSubscribed = needed;
    }

    private static void ValidateArguments(object owner, string channel, Action<double> apply)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(apply);
    }

    private sealed class Channel(long id, object owner)
    {
        public long Id { get; } = id;
        public WeakReference<object> Owner { get; } = new(owner);
        public SpringState State;
        public SpringParameters Parameters;
        public Action<double> Apply = null!;
        public long Timestamp;
    }
}
