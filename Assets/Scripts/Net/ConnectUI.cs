using FishNet;
using FishNet.Transporting;
using UnityEngine;

// Connect screen. Replaces FishNet's demo HUD, which hardcodes localhost and so can only
// ever connect a build to itself — useless for playing with anyone else.
//
// Lets you type the host's address before joining, which is the one code-side requirement
// shared by every route to internet play: a mesh VPN (Tailscale/ZeroTier) gives you a
// virtual LAN IP, port-forwarding gives you a public IP, and a relay gives you an
// allocation address. All of them need somewhere to put that address.
//
// Shows only until connected, then gets out of the way.
public class ConnectUI : MonoBehaviour
{
    // Address and port live in GameSettings so they survive a restart. They were serialized
    // fields resetting to localhost every launch, which is fine while testing against your own
    // machine and miserable once a group plays on one fixed host — everybody retyped the same
    // string every session. The inspector values below are only the FIRST-RUN defaults; after
    // that the saved ones win.
    [Tooltip("First-run default only. Once a player connects, their last address is remembered. " +
             "localhost = same machine; a LAN, tunnel or public address reaches another machine.")]
    public string defaultAddress = "localhost";
    public ushort defaultPort = 7770;

    [Tooltip("Port the SERVER binds when you Host. Separate from the port clients dial, because " +
             "behind a tunnel or a port-forward the two are different numbers: the tunnel " +
             "listens publicly on one port and forwards to this one. Leave at 7770 unless the " +
             "tunnel's local target says otherwise.")]
    public ushort hostBindPort = 7770;

    string address = "localhost";
    ushort port = 7770;

    GUIStyle label, field, button, selected, error, hint;
    Texture2D panel;
    // Through NetPresence: this is read from OnGUI, and InstanceFinder logs a stack trace
    // every time it is asked for a manager that is not there yet — which, on the connect
    // screen, is the normal state.
    bool Started => NetPresence.IsRunning;

    // True while the connect panel owns the screen. Read by the player components so the
    // local player stands still behind it.
    //
    // SampleScene contains a scene-placed test player (the map scenes do not), so in the
    // editor there is a fully live, fully controllable character standing behind the connect
    // screen: you could walk, shoot and grapple around the map while "at the main menu".
    // Freezing on this flag fixes it for every entry point at once — including the moment
    // after Leave match, when the spawned player has not been despawned yet.
    public static bool MenuOpen { get; private set; }

    // A host attempt is in flight. Without this, clicking Host again while the first attempt is
    // still resolving subscribes OnServerState a SECOND time — and every extra subscription
    // runs LoadChosenMap and StartConnection again once one finally succeeds.
    bool hosting;

    // Same for an explicit Client press. Tracked separately from `hosting` because hosting also
    // starts a local client, and a host's client connecting must not be reported as a join.
    bool joining;

    // Shown on the panel when a connection attempt fails. The failure was previously silent:
    // the server reported "port unavailable" to the console and the connect screen just sat
    // there, so the only symptom a player sees is that the Host button does nothing.
    string lastError;

    void Awake()
    {
        GameSettings.Load();

        // First run has nothing saved, so seed the store from the inspector values and keep
        // them in step from then on.
        if (string.IsNullOrEmpty(GameSettings.Address)) GameSettings.Address = defaultAddress;
        if (GameSettings.Port == 0) GameSettings.Port = defaultPort;

        address = GameSettings.Address;
        port = GameSettings.Port;
    }

    // Rebind capture has to be driven from Update — see KeybindsUI.Tick. Harmless when the
    // panel is shut, and idempotent if GameMenu is ticking it in the same frame.
    void Update()
    {
        KeybindsUI.Tick();
        // Computed here rather than in OnGUI: OnGUI runs several times a frame and not at all
        // on frames Unity skips repaint, so a flag set there would flicker.
        MenuOpen = NetPresence.HasNetworkManager && !NetPresence.IsRunning;
    }

    void OnDisable() => MenuOpen = false;

    void OnGUI()
    {
        // Silent probe — InstanceFinder logs a stack trace per access when no NetworkManager
        // exists, and OnGUI runs several times a frame (see NetPresence).
        var nm = NetPresence.Manager;
        if (nm == null) return;

        // Modal, and IMGUI has no notion of one: a button drawn underneath the rebinder is
        // still clickable through it, so hosting a match is one stray click away while you are
        // remapping. Drawing nothing else is the only way to actually block it.
        if (KeybindsUI.Open) { KeybindsUI.Draw(); return; }

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            label.normal.textColor = Color.white;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            selected = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            selected.normal.textColor = new Color(1f, 0.9f, 0.4f);
            error = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            error.normal.textColor = new Color(1f, 0.45f, 0.4f);
            hint = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        }

        // Once running, offer only a way out — the address is locked in by then. The rebinder
        // is GameMenu's to draw from here on; drawing it from both would double every click.
        if (Started)
        {
            // Hidden while paused: the menu has its own Leave button, and one fewer thing
            // drawing over the pause overlay is one fewer chance of a draw-order fight.
            if (!GameMenu.IsPaused && GUI.Button(new Rect(12, 12, 140, 30), "Leave match", button))
                LeaveMatch();
            return;
        }

        // Opaque backing panel. Without it anything else drawing this frame bleeds through
        // and the text becomes unreadable rather than merely untidy.
        // The trailing 44 is the error line's reserved space. Reserved unconditionally rather
        // than grown on demand, so the panel does not resize under the cursor at the exact
        // moment the player is clicking Host again. The extra 40 is the quit row.
        // The passive grid is 4 wide and grows a row at a time, so everything under it — the
        // description, the settings block, the quit row — is measured from where that grid
        // ENDS rather than from a constant. Two passives were added on 2026-07-27 and the
        // third row landed straight on top of the description line, which is the kind of
        // layout bug that only appears the day someone adds content.
        const float passiveTop = 226f, passiveRow = 34f;
        int passiveRows = Mathf.CeilToInt(PassiveChoice.Options.Length / 4f);
        float passiveBottom = passiveTop + passiveRows * passiveRow;
        float describeY = passiveBottom + 6f;
        float settingsY = describeY + 32f;
        float quitY = settingsY + SettingsUI.Height + 10f;

        const float panelW = 560f;
        float panelH = quitY + 30f + 16f + 44f;
        if (panel == null)
        {
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, Color.white);
            panel.Apply();
        }
        GUI.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        GUI.DrawTexture(new Rect(0, 0, panelW, panelH), panel);
        GUI.color = Color.white;

        GUI.Label(new Rect(12, 10, 300, 24), "Host address  (to join)", label);
        address = GUI.TextField(new Rect(12, 36, 220, 28), address, field);
        GUI.Label(new Rect(240, 10, 60, 24), "Port", label);
        string portText = GUI.TextField(new Rect(240, 36, 70, 28), port.ToString(), field);
        if (ushort.TryParse(portText, out ushort p)) port = p;

        // Says out loud that hosting ignores the two fields above. Behind a tunnel they are
        // different numbers, and a host who assumes the port box applies to them binds a port
        // nothing forwards to and gets no error to explain it.
        if (hostBindPort != port)
            GUI.Label(new Rect(12, 62, 320, 20), $"hosting binds :{hostBindPort}", hint);

        // Loadout is chosen HERE, before connecting, and locked for the match. Picking after
        // you spawn would make it a counter-pick rather than a commitment.
        GUI.Label(new Rect(12, 112, 300, 24), "Weapon (locked for the match)", label);
        for (int i = 0; i < LoadoutChoice.Names.Length; i++)
        {
            // Width derived, not fixed: the Knife made this six picks and a fixed 88 ran the
            // row off the panel. Divides whatever space the row has, so a seventh weapon fits
            // without another layout bug.
            float lw = (panelW - 24f - (LoadoutChoice.Names.Length - 1) * 4f) / LoadoutChoice.Names.Length;
            bool on = LoadoutChoice.WeaponIndex == i;
            if (GUI.Button(new Rect(12 + i * (lw + 4f), 138, lw, 30), LoadoutChoice.Names[i],
                    on ? selected : button))
                LoadoutChoice.WeaponIndex = i;
        }
        GUI.Label(new Rect(12, 170, panelW - 24f, 22),
            LoadoutChoice.Describe(LoadoutChoice.WeaponIndex), label);
        GUI.Label(new Rect(330, 10, panelW - 342f, 22), "Map (host decides)", label);

        GUI.Label(new Rect(12, 200, 400, 24), "Passive (locked for the match)", label);
        var opts = PassiveChoice.Options;
        for (int i = 0; i < opts.Length; i++)
        {
            bool on = PassiveChoice.Selected == opts[i];
            // 134 wide so "Featherweight" and "DoubleJump" fit — at 114 they were clipped.
            float bx = 12 + (i % 4) * 134, by = passiveTop + (i / 4) * passiveRow;
            if (GUI.Button(new Rect(bx, by, 130, 30), opts[i].ToString(), on ? selected : button))
                PassiveChoice.Selected = opts[i];
        }

        // Description of the current pick, so the choice can be made without reading code.
        GUI.Label(new Rect(12, describeY, panelW - 24f, 24),
            PassiveChoice.Describe(PassiveChoice.Selected), label);

        // Settings here too, not only in the pause menu — otherwise a first-time player spends
        // their entire first match on someone else's sensitivity before they can find it.
        // Saved immediately on change, since there is no menu-close event to hook here.
        if (SettingsUI.Draw(12, settingsY, panelW - 24f)) GameSettings.Save();

        // A way OUT of the game from the first screen. Before this, quitting a build meant
        // Alt+F4 or joining a match purely to reach the pause menu's Quit — the playtest ask
        // was literally "have an option to exit the game".
        if (GUI.Button(new Rect(12, quitY, 160, 30), "Quit to desktop", button))
            QuitGame();

        // Map. Host-only in effect: the server loads it as a global scene and clients receive
        // it when they join, so a client's selection here is ignored.
        //
        // The row divides the space it HAS between however many maps exist, rather than using
        // a fixed button width: at 104 wide a third map ran off the edge of the panel, and a
        // fourth would have been invisible entirely.
        float mapRowX = 330f, mapRowW = panelW - mapRowX - 12f;
        float mapBtnW = (mapRowW - (MapChoice.Names.Length - 1) * 6f) / MapChoice.Names.Length;
        for (int i = 0; i < MapChoice.Names.Length; i++)
        {
            if (GUI.Button(new Rect(mapRowX + i * (mapBtnW + 6f), 36, mapBtnW, 28), MapChoice.Names[i],
                    MapChoice.Index == i ? selected : button))
                MapChoice.Index = i;
        }

        // Game mode. Host-only in effect — MatchManager reads this on the server and syncs it,
        // so a client toggling it changes nothing about the match they join. Cycles because it
        // is one value with three options and the panel has no room for a row.
        if (GUI.Button(new Rect(330, 72, 214, 32),
                GameModeChoice.Describe(GameModeChoice.ModeIndex),
                GameModeChoice.ModeIndex != GameModeChoice.PureDeathmatch ? selected : button))
            GameModeChoice.ModeIndex = (GameModeChoice.ModeIndex + 1) % GameModeChoice.Count;

        // Bots, host-only in effect like the mode above. Cycles rather than using a row of
        // buttons because it is one number with four values and the panel has no room left.
        if (GUI.Button(new Rect(330, 108, 104, 30), BotChoice.Describe(BotChoice.Count),
                BotChoice.Count > 0 ? selected : button))
            BotChoice.Count = (BotChoice.Count + 1) % (BotChoice.Max + 1);

        // Difficulty sits next to the count because it is useless without it. Defaults to
        // Practice: bots exist to be shot at while you test a weapon, and at full strength three
        // of them kill a full-health player in about four seconds.
        if (GUI.Button(new Rect(438, 108, 106, 30),
                BotChoice.DescribeDifficulty(BotChoice.Difficulty),
                BotChoice.Difficulty > BotChoice.Practice + 0.01f ? selected : button))
            BotChoice.Difficulty = BotChoice.NextDifficulty(BotChoice.Difficulty);

        // Host = server + local client, the normal way one player hosts for others.
        if (GUI.Button(new Rect(12, 72, 100, 32), hosting ? "Hosting..." : "Host", button)
            && !hosting)
        {
            ApplyTransport(nm, asServer: true);
            lastError = null;
            hosting = true;
            // StartConnection is ASYNC: IsServerStarted is still false on the next line, and
            // LoadGlobalScenes silently refuses when the server is not up. So wait for the
            // started callback, then load the map, then connect our own client.
            nm.ServerManager.OnServerConnectionState += OnServerState;
            nm.ServerManager.StartConnection();
        }

        if (GUI.Button(new Rect(120, 72, 100, 32), joining ? "Joining..." : "Client", button)
            && !joining)
        {
            ApplyTransport(nm, asServer: false);
            lastError = null;
            joining = true;
            nm.ClientManager.OnClientConnectionState += OnClientState;
            nm.ClientManager.StartConnection();
        }

        // Dedicated server: no local player, for a box with a public IP.
        if (GUI.Button(new Rect(228, 72, 100, 32), "Server", button))
        {
            ApplyTransport(nm, asServer: true);
            nm.ServerManager.StartConnection();
        }

        if (!string.IsNullOrEmpty(lastError))
            GUI.Label(new Rect(12, panelH - 46f, panelW - 24f, 40f), lastError, error);

        // Last, so it covers the connect panel rather than fighting it for the same pixels.
        KeybindsUI.Draw();
    }

    // Fires once the server socket is genuinely up. Only now can the map be registered.
    //
    // Also handles the FAILURE path, which used to be ignored entirely: a server that cannot
    // bind reports Stopped, the old code returned early and left the subscription attached, and
    // the screen said nothing at all. The common cause is the port already being in use — by
    // another copy of the game, or by an editor session whose socket outlived play mode.
    void OnServerState(FishNet.Transporting.ServerConnectionStateArgs args)
    {
        var state = args.ConnectionState;
        if (state == FishNet.Transporting.LocalConnectionState.Starting) return;

        var nm = InstanceFinder.NetworkManager;
        if (nm == null) { hosting = false; return; }

        if (state != FishNet.Transporting.LocalConnectionState.Started)
        {
            // Stopped/Stopping without ever starting = the bind failed.
            nm.ServerManager.OnServerConnectionState -= OnServerState;
            hosting = false;
            lastError = $"Could not host on port {port}. Something is already using it — " +
                        "another copy of the game, or a previous session. Try a different port.";
            return;
        }

        nm.ServerManager.OnServerConnectionState -= OnServerState;
        hosting = false;

        LoadChosenMap(nm);
        nm.ClientManager.StartConnection();
    }

    // A failed join is otherwise completely silent — the button clicks, nothing happens, and the
    // reason (wrong address, host not up, port blocked) never reaches the person who can fix it.
    void OnClientState(FishNet.Transporting.ClientConnectionStateArgs args)
    {
        var state = args.ConnectionState;
        if (state == FishNet.Transporting.LocalConnectionState.Starting) return;

        var nm = InstanceFinder.NetworkManager;
        if (nm == null) { joining = false; return; }

        if (state == FishNet.Transporting.LocalConnectionState.Started)
        {
            nm.ClientManager.OnClientConnectionState -= OnClientState;
            joining = false;
            return;
        }

        nm.ClientManager.OnClientConnectionState -= OnClientState;
        joining = false;
        lastError = $"Could not reach a host at {address}:{port}. Check the address and port, " +
                    "and that the host has actually clicked Host.";
    }

    // Load the host's map as a global scene so joining clients receive it automatically.
    // Skipped when we are already in it, since replacing a scene with itself would needlessly
    // destroy and rebuild everything in it.
    static void LoadChosenMap(FishNet.Managing.NetworkManager nm)
    {
        string want = MapChoice.Selected;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == want) return;

        var data = new FishNet.Managing.Scened.SceneLoadData(want);
        data.ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All;
        nm.SceneManager.LoadGlobalScenes(data);
    }

    // Push address/port into the transport before any connection starts — afterwards it's too
    // late, the socket is already bound.
    //
    // asServer matters because SetPort means two different things. Serving, it is the port the
    // socket BINDS locally. Joining, it is the port dialled on the far end. Behind a tunnel or
    // a port-forward those are different numbers — the tunnel listens publicly on one and
    // forwards to the other — so using the typed value for both means a player who once joined
    // a tunnelled game then hosts on the tunnel's PUBLIC port, binds a port nothing forwards to,
    // and fails with no error anywhere. Persisting the typed port made that likelier, not less.
    void ApplyTransport(FishNet.Managing.NetworkManager nm, bool asServer)
    {
        // Only remember what was actually dialled. Hosting does not use the address at all, so
        // hosting must not overwrite the group's server address with a stale local one.
        if (!asServer)
        {
            GameSettings.Address = address;
            GameSettings.Port = port;
            GameSettings.Save();
        }

        Transport t = nm.TransportManager.Transport;
        if (t == null) return;

        // Host is server + local client, so its client half dials its own bound port on loopback.
        t.SetClientAddress(asServer ? "localhost" : address);
        t.SetPort(asServer ? hostBindPort : port);
    }

    // Leave the match and return to the connect screen without restarting the app.
    //
    // Deliberately does NOT reload the scene. FishNet already despawns networked objects when
    // the connection stops, so a reload adds nothing — and reloading a scene that contains a
    // NetworkManager while the live one is DontDestroyOnLoad risks ending up with two. You
    // stay in whatever map you last played; hosting again loads whichever map is selected.
    public static void LeaveMatch()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm != null) StopAll(nm);

        // Clear the in-flight flags, or a Host that was left mid-attempt leaves the button
        // reading "Hosting..." forever with nothing behind it.
        foreach (var ui in FindObjectsByType<ConnectUI>(FindObjectsSortMode.None))
        {
            if (ui == null) continue;
            ui.hosting = false;
            ui.joining = false;
        }

        // Pause state is static, so it survives everything and has to be cleared explicitly.
        // Leaving it set was why the pause panel stayed on screen over the connect screen.
        GameMenu.ForceUnpause();
    }

    static void StopAll(FishNet.Managing.NetworkManager nm)
    {
        if (InstanceFinder.IsClientStarted) nm.ClientManager.StopConnection();
        if (InstanceFinder.IsServerStarted) nm.ServerManager.StopConnection(true);
    }

    // Same shape as GameMenu.Quit — duplicated because both are four lines and a shared
    // "QuitHelper" for two call sites would be more file than function.
    static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
