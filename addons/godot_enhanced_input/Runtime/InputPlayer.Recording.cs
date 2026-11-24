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
        ClearRawInput();
        _replay = recording;
        _replayIndex = 0;
        _replayEndsThisTick = false;
        if (recording.Ticks.Count == 0) FinishReplay();
    }

    /// <summary>Stops a replay early. Live input is accepted again.</summary>
    public void StopReplay()
    {
        if (_replay == null) return;
        ClearRawInput();
        FinishReplay();
    }

    private void FinishReplay()
    {
        _replay = null;
        _replayIndex = 0;
        _replayEndsThisTick = false;
        EmitSignal(SignalName.ReplayFinished);
    }

    /// <summary>True when live input must be ignored because a replay is running.</summary>
    private bool LiveInputBlocked => _replay != null && !_replayFeeding;

    private void Record(RecordedInput input)
    {
        if (_recording != null && !_replayFeeding) _recordingPending.Inputs.Add(input);
    }

    private void RecordEvent(InputEvent @event, IntakeStage stage)
    {
        if (_recording == null || _replayFeeding) return;
        if (@event.Duplicate() is InputEvent copy)
            _recordingPending.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.Event, Event = copy, Stage = stage });
    }

    /// <summary>Called at the start of a tick: closes the recorded tick, or feeds the next replayed one. Returns the delta to use.</summary>
    private double BeginTickRecording(double delta)
    {
        if (_replay != null)
        {
            var tick = _replay.Ticks[_replayIndex++];
            _replayFeeding = true;
            try
            {
                foreach (var input in tick.Inputs) Play(input);
            }
            finally
            {
                _replayFeeding = false;
            }
            if (_replayIndex >= _replay.Ticks.Count) _replayEndsThisTick = true;
            return tick.Delta;
        }

        if (_recording != null)
        {
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
        }
    }
}
