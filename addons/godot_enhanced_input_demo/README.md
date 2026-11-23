# Godot Enhanced Input — demo

A small top-down playground. Open `demo.tscn` and press F6 (Run Current Scene).

| Input | Action | Shows |
|---|---|---|
| WASD / left stick | Move | a key composite and a stick with a radial deadzone on one 2D action; normalizing at action level |
| Shift / LB | Sprint | polling `IsTriggered` every frame |
| Space / A | Jump | a pressed trigger, plus a chord blocker so it stays quiet while Dash fires |
| Shift + Space / B | Dash | a chord trigger |
| Jump, then Dash within 0.6 s | Spin | a combo trigger on a binding with no source |
| Hold E / X | Charge | a one-shot hold trigger; the bar follows `ElapsedSeconds` |
| Tap F | Inspect | a tap trigger… |
| Hold F | Pick up | …and a hold trigger on the same key |
| Left mouse / right trigger | Fire | pulse triggers; the analog trigger uses an actuation threshold |
| Esc / Start | Pause, Resume | a menu context with priority 100 that blocks gameplay, added with *ignore held sources until released* so the Escape press that opened it does not close it again |
| *in the pause menu* | Rebinding | click a binding, press the new input; conflicts swap; the profile is saved to `user://godot_enhanced_input_demo/bindings.json`. Jump and Dash share one rebind id (both use Space), and so do Pause and Resume across the two contexts |

The panel on the left lists every action's phase, value and elapsed time; the log at the
bottom shows the events (started, triggered, completed, canceled).

All input lives in resources you can open in the Inspector: `actions/*.tres` and
`contexts/gameplay.tres` / `contexts/menu.tres`. They are generated from `DemoContent.cs`
(run the test suite with `GEI_WRITE_DEMO=1` to regenerate them after changing it).

This folder is not needed by the plugin; delete it once you have had a look.
