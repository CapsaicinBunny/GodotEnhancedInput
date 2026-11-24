using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The queue of on/off changes, the steps a tick evaluates it in, cut-offs and tracked presence.
/// </summary>
public sealed partial class RawInputState
{
    /// <summary>A channel's queued on/off changes (a small ring buffer).</summary>
    private sealed class ChannelQueue
    {
        public SourceKey Key;
        public Vector4 Base;          // the value observed before the first queued change
        private Entry[] _entries = new Entry[4];
        private int _head;
        public int Count;
        public bool Active;

        private struct Entry
        {
            public Vector4 Value;
            public long Seq;
            public long Group;
            public ulong Stamp;
            public double Fraction;   // NaN unless given (replays)
            public int Cancel;        // −1, or the InputEndReason of a cut-off
        }

        private ref Entry At(int i) => ref _entries[(_head + i) % _entries.Length];

        public Vector4 First => _entries[_head].Value;
        public long FirstSeq => _entries[_head].Seq;
        public long FirstGroup => _entries[_head].Group;
        public ulong FirstStamp => _entries[_head].Stamp;
        public double FirstFraction => _entries[_head].Fraction;
        public int FirstCancel => _entries[_head].Cancel;
        public long SeqAt(int i) => At(i).Seq;
        public Vector4 Last => At(Count - 1).Value;

        public void ReplaceLast(Vector4 value) => At(Count - 1).Value = value;

        public void Push(Vector4 value, long seq, long group, ulong stamp, double fraction, int cancel)
        {
            if (Count == _entries.Length)
            {
                var entries = new Entry[_entries.Length * 2];
                for (int i = 0; i < Count; i++) entries[i] = At(i);
                _entries = entries;
                _head = 0;
            }
            _entries[(_head + Count) % _entries.Length] = new Entry
            {
                Value = value, Seq = seq, Group = group, Stamp = stamp, Fraction = fraction, Cancel = cancel,
            };
            Count++;
        }

        public void PopFirst()
        {
            Base = _entries[_head].Value;
            _head = (_head + 1) % _entries.Length;
            Count--;
        }

        public void Clear()
        {
            Count = 0;
            _head = 0;
        }
    }

    /// <summary>Starts a group: every change made until <see cref="EndEventGroup"/> belongs to one input event.</summary>
    public void BeginEventGroup()
    {
        if (_groupDepth++ == 0) _currentGroup = ++_groupCounter;
    }

    /// <summary>Ends a group started with <see cref="BeginEventGroup"/>.</summary>
    public void EndEventGroup()
    {
        if (_groupDepth > 0) _groupDepth--;
    }

    private long NextGroup() => _groupDepth > 0 ? _currentGroup : ++_groupCounter;

    private void Enqueue(ChannelQueue queue, Vector4 value, Vector4 before, int cancel)
    {
        if (queue.Count == 0) queue.Base = before;
        queue.Push(value, ++_sequence, NextGroup(), Clock(), FractionOverride, cancel);
        if (!queue.Active) { queue.Active = true; _active.Add(queue); }
    }

    /// <summary>The value the current step observes: its first queued change if it belongs to this step, else the last observed value.</summary>
    private Vector4 Observed(in SourceKey key)
    {
        if (!_pending.TryGetValue(key, out var queue) || queue.Count == 0) return GetCurrent(key);
        return queue.FirstSeq < _boundary ? queue.First : queue.Base;
    }

    /// <summary>Whether queued changes are waiting for another evaluation step.</summary>
    public bool HasPendingChanges
    {
        get
        {
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].Count > 0) return true;
            return false;
        }
    }

    /// <summary>
    /// Sets the time span the coming tick covers (microseconds on <see cref="Clock"/>), for placing changes
    /// within it. Without <paramref name="useStamps"/>, every change counts as arriving at the start.
    /// </summary>
    public void SetFrameWindow(ulong start, ulong end, bool useStamps)
    {
        _windowStart = start;
        _windowEnd = end;
        _useStamps = useStamps;
    }

    /// <summary>
    /// Starts the next event step: the earliest queued input event's changes (up to a channel that changes
    /// twice in it). Fills <paramref name="changed"/> with the channels it changes and gives the fraction of
    /// the tick at which it arrived (0…1). False when nothing is queued.
    /// </summary>
    public bool NextEventStep(List<SourceKey> changed, out double fraction)
    {
        changed.Clear();
        _canceled.Clear();
        ChannelQueue first = null;
        for (int i = 0; i < _active.Count; i++)
        {
            var q = _active[i];
            if (q.Count > 0 && (first == null || q.FirstSeq < first.FirstSeq)) first = q;
        }
        if (first == null)
        {
            fraction = 0;
            return false;
        }
        long group = first.FirstGroup;
        _boundary = long.MaxValue;
        for (int i = 0; i < _active.Count; i++)
        {
            var q = _active[i];
            if (q.Count == 0) continue;
            if (q.FirstGroup != group && q.FirstSeq < _boundary) _boundary = q.FirstSeq;
            if (q.Count >= 2 && q.SeqAt(1) < _boundary) _boundary = q.SeqAt(1);
        }
        for (int i = 0; i < _active.Count; i++)
        {
            var q = _active[i];
            if (q.Count == 0 || q.FirstSeq >= _boundary) continue;
            changed.Add(q.Key);
            if (q.FirstCancel >= 0) _canceled[q.Key] = (InputEndReason)q.FirstCancel;
        }
        fraction = FractionOf(first);
        _frameStep = false;
        return true;
    }

    private double FractionOf(ChannelQueue q)
    {
        double f = q.FirstFraction;
        if (double.IsNaN(f))
        {
            if (!_useStamps || _windowEnd <= _windowStart || q.FirstStamp <= _windowStart) return 0.0;
            f = (double)(q.FirstStamp - _windowStart) / (_windowEnd - _windowStart);
        }
        return f < 0 ? 0 : f > 1 ? 1 : f;
    }

    /// <summary>Within an event step: read the channels as they were before its changes (time passing up to it).</summary>
    public void ObserveBefore()
    {
        _savedBoundary = _boundary;
        _boundary = long.MinValue;
    }

    /// <summary>Within an event step: read the channels with its changes applied.</summary>
    public void ObserveStep() => _boundary = _savedBoundary;

    /// <summary>Starts the tick's final step: settled values plus the frame's per-frame amounts.</summary>
    public void BeginFinalStep()
    {
        _boundary = long.MinValue;
        _canceled.Clear();
        _frameStep = true;
    }

    /// <summary>Ends an evaluation step: the changes it observed become the channels' settled values.</summary>
    public void EndStep()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var q = _active[i];
            if (q.Count > 0 && q.FirstSeq < _boundary) q.PopFirst();
            if (q.Count == 0)
            {
                q.Active = false;
                _active[i] = _active[^1];
                _active.RemoveAt(_active.Count - 1);
            }
        }
        _boundary = long.MaxValue;
        _canceled.Clear();
    }

    /// <summary>Whether a channel was cut off this step (device lost, focus lost…) rather than released, and why.</summary>
    public bool WasCanceled(in SourceKey key, out InputEndReason reason)
    {
        if (_canceled.TryGetValue(key, out reason)) return true;
        if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
            foreach (var pair in _canceled)
                if (pair.Key.Kind == key.Kind && pair.Key.Code == key.Code && pair.Key.Name == key.Name) { reason = pair.Value; return true; }
        return false;
    }

    /// <summary>Whether any channel was cut off this step.</summary>
    public bool AnyCanceled => _canceled.Count > 0;

    /// <summary>
    /// Cuts a channel off: it goes to zero as a queued change that carries <paramref name="reason"/>, so the
    /// tick sees it in order with everything else and actions end canceled instead of released. The
    /// channel also stops being tracked (see <see cref="IsPresent"/>).
    /// </summary>
    public void Cancel(in SourceKey key, InputEndReason reason)
    {
        if (Frozen) return;
        _present.Remove(key);
        _pending.TryGetValue(key, out var queue);
        var last = queue != null && queue.Count > 0 ? queue.Last : GetCurrent(key);
        _current[key] = Vector4.Zero;
        if (last == Vector4.Zero) return;
        if (queue == null) _pending[key] = queue = new ChannelQueue { Key = key };
        Enqueue(queue, Vector4.Zero, queue.Count > 0 ? queue.Base : last, (int)reason);
        if (!_clearing) CutOff?.Invoke(key, reason);
    }

    /// <summary>Whether a channel has a value now (written and not cut off since) — a tracked pose at the origin is present.</summary>
    public bool IsPresent(in SourceKey key)
    {
        if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
        {
            foreach (var d in DevicesOf(key.Kind))
                if (_present.Contains(key with { Device = d })) return true;
            return false;
        }
        return _present.Contains(key);
    }

    /// <summary>Marks a channel as a tracked quantity (a pose, position or gaze): while present it counts as input even at zero or identity.</summary>
    public void MarkTracked(in SourceKey key) => _absolute.Add(key);

    /// <summary>Whether a channel is a tracked quantity that currently has a value.</summary>
    public bool IsTracked(in SourceKey key)
    {
        if (_absolute.Count == 0) return false;
        if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
        {
            foreach (var d in DevicesOf(key.Kind))
            {
                var exact = key with { Device = d };
                if (_absolute.Contains(exact) && _present.Contains(exact)) return true;
            }
            return false;
        }
        return _absolute.Contains(key) && _present.Contains(key);
    }
}
