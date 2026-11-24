using Godot;

namespace GodotEnhancedInput;

/// <summary>Turning Godot input events into channel changes, with the GUI rules of each intake stage.</summary>
public sealed partial class RawInputState
{
    /// <summary>
    /// Applies a Godot input event. With <see cref="IntakeStage.Early"/> only changes that lower a value
    /// are applied; with <see cref="IntakeStage.Unhandled"/> only changes that raise one;
    /// <see cref="IntakeStage.Direct"/> applies everything. Returns true when the event changed anything.
    /// </summary>
    public bool Apply(InputEvent @event, IntakeStage stage)
    {
        BeginEventGroup();
        try
        {
            return ApplyEvent(@event, stage);
        }
        finally
        {
            EndEventGroup();
        }
    }

    private bool ApplyKey(InputEventKey key, float v, KeyLocation side)
    {
        bool any = false;
        var physical = SourceKey.ForKey(key.PhysicalKeycode, KeyMatch.Physical, side);
        if (key.PhysicalKeycode != Key.None) { Set(physical, v); any = true; }
        if (key.Keycode != Key.None)
        {
            var logical = SourceKey.ForKey(key.Keycode, KeyMatch.Logical, side);
            if (key.PhysicalKeycode != Key.None) _physicalOf[logical] = physical;
            Set(logical, v);
            any = true;
        }
        if (key.KeyLabel != Key.None)
        {
            var label = SourceKey.ForKey(key.KeyLabel, KeyMatch.Label, side);
            if (key.PhysicalKeycode != Key.None) _physicalOf[label] = physical;
            Set(label, v);
            any = true;
        }
        return any;
    }

    private bool ApplyEvent(InputEvent @event, IntakeStage stage)
    {
        switch (@event)
        {
            case InputEventKey key:
            {
                if (key.Echo) return false;
                if (!StageAllows(stage, raising: key.Pressed)) return false;
                float v = key.Pressed ? 1f : 0f;
                bool any = ApplyKey(key, v, KeyLocation.Unspecified);
                // Left and right Shift, Ctrl, Alt and Meta also have a channel of their own side.
                if (key.Location != KeyLocation.Unspecified) ApplyKey(key, v, key.Location);
                return any;
            }
            case InputEventMouse emulated when IgnoreTouchEmulatedMouse && emulated.Device == InputEvent.DeviceIdEmulation:
                return false;
            case InputEventMouseButton mouseButton:
            {
                if (!StageAllows(stage, raising: mouseButton.Pressed)) return false;
                bool wheel = IsWheel(mouseButton.ButtonIndex);
                float strength = mouseButton.Factor > 0f && wheel ? mouseButton.Factor : 1f;
                Set(SourceKey.ForMouseButton(mouseButton.ButtonIndex), mouseButton.Pressed ? strength : 0f);
                if (wheel && mouseButton.Pressed) _wheel += WheelDirection(mouseButton.ButtonIndex) * strength;
                if (stage != IntakeStage.Unhandled) _position = mouseButton.Position;
                if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
                {
                    Set(SourceKey.ForPen(PenChannel.Pressure), 0f);
                    Set(SourceKey.ForPen(PenChannel.Eraser), 0f);
                }
                return true;
            }
            case InputEventMouseMotion motion:
            {
                // Position is never contested by the GUI; movement is "raising" input.
                if (stage != IntakeStage.Unhandled)
                {
                    _position = motion.Position;
                    // Pen data rides on motion events (a plain mouse reports zero pressure and no tilt).
                    if (motion.Pressure > 0f || motion.Tilt != Vector2.Zero || motion.PenInverted || GetCurrent(SourceKey.ForPen(PenChannel.Pressure)) != Vector4.Zero)
                    {
                        Set(SourceKey.ForPen(PenChannel.Pressure), motion.Pressure);
                        Set(SourceKey.ForPen(PenChannel.Tilt), V4(motion.Tilt.X, motion.Tilt.Y, 0f));
                        Set(SourceKey.ForPen(PenChannel.Eraser), motion.PenInverted && motion.Pressure > 0f ? 1f : 0f);
                    }
                }
                if (StageAllows(stage, raising: true)) _motion += motion.Relative;
                return true;
            }
            case InputEventPanGesture pan:
            {
                if (!StageAllows(stage, raising: true)) return false;
                _wheel += pan.Delta;
                return true;
            }
            case InputEventJoypadButton joyButton:
            {
                if (!StageAllows(stage, raising: joyButton.Pressed)) return false;
                Set(SourceKey.ForJoyButton(joyButton.ButtonIndex, joyButton.Device), joyButton.Pressed ? 1f : 0f);
                return true;
            }
            case InputEventJoypadMotion joyMotion:
            {
                var key = SourceKey.ForJoyAxis(joyMotion.Axis, joyMotion.Device);
                float previous = GetCurrent(key).X;
                float value = joyMotion.AxisValue;
                if (previous != 0f && value != 0f && Mathf.Sign(value) != Mathf.Sign(previous))
                {
                    // A reversal is a release of the old direction and a press of the new one. The release
                    // is never held back by the GUI; the press must pass it like any other.
                    if (stage == IntakeStage.Early) { Set(key, 0f); return true; }
                    if (stage == IntakeStage.Direct) { Set(key, 0f); Set(key, value); return true; }
                    previous = 0f;   // Unhandled: the early stage already released it
                }
                // Toward zero lowers the current direction: never let the GUI hold it back.
                bool raising = Mathf.Abs(value) > Mathf.Abs(previous);
                if (!StageAllows(stage, raising)) return false;
                Set(key, value);
                return true;
            }
            case InputEventScreenTouch touch:
            {
                bool down = touch.Pressed && !touch.Canceled;
                if (!StageAllows(stage, raising: down)) return false;
                GestureTouch(touch.Index, touch.Position, down, touch.Canceled);
                Set(SourceKey.ForTouch(TouchChannel.Position, touch.Index), V4(touch.Position.X, touch.Position.Y, 0f));
                var pressed = SourceKey.ForTouch(TouchChannel.Pressed, touch.Index);
                // A touch the system took away (a system gesture, an incoming call) was not lifted.
                if (touch.Canceled) Cancel(pressed, InputEndReason.SystemCanceled);
                else Set(pressed, down ? 1f : 0f);
                return true;
            }
            case InputEventScreenDrag drag:
            {
                // Like the mouse: position is never contested; movement is "raising" input.
                if (stage != IntakeStage.Unhandled)
                    Set(SourceKey.ForTouch(TouchChannel.Position, drag.Index), V4(drag.Position.X, drag.Position.Y, 0f));
                if (StageAllows(stage, raising: true))
                {
                    AddTouchDrag(drag.Index, drag.Relative);
                    GestureDrag(drag.Index, drag.Position, drag.Velocity);
                }
                return true;
            }
            case InputEventMagnifyGesture magnify:
            {
                if (!StageAllows(stage, raising: true)) return false;
                _pinch += magnify.Factor - 1f;
                return true;
            }
            case InputEventAction action:
            {
                if (!StageAllows(stage, raising: action.Pressed)) return false;
                Set(SourceKey.ForInputMapAction(action.Action), action.Pressed ? action.Strength : 0f);
                return true;
            }
        }
        return false;
    }

    private static bool StageAllows(IntakeStage stage, bool raising) => stage switch
    {
        IntakeStage.Early => !raising,
        IntakeStage.Unhandled => raising,
        _ => true,
    };

    private void GestureTouch(int finger, Vector2 position, bool down, bool canceled)
    {
        if (down)
        {
            _gestureFingers[finger] = position;
            _gestureTracks[finger] = (position, Vector2.Zero);
            return;
        }
        if (_gestureTracks.TryGetValue(finger, out var track) && !canceled && _gestureFingers.Count == 1)
        {
            var travel = position - track.Start;
            if (travel.Length() >= SwipeMinDistance && track.Velocity.Length() >= SwipeMinSpeed)
                _swipe = travel.Normalized();
        }
        _gestureFingers.Remove(finger);
        _gestureTracks.Remove(finger);
    }

    private void GestureDrag(int finger, Vector2 position, Vector2 velocity)
    {
        if (!_gestureFingers.ContainsKey(finger))
        {
            _gestureFingers[finger] = position;
            _gestureTracks[finger] = (position, velocity);
            return;
        }
        if (_gestureTracks.TryGetValue(finger, out var track)) _gestureTracks[finger] = (track.Start, velocity);

        if (_gestureFingers.Count == 2)
        {
            int other = -1;
            foreach (var f in _gestureFingers.Keys)
                if (f != finger) other = f;
            var otherPosition = _gestureFingers[other];
            var before = _gestureFingers[finger] - otherPosition;
            var after = position - otherPosition;
            if (before.Length() > 1f)
            {
                _pinch += after.Length() / before.Length() - 1f;
                _rotate += before.AngleTo(after);
            }
            var midpointBefore = (_gestureFingers[finger] + otherPosition) * 0.5f;
            var midpointAfter = (position + otherPosition) * 0.5f;
            _twoFingerDrag += midpointAfter - midpointBefore;
        }
        _gestureFingers[finger] = position;
    }

    private static Vector2 WheelDirection(MouseButton button) => button switch
    {
        MouseButton.WheelUp => new Vector2(0f, -1f),
        MouseButton.WheelDown => new Vector2(0f, 1f),
        MouseButton.WheelLeft => new Vector2(-1f, 0f),
        _ => new Vector2(1f, 0f),
    };

    private static bool IsWheel(MouseButton button)
        => button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight;
}
