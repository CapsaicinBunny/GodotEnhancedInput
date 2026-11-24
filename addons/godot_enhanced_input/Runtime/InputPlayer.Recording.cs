using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>Emitted when a replay started with <see cref="StartReplay"/> has played its last tick (or was stopped).</summary>
    [Signal] public delegate void ReplayFinishedEventHandler();

    private InputRecording _recording;
    private RecordedTick _recordingPending;
    private InputRecording _replay;
    private int _replayIndex;
    private bool _replayFeeding;
    private bool _replayEndsThisTick;

    /// <summary>Whether input is being recorded.</summary>
    public bool IsRecording => _recording != null;

    /// <summary>Whether a recording is being replayed (live input is ignored meanwhile).</summary>
    public bool IsReplaying => _replay != null;

    /// <summary>The next tick of the replay to play (0-based).</summary>
    public int ReplayPosition => _replayIndex;

    /// <summary>
    /// Starts recording everything this player receives (events, virtual controls, injections) and the
    /// time step of every tick. For an exact replay, start while nothing is held (e.g. at level start)
    /// and replay into a player with the same contexts, mode, profile and settings.
    /// </summary>
    public InputRecording StartRecording(string notes = null)
    {
        _recording = new InputRecording { Notes = notes ?? string.Empty };
        _recordingPending = new RecordedTick();
        return _recording;
    }

    /// <summary>Stops recording and returns the recording (input received after the last tick is dropped).</summary>
    public InputRecording StopRecording()
    {
        var recording = _recording;
        _recording = null;
        _recordingPending = null;
        return recording;
    }

    /// <summary>
    /// Plays a recording back from the next tick on: each <see cref="Tick"/> feeds one recorded tick's
    /// input and uses its recorded time step instead of the one passed in. Raw input is cleared first;
    /// live input, motion sensors and InputMap polling are ignored until the replay ends.
    /// </summary>
    public void StartReplay(InputRecording recording)
    {
        if (recording == null) return;
        StopRecording();
        ClearRawInput(InputEndReason.Replay);
        Raw.Frozen = true;   // live cut-offs (disconnects, focus loss) do not reach the replayed state
        _replay = recording;
        _replayIndex = 0;
        _replayEndsThisTick = false;
        if (recording.Ticks.Count == 0) FinishReplay();
    }

    /// <summary>Stops a replay early. Live input is accepted again.</summary>
    public void StopReplay()
    {
        if (_replay == null) return;
        FinishReplay();
    }

    private void FinishReplay()
    {
        // Whatever the replay still held ends, as cut off by the replay (natural end or stopped early).
        Raw.Frozen = false;
        if (_replay != null) ClearRawInput(InputEndReason.Replay);
        _replay = null;
        _replayIndex = 0;
        _replayEndsThisTick = false;
        EmitSignal(SignalName.ReplayFinished);
    }

    /// <summary>
    /// A backend device control reaching this player: ignored during a replay, recorded while recording,
    /// like every other input.
    /// </summary>
    internal void ReceiveDeviceControl(in SourceKey key, Vector4 value, bool tracked = false)
    {
        if (LiveInputBlocked) return;
        if (_recording != null) _recordingPending.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.DeviceControl, Name = key.Name, Device = key.Device, Value = value, Tracked = tracked, Stamp = Raw.Clock() });
        if (tracked) Raw.MarkTracked(key);
        Raw.Set(key, value);
        if (!tracked) TrackCustomDevice(key, value);
    }

    /// <summary>A backend device control lost its value (tracking lost): cut off, recorded like other cut-offs.</summary>
    internal void ClearDeviceControl(in SourceKey key)
    {
        if (LiveInputBlocked) return;
        Raw.Cancel(key, InputEndReason.TrackingLost);
    }

    /// <summary>A polled sensor value (recorded while recording; replays play it back instead of polling).</summary>
    internal void ReceiveSensor(MotionSensor sensor, int device, Vector4 value)
    {
        if (LiveInputBlocked) return;
        var key = SourceKey.ForMotion(sensor, device);
        if (_recording != null && Raw.GetCurrent(key) != value)
            _recordingPending.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.Sensor, Sensor = sensor, Device = device, Value = value, Stamp = Raw.Clock() });
        Raw.Set(key, value);
    }

    /// <summary>True when live input must be ignored because a replay is running.</summary>
    private bool LiveInputBlocked => _replay != null && !_replayFeeding;

    private void Record(RecordedInput input)
    {
        if (_recording == null || _replayFeeding) return;
        input.Stamp = Raw.Clock();
        _recordingPending.Inputs.Add(input);
    }

    private void RecordEvent(InputEvent @event, IntakeStage stage)
    {
        if (_recording == null || _replayFeeding) return;
        if (@event.Duplicate() is InputEvent copy)
            _recordingPending.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.Event, Event = copy, Stage = stage, Stamp = Raw.Clock() });
    }

    /// <summary>Records a cut-off of raw input (one channel, or everything when <paramref name="key"/> is null).</summary>
    private void RecordCutOff(SourceKey? key, InputEndReason reason)
    {
        if (_recording == null || _replayFeeding) return;
        _recordingPending.Inputs.Add(key is { } k
            ? new RecordedInput { Kind = RecordedInputKind.Cancel, ChannelKind = k.Kind, Code = k.Code, Device = k.Device, Name = k.Name, Reason = reason, Stamp = Raw.Clock() }
            : new RecordedInput { Kind = RecordedInputKind.ClearAll, Reason = reason, Stamp = Raw.Clock() });
    }

    /// <summary>
    /// Called at the start of a tick (after live sensors were polled): closes the recorded tick — placing
    /// each input at the moment within it that it arrived — or feeds the next replayed one at its recorded
    /// moments. Returns the delta to use.
    /// </summary>
    private double BeginTickRecording(double delta, ulong now)
    {
        if (_replay != null)
        {
            var tick = _replay.Ticks[_replayIndex++];
            _replayFeeding = true;
            bool frozen = Raw.Frozen;
            Raw.Frozen = false;
            try
            {
                foreach (var input in tick.Inputs)
                {
                    Raw.FractionOverride = input.At;
                    Play(input);
                }
            }
            finally
            {
                Raw.FractionOverride = double.NaN;
                Raw.Frozen = frozen;
                _replayFeeding = false;
            }
            if (_replayIndex >= _replay.Ticks.Count) _replayEndsThisTick = true;
            return tick.Delta;
        }

        if (_recording != null)
        {
            ulong start = _lastFrameUsec;
            foreach (var input in _recordingPending.Inputs)
                input.At = UseEventTimes && now > start && input.Stamp > start ? System.Math.Min(1.0, (double)(input.Stamp - start) / (now - start)) : 0.0;
            _recordingPending.Delta = delta;
            _recording.Ticks.Add(_recordingPending);
            _recordingPending = new RecordedTick();
        }
        return delta;
    }

    private void EndTickRecording()
    {
        if (_replayEndsThisTick) FinishReplay();
    }

    private void Play(RecordedInput input)
    {
        switch (input.Kind)
        {
            case RecordedInputKind.Event:
                if (input.Event != null) FeedEvent(input.Event, input.Stage);
                break;
            case RecordedInputKind.Virtual:
                SetVirtualControl(input.Name, input.Value);
                break;
            case RecordedInputKind.VirtualDelta:
                AddToVirtualControl(input.Name, input.Value);
                break;
            case RecordedInputKind.Inject:
                InjectInput(FindAction(input.Name), input.Value);
                break;
            case RecordedInputKind.InjectStart:
                StartInjecting(FindAction(input.Name), input.Value);
                break;
            case RecordedInputKind.InjectStop:
                StopInjecting(FindAction(input.Name));
                break;
            case RecordedInputKind.DeviceControl:
            {
                var key = new SourceKey(RawKind.DeviceControl, 0, input.Device, input.Name);
                if (input.Tracked) Raw.MarkTracked(key);
                Raw.Set(key, input.Value);
                break;
            }
            case RecordedInputKind.Sensor:
                Raw.Set(SourceKey.ForMotion(input.Sensor, input.Device), input.Value);
                break;
            case RecordedInputKind.Cancel:
                Raw.Cancel(new SourceKey(input.ChannelKind, input.Code, input.Device, input.Name), input.Reason);
                break;
            case RecordedInputKind.ClearAll:
                ClearRawInput(input.Reason);
                break;
        }
    }
}
