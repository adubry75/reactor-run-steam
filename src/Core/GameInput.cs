using Godot;

namespace ReactorRun;

/// <summary>Registers every action in code (keyboard + gamepad) so project.godot stays tiny and nothing is hand-serialized.</summary>
public static class GameInput
{
    public const string Left = "rr_left", Right = "rr_right", Up = "rr_up", Down = "rr_down";
    public const string Fire = "rr_fire", Overclock = "rr_overclock", Confirm = "rr_confirm", Back = "rr_back";
    public const string Pause = "rr_pause", Music = "rr_music", Fullscreen = "rr_fullscreen";
    public const string NavUp = "rr_nav_up", NavDown = "rr_nav_down", NavLeft = "rr_nav_left", NavRight = "rr_nav_right";

    public static void Register()
    {
        Add(Left, 0.3f, Keys(Key.Left, Key.A), Axis(JoyAxis.LeftX, -1f), Pad(JoyButton.DpadLeft));
        Add(Right, 0.3f, Keys(Key.Right, Key.D), Axis(JoyAxis.LeftX, 1f), Pad(JoyButton.DpadRight));
        Add(Up, 0.3f, Keys(Key.Up, Key.W), Axis(JoyAxis.LeftY, -1f), Pad(JoyButton.DpadUp), Pad(JoyButton.A));
        Add(Down, 0.3f, Keys(Key.Down, Key.S), Axis(JoyAxis.LeftY, 1f), Pad(JoyButton.DpadDown));
        Add(Fire, 0.3f, Keys(Key.Space, Key.J), Pad(JoyButton.X), Axis(JoyAxis.TriggerRight, 1f));
        Add(Overclock, 0.3f, Keys(Key.Q), Pad(JoyButton.Y), Axis(JoyAxis.TriggerLeft, 1f));
        Add(Confirm, 0.5f, Keys(Key.Enter, Key.KpEnter, Key.Space), Pad(JoyButton.A), Pad(JoyButton.Start));
        Add(Back, 0.5f, Keys(Key.Backspace), Pad(JoyButton.B));
        Add(Pause, 0.5f, Keys(Key.P, Key.Escape), Pad(JoyButton.Start));
        Add(Music, 0.5f, Keys(Key.M), Pad(JoyButton.Back));
        Add(Fullscreen, 0.5f, Keys(Key.F11));
        Add(NavUp, 0.6f, Keys(Key.Up, Key.W), Axis(JoyAxis.LeftY, -1f), Pad(JoyButton.DpadUp));
        Add(NavDown, 0.6f, Keys(Key.Down, Key.S), Axis(JoyAxis.LeftY, 1f), Pad(JoyButton.DpadDown));
        Add(NavLeft, 0.6f, Keys(Key.Left, Key.A), Axis(JoyAxis.LeftX, -1f), Pad(JoyButton.DpadLeft));
        Add(NavRight, 0.6f, Keys(Key.Right, Key.D), Axis(JoyAxis.LeftX, 1f), Pad(JoyButton.DpadRight));
    }

    static void Add(string action, float deadzone, params InputEvent[][] groups)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, deadzone);
        foreach (var group in groups)
            foreach (var ev in group)
                InputMap.ActionAddEvent(action, ev);
    }

    static InputEvent[] Keys(params Key[] keys)
    {
        var list = new InputEvent[keys.Length];
        for (int i = 0; i < keys.Length; i++) list[i] = new InputEventKey { PhysicalKeycode = keys[i] };
        return list;
    }

    static InputEvent[] Pad(JoyButton b) => new InputEvent[] { new InputEventJoypadButton { ButtonIndex = b } };
    static InputEvent[] Axis(JoyAxis a, float v) => new InputEvent[] { new InputEventJoypadMotion { Axis = a, AxisValue = v } };

    public static bool Held(string a) => Input.IsActionPressed(a);
    public static bool Hit(string a) => Input.IsActionJustPressed(a);
    public static float MoveX() => Input.GetAxis(Left, Right);
}
