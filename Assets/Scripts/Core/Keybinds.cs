using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Every rebindable action. The order here is the order the rebinding panel lists them in,
// so it is grouped by what a player thinks about together (movement, then combat, then UI)
// rather than by which script happens to read it.
public enum GameAction
{
    MoveForward,
    MoveBack,
    MoveLeft,
    MoveRight,
    Jump,
    Crouch,
    Dash,
    Fire,
    Scope,
    Grapple,
    Reload,
    Scoreboard,
    Pause,
    ToggleControls,
    ToggleDebug,
}

// One physical input: a keyboard key OR a mouse button, never both.
//
// Mouse is stored as button+1 rather than the raw index so that default(Bind) — which is what
// an uninitialised array element is — reads as UNSET. With a raw index, zero would mean left
// mouse button, and every unbound slot would silently fire on click.
public struct Bind : IEquatable<Bind>
{
    public Key key;            // Key.None unless this is a keyboard bind
    public int mousePlusOne;   // 0 unless this is a mouse bind; otherwise button index + 1

    public bool IsMouse => mousePlusOne > 0;
    public int MouseButton => mousePlusOne - 1;
    public bool IsSet => key != Key.None || mousePlusOne > 0;

    public static Bind FromKey(Key k) => new Bind { key = k, mousePlusOne = 0 };
    public static Bind FromMouse(int button) => new Bind { key = Key.None, mousePlusOne = button + 1 };

    public bool Equals(Bind other) => key == other.key && mousePlusOne == other.mousePlusOne;
    public override bool Equals(object o) => o is Bind b && Equals(b);
    public override int GetHashCode() => ((int)key * 397) ^ mousePlusOne;
}

// Player-remappable controls, persisted to PlayerPrefs.
//
// Every key was hardcoded across four scripts until now, which is unplayable for anyone on a
// non-QWERTY layout (WASD is nowhere near the left hand on AZERTY) and locks out anyone who
// cannot reach the default chord. It also meant the on-screen controls card was a hand-written
// string list that could — and did — drift from what the code actually read.
//
// Two slots per action (primary + alternate) because several controls genuinely want both:
// crouch has always been Ctrl-or-C, dash has always been either Shift.
public static class Keybinds
{
    public const int SlotCount = 2;

    static readonly int ActionCount = Enum.GetValues(typeof(GameAction)).Length;

    // [action, slot]. Always fully populated — see Reset, which every load path runs first.
    static Bind[,] binds;
    static bool loaded;

    // Bumped whenever a binding changes, so UI that caches formatted label strings can tell
    // when to rebuild them instead of re-formatting every action every OnGUI pass.
    public static int Version { get; private set; }

    // Every public entry point runs this rather than trusting somebody to have called Load
    // first. Script execution order is not guaranteed, and the failure mode of getting it
    // wrong is a null array inside Update — i.e. no controls at all, on one machine, sometimes.
    static void EnsureLoaded()
    {
        if (!loaded) Load();
    }

    public static void Load()
    {
        if (loaded) return;
        Reset();
        for (int a = 0; a < ActionCount; a++)
        {
            string s = PlayerPrefs.GetString(PrefKey((GameAction)a), null);
            if (string.IsNullOrEmpty(s)) continue;
            string[] parts = s.Split('|');
            for (int slot = 0; slot < SlotCount && slot < parts.Length; slot++)
                binds[a, slot] = Parse(parts[slot]);
        }
        // Migration: saves from before the Scoreboard action have ToggleControls persisted on
        // Tab, and Load just restored that on top of Scoreboard's new Tab default — two actions
        // on one key, both firing every press. Scoreboard wins the contest (it is why the key
        // moved); the card goes to the new F1 default.
        var tab = Bind.FromKey(Key.Tab);
        for (int slot = 0; slot < SlotCount; slot++)
            if (binds[(int)GameAction.ToggleControls, slot].Equals(tab)
                && (binds[(int)GameAction.Scoreboard, 0].Equals(tab)
                    || binds[(int)GameAction.Scoreboard, 1].Equals(tab)))
                binds[(int)GameAction.ToggleControls, slot] = Bind.FromKey(Key.F1);

        // Migration: profiles saved before the scope existed have Grapple on right mouse, and
        // Load has just restored that on top of Scope's new right-mouse default — two actions
        // on one button. Scope wins (moving it there is the entire point of the change) and
        // the grapple takes the thumb button, with Shift alongside it as before.
        var rmb = Bind.FromMouse(1);
        bool grappleOnRmb = binds[(int)GameAction.Grapple, 0].Equals(rmb)
                         || binds[(int)GameAction.Grapple, 1].Equals(rmb);
        bool scopeOnRmb = binds[(int)GameAction.Scope, 0].Equals(rmb)
                       || binds[(int)GameAction.Scope, 1].Equals(rmb);
        if (grappleOnRmb && scopeOnRmb)
        {
            for (int slot = 0; slot < SlotCount; slot++)
                if (binds[(int)GameAction.Grapple, slot].Equals(rmb))
                    binds[(int)GameAction.Grapple, slot] = default;
            binds[(int)GameAction.Grapple, 0] = Bind.FromMouse(3);
            binds[(int)GameAction.Grapple, 1] = Bind.FromKey(Key.LeftShift);
        }

        // Migration: profiles from the Shift-alternate layout keep Shift on the grapple, which
        // was half of a chord that was impossible to hold. Move that slot to Q.
        var shift = Bind.FromKey(Key.LeftShift);
        for (int slot = 0; slot < SlotCount; slot++)
            if (binds[(int)GameAction.Grapple, slot].Equals(shift))
                binds[(int)GameAction.Grapple, slot] = Bind.FromKey(Key.Q);

        loaded = true;
        Version++;
    }

    public static void Save()
    {
        EnsureLoaded();
        for (int a = 0; a < ActionCount; a++)
            PlayerPrefs.SetString(PrefKey((GameAction)a),
                Encode(binds[a, 0]) + "|" + Encode(binds[a, 1]));
        PlayerPrefs.Save();
    }

    // Restores the shipped layout. Does not save on its own — the panel that calls this owns
    // when to persist, same as the sliders.
    public static void Reset()
    {
        binds = new Bind[ActionCount, SlotCount];
        Set(GameAction.MoveForward, Key.W, Key.UpArrow);
        Set(GameAction.MoveBack, Key.S, Key.DownArrow);
        Set(GameAction.MoveLeft, Key.A, Key.LeftArrow);
        Set(GameAction.MoveRight, Key.D, Key.RightArrow);
        Set(GameAction.Jump, Key.Space, Key.None);
        Set(GameAction.Crouch, Key.LeftCtrl, Key.C);
        // Dash is SHELVED as a passive (see PassiveChoice), so its old Shift default would
        // now sit on top of the grapple's alternate and show as a duplicate in the rebinder.
        // Left unbound: if the passive ever comes back, it gets a key then.
        Set(GameAction.Dash, Key.None, Key.None);
        Set(GameAction.Reload, Key.R, Key.None);
        // Scope has no keyboard default any more: it lives on RIGHT MOUSE, below. Aiming down
        // sights is right-click in every shooter a player has touched, and fighting that
        // instinct costs more than any other binding decision here.
        Set(GameAction.Scope, Key.None, Key.None);
        // Tab = scoreboard (hold), the FPS-universal bind. The controls card lived on Tab
        // before the scoreboard existed and moved to F1 to make room — see Load's migration.
        Set(GameAction.Scoreboard, Key.Tab, Key.None);
        Set(GameAction.Pause, Key.Escape, Key.None);
        Set(GameAction.ToggleControls, Key.F1, Key.None);
        Set(GameAction.ToggleDebug, Key.F3, Key.None);
        binds[(int)GameAction.Fire, 0] = Bind.FromMouse(0);
        binds[(int)GameAction.Scope, 0] = Bind.FromMouse(1);   // right mouse — the instinct

        // Grapple on Q, reel on E, thumb button as the grapple alternate.
        //
        // The rope needs two inputs held together — swing and wind-in — and the previous
        // layout put them on Shift and Ctrl, which is the same pinky twice. Q and E are two
        // different fingers that are already resting next to WASD, so the chord is physically
        // possible at speed. Mouse4 stays on the grapple for anyone who prefers the thumb.
        binds[(int)GameAction.Grapple, 0] = Bind.FromKey(Key.Q);
        binds[(int)GameAction.Grapple, 1] = Bind.FromMouse(3); // Mouse4
        loaded = true; // a reset is a fully-formed set; a later Load must not clobber it
        Version++;

        void Set(GameAction a, Key primary, Key alt)
        {
            binds[(int)a, 0] = primary == Key.None ? default : Bind.FromKey(primary);
            binds[(int)a, 1] = alt == Key.None ? default : Bind.FromKey(alt);
        }
    }

    public static Bind Get(GameAction a, int slot)
    {
        EnsureLoaded();
        return binds[(int)a, Mathf.Clamp(slot, 0, SlotCount - 1)];
    }

    // Assigns a bind, stealing it from whoever else held it.
    //
    // Silently leaving a duplicate in place is the worse option: the player sees the key they
    // just chose listed against two actions and has no way to tell which one will win, because
    // both do — every frame.
    public static void Assign(GameAction action, int slot, Bind b)
    {
        if (slot < 0 || slot >= SlotCount) return;
        EnsureLoaded();

        if (b.IsSet)
        {
            for (int a = 0; a < ActionCount; a++)
                for (int s = 0; s < SlotCount; s++)
                {
                    if (a == (int)action && s == slot) continue;
                    if (binds[a, s].Equals(b)) binds[a, s] = default;
                }
        }

        binds[(int)action, slot] = b;

        // Pause must never end up with nothing on it — see Pressed, which keeps Escape as a
        // last resort, but a visibly empty Pause row still reads as "I have broken the menu".
        if (action == GameAction.Pause && !binds[(int)GameAction.Pause, 0].IsSet
            && !binds[(int)GameAction.Pause, 1].IsSet)
            binds[(int)GameAction.Pause, 0] = Bind.FromKey(Key.Escape);

        Version++;
    }

    public static void Clear(GameAction action, int slot) => Assign(action, slot, default);

    // True while either bound input is down this frame.
    public static bool Held(GameAction a)
    {
        EnsureLoaded();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var c = Control(binds[(int)a, slot]);
            if (c != null && c.isPressed) return true;
        }
        return false;
    }

    // True on the frame either bound input goes down.
    public static bool Pressed(GameAction a)
    {
        EnsureLoaded();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var c = Control(binds[(int)a, slot]);
            if (c != null && c.wasPressedThisFrame) return true;
        }

        // Escape always reaches the pause menu unless the player has deliberately given Escape
        // to something else. A player who rebinds Pause to a key their keyboard does not have
        // would otherwise be stuck in the match with no way back to the settings that did it.
        if (a == GameAction.Pause && !IsBoundElsewhere(Key.Escape, GameAction.Pause))
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
        }
        return false;
    }

    static bool IsBoundElsewhere(Key k, GameAction except)
    {
        var probe = Bind.FromKey(k);
        for (int a = 0; a < ActionCount; a++)
        {
            if (a == (int)except) continue;
            for (int s = 0; s < SlotCount; s++)
                if (binds[a, s].Equals(probe)) return true;
        }
        return false;
    }

    static ButtonControl Control(Bind b)
    {
        if (!b.IsSet) return null;

        if (b.IsMouse)
        {
            var m = Mouse.current;
            if (m == null) return null;
            switch (b.MouseButton)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.forwardButton;
                case 4: return m.backButton;
                default: return null;
            }
        }

        var kb = Keyboard.current;
        return kb != null ? kb[b.key] : null;
    }

    // --- display ------------------------------------------------------------------------

    public static string Label(Bind b)
    {
        if (!b.IsSet) return "—";
        return b.IsMouse ? MouseName(b.MouseButton) : KeyName(b.key);
    }

    // Both slots joined, for the controls card: "W / Up".
    public static string Label(GameAction a)
    {
        EnsureLoaded();
        string p = binds[(int)a, 0].IsSet ? Label(binds[(int)a, 0]) : null;
        string s = binds[(int)a, 1].IsSet ? Label(binds[(int)a, 1]) : null;
        if (p == null && s == null) return "unbound";
        if (p == null) return s;
        if (s == null) return p;
        return p + " / " + s;
    }

    public static string ActionName(GameAction a)
    {
        switch (a)
        {
            case GameAction.MoveForward: return "Move forward";
            case GameAction.MoveBack: return "Move back";
            case GameAction.MoveLeft: return "Strafe left";
            case GameAction.MoveRight: return "Strafe right";
            case GameAction.Jump: return "Jump";
            case GameAction.Crouch: return "Crouch / slide";
            case GameAction.Dash: return "Dash";
            case GameAction.Fire: return "Fire";
            case GameAction.Scope: return "Scope (hold)";
            case GameAction.Grapple: return "Grapple (hold)";
            case GameAction.Reload: return "Reload";
            case GameAction.Scoreboard: return "Scoreboard (hold)";
            case GameAction.Pause: return "Pause menu";
            case GameAction.ToggleControls: return "Controls card";
            case GameAction.ToggleDebug: return "Debug readout";
            default: return a.ToString();
        }
    }

    static string MouseName(int button)
    {
        switch (button)
        {
            case 0: return "LMB";
            case 1: return "RMB";
            case 2: return "MMB";
            case 3: return "Mouse4";
            case 4: return "Mouse5";
            default: return "Mouse" + button;
        }
    }

    // The Key enum names are code-shaped ("LeftCtrl", "Digit1", "UpArrow"). Printing them raw
    // onto a controls card asks the player to translate; these are what is written on the keys.
    static string KeyName(Key k)
    {
        switch (k)
        {
            case Key.LeftShift: return "Shift";
            case Key.RightShift: return "R-Shift";
            case Key.LeftCtrl: return "Ctrl";
            case Key.RightCtrl: return "R-Ctrl";
            case Key.LeftAlt: return "Alt";
            case Key.RightAlt: return "R-Alt";
            case Key.UpArrow: return "Up";
            case Key.DownArrow: return "Down";
            case Key.LeftArrow: return "Left";
            case Key.RightArrow: return "Right";
            case Key.Escape: return "Esc";
            case Key.Backspace: return "Bksp";
            case Key.PageUp: return "PgUp";
            case Key.PageDown: return "PgDn";
            case Key.CapsLock: return "Caps";
            case Key.NumpadEnter: return "Num Enter";
            case Key.Space: return "Space";
        }

        string s = k.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        if (s.StartsWith("Numpad")) return "Num " + s.Substring(6);
        return s;
    }

    // --- persistence format -------------------------------------------------------------

    static string PrefKey(GameAction a) => "bind_" + a; // by NAME, so reordering the enum is safe

    static string Encode(Bind b)
    {
        if (!b.IsSet) return "";
        return b.IsMouse ? "m:" + b.MouseButton : "k:" + b.key;
    }

    static Bind Parse(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 3) return default;
        string body = s.Substring(2);
        if (s[0] == 'm')
            return int.TryParse(body, out int btn) && btn >= 0 ? Bind.FromMouse(btn) : default;
        if (s[0] == 'k')
            return Enum.TryParse(body, out Key k) && k != Key.None ? Bind.FromKey(k) : default;
        return default;
    }
}
