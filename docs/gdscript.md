# GDScript

The plugin is written in C#, and GDScript can use it through snake_case methods on the `EnhancedInput`
autoload. Actions are given by id or name (or as the resource); they are looked up among the player's
active mapping contexts and its config. Each method reads player 0 and has a `_for` variant taking the player
index (GDScript cannot use C# overloads or default arguments).

```gdscript
func _physics_process(delta):
    var move: Vector2 = EnhancedInput.get_vector("move")
    velocity.x = move.x * speed
    if is_on_floor() and EnhancedInput.consume_triggered("jump", 0.15):   # jump buffering
        velocity.y = jump_speed
    if EnhancedInput.was_pressed_in_physics("dash"):
        dash()

func _ready():
    EnhancedInput.activate_context("gameplay")
```

| Method | Returns |
|---|---|
| `is_triggered(action)` | held / triggered now |
| `was_pressed(action)`, `was_released(action)`, `was_triggered(action)` | edges of this frame |
| `was_pressed_in_physics(action)`, `was_released_in_physics(action)` | edges since the previous physics step |
| `get_axis(action)`, `get_vector(action)`, `get_vector3(action)`, `get_rotation(action)` | values |
| `consume_triggered(action, within_seconds)` | input buffering, once per press |
| `activate_context(context)`, `deactivate_context(context)` | by id, name or resource |
| `get_prompt(action)` | "Space / A" for the current device |
| `find_action(id)`, `get_player(index)` | the resource, the player node |

`..._for(action, player)` takes a player index (`was_pressed_for("jump", 1)`). The players' signals
(`ActionTriggered` and the others) can be connected from GDScript as usual, and every other public member is
callable on the player nodes under its C# name.
