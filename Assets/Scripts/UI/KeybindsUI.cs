using System;
using UnityEngine;
using UnityEngine.InputSystem;

// The rebinding panel. Opened from the pause menu and from the connect screen, because the
// player who most needs it is the one who has not started a match yet.
//
// Capture runs in Tick (called from Update), never in Draw. OnGUI fires several times per
// frame — once to lay out, once to repaint — so reading "was this key pressed this frame"
// from inside it would assign the same key two or three times and, worse, would read input
// during Layout that the Repaint pass then disagrees about.
public static class KeybindsUI
{
    public static bool Open;

    const float RowHeight = 26f;
    const float PanelWidth = 540f;

    static readonly GameAction[] Actions = (GameAction[])Enum.GetValues(typeof(GameAction));

    static bool capturing;
    static GameAction captureAction;
    static int captureSlot;
    // Set once every mouse button is up. Without it the click that opens capture is itself
    // captured, and every rebind attempt instantly binds LMB.
    static bool captureArmed;

    static int lastTickFrame = -1;
    static int consumedFrame = -1;

    // True on any frame this panel used a key press for itself. The pause menu checks it before
    // acting on its own Escape: closing the rebinder and unpausing off one press would have the
    // player pressing Escape to leave the controls screen and finding themselves back in the
    // match, which reads as the menu losing their bindings.
    public static bool ConsumedInput => consumedFrame == Time.frameCount;
    static GUIStyle panel, label, hint, slot, slotEmpty, slotCapturing;

    public static void Toggle()
    {
        Open = !Open;
        if (!Open) CancelCapture();
    }

    public static void Close()
    {
        Open = false;
        CancelCapture();
    }

    // Call from Update. Safe to call from several components in the same frame — the second
    // call in a frame does nothing, so GameMenu and ConnectUI can both drive it without
    // consuming each other's key presses.
    public static void Tick()
    {
        if (lastTickFrame == Time.frameCount) return;
        lastTickFrame = Time.frameCount;

        if (!Open) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (!capturing)
        {
            // Escape leaves the panel. Handled here rather than by the pause menu so there is
            // exactly one owner of the key while this is up.
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                consumedFrame = Time.frameCount;
                Close();
            }
            return;
        }

        if (!captureArmed)
        {
            bool anythingDown = (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed
                                 || mouse.middleButton.isPressed))
                                || (kb != null && kb.anyKey.isPressed);
            if (!anythingDown) captureArmed = true;
            return;
        }

        if (kb != null)
        {
            // Escape backs out without changing anything; Backspace unbinds the slot. Neither
            // can be bound from here, which is the accepted trade — Escape is the universal
            // "get me out" and a rebind screen with no way out is how you lose a player.
            if (kb.escapeKey.wasPressedThisFrame) { CancelCapture(); return; }
            if (kb.backspaceKey.wasPressedThisFrame)
            {
                Keybinds.Clear(captureAction, captureSlot);
                CommitCapture();
                return;
            }

            foreach (var control in kb.allKeys)
            {
                if (!control.wasPressedThisFrame) continue;
                Keybinds.Assign(captureAction, captureSlot, Bind.FromKey(control.keyCode));
                CommitCapture();
                return;
            }
        }

        if (mouse != null)
        {
            int button = -1;
            if (mouse.leftButton.wasPressedThisFrame) button = 0;
            else if (mouse.rightButton.wasPressedThisFrame) button = 1;
            else if (mouse.middleButton.wasPressedThisFrame) button = 2;
            else if (mouse.forwardButton.wasPressedThisFrame) button = 3;
            else if (mouse.backButton.wasPressedThisFrame) button = 4;

            if (button >= 0)
            {
                Keybinds.Assign(captureAction, captureSlot, Bind.FromMouse(button));
                CommitCapture();
            }
        }
    }

    static void BeginCapture(GameAction a, int s)
    {
        capturing = true;
        captureAction = a;
        captureSlot = s;
        captureArmed = false;
    }

    static void CommitCapture()
    {
        capturing = false;
        consumedFrame = Time.frameCount;
        Keybinds.Save();
    }

    static void CancelCapture()
    {
        if (capturing) consumedFrame = Time.frameCount;
        capturing = false;
    }

    public static float Height => 78f + Actions.Length * RowHeight + 84f;

    // Full-screen modal. Drawn by whichever screen is up; they are mutually exclusive (the
    // connect screen hides once a match starts, the pause menu only draws once one has), so
    // there is no frame where both would draw this.
    public static void Draw()
    {
        if (!Open) return;
        EnsureStyles();

        GUI.depth = -200; // above the pause overlay, which is already at -100

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float w = PanelWidth, h = Mathf.Min(Height, Screen.height - 40f);
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;

        GUI.Box(new Rect(x, y, w, h), "CONTROLS", panel);

        float rowY = y + 40f;
        GUI.Label(new Rect(x + 16f, rowY, w - 32f, 20f),
            capturing
                ? "Press any key or mouse button   ·   [Backspace] unbind   ·   [Esc] cancel"
                : "Click a binding to change it.", hint);
        rowY += 26f;

        const float nameW = 220f, gap = 8f;
        float slotW = (w - 32f - nameW - gap * 2f) * 0.5f;

        foreach (var action in Actions)
        {
            GUI.Label(new Rect(x + 16f, rowY, nameW, RowHeight), Keybinds.ActionName(action), label);

            for (int s = 0; s < Keybinds.SlotCount; s++)
            {
                var r = new Rect(x + 16f + nameW + gap + s * (slotW + gap), rowY + 2f, slotW,
                    RowHeight - 4f);
                bool isTarget = capturing && captureAction == action && captureSlot == s;
                var b = Keybinds.Get(action, s);
                string text = isTarget ? "…" : Keybinds.Label(b);
                var style = isTarget ? slotCapturing : b.IsSet ? slot : slotEmpty;

                // While capturing, rows are inert boxes rather than buttons: the click that
                // binds LMB would otherwise ALSO register as pressing whatever button sits
                // under the cursor, retargeting capture to a row the player never chose.
                if (capturing) GUI.Box(r, text, style);
                else if (GUI.Button(r, text, style)) BeginCapture(action, s);
            }

            rowY += RowHeight;
        }

        rowY += 10f;
        if (!capturing && GUI.Button(new Rect(x + 16f, rowY, w - 32f, 30f),
                "Reset controls to default"))
        {
            Keybinds.Reset();
            Keybinds.Save();
        }
        else if (capturing)
        {
            GUI.Box(new Rect(x + 16f, rowY, w - 32f, 30f), "Reset controls to default", slotEmpty);
        }

        rowY += 36f;
        if (!capturing && GUI.Button(new Rect(x + 16f, rowY, w - 32f, 32f), "Done")) Close();
        else if (capturing) GUI.Box(new Rect(x + 16f, rowY, w - 32f, 32f), "Done", slotEmpty);
    }

    static void EnsureStyles()
    {
        if (panel != null) return;
        panel = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 15,
        };
        label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        label.normal.textColor = Color.white;
        hint = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        slot = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        slotEmpty = new GUIStyle(slot);
        slotEmpty.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        slotCapturing = new GUIStyle(slot) { fontStyle = FontStyle.Bold };
        slotCapturing.normal.textColor = new Color(1f, 0.85f, 0.35f);
    }
}
