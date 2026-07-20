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
    [Tooltip("Address a CLIENT dials. localhost = same machine. A LAN/Tailscale/public IP " +
             "connects to another machine.")]
    public string address = "localhost";
    public ushort port = 7770;

    GUIStyle label, field, button, selected;
    bool Started => InstanceFinder.NetworkManager != null &&
                    (InstanceFinder.IsServerStarted || InstanceFinder.IsClientStarted);

    void OnGUI()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            label.normal.textColor = Color.white;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            selected = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            selected.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }

        // Once running, offer only a way out — the address is locked in by then.
        if (Started)
        {
            if (GUI.Button(new Rect(12, 12, 120, 30), "Disconnect", button)) StopAll(nm);
            return;
        }

        GUI.Label(new Rect(12, 10, 300, 24), "Host address", label);
        address = GUI.TextField(new Rect(12, 36, 220, 28), address, field);
        GUI.Label(new Rect(240, 10, 60, 24), "Port", label);
        string portText = GUI.TextField(new Rect(240, 36, 70, 28), port.ToString(), field);
        if (ushort.TryParse(portText, out ushort p)) port = p;

        // Loadout is chosen HERE, before connecting, and locked for the match. Picking after
        // you spawn would make it a counter-pick rather than a commitment.
        GUI.Label(new Rect(12, 112, 300, 24), "Weapon (locked for the match)", label);
        for (int i = 0; i < LoadoutChoice.Names.Length; i++)
        {
            bool on = LoadoutChoice.WeaponIndex == i;
            if (GUI.Button(new Rect(12 + i * 92, 138, 88, 30), LoadoutChoice.Names[i],
                    on ? selected : button))
                LoadoutChoice.WeaponIndex = i;
        }

        GUI.Label(new Rect(12, 180, 400, 24), "Passive (locked for the match)", label);
        var opts = PassiveChoice.Options;
        for (int i = 0; i < opts.Length; i++)
        {
            bool on = PassiveChoice.Selected == opts[i];
            float bx = 12 + (i % 4) * 118, by = 206 + (i / 4) * 34;
            if (GUI.Button(new Rect(bx, by, 114, 30), opts[i].ToString(), on ? selected : button))
                PassiveChoice.Selected = opts[i];
        }

        // Description of the current pick, so the choice can be made without reading code.
        GUI.Label(new Rect(12, 280, Screen.width - 40f, 24),
            PassiveChoice.Describe(PassiveChoice.Selected), label);

        // Host = server + local client, the normal way one player hosts for others.
        if (GUI.Button(new Rect(12, 72, 100, 32), "Host", button))
        {
            ApplyTransport(nm);
            nm.ServerManager.StartConnection();
            nm.ClientManager.StartConnection();
        }

        if (GUI.Button(new Rect(120, 72, 100, 32), "Client", button))
        {
            ApplyTransport(nm);
            nm.ClientManager.StartConnection();
        }

        // Dedicated server: no local player, for a box with a public IP.
        if (GUI.Button(new Rect(228, 72, 100, 32), "Server", button))
        {
            ApplyTransport(nm);
            nm.ServerManager.StartConnection();
        }
    }

    // Push the typed address/port into the transport before any connection starts —
    // afterwards it's too late, the socket is already bound.
    void ApplyTransport(FishNet.Managing.NetworkManager nm)
    {
        Transport t = nm.TransportManager.Transport;
        if (t == null) return;
        t.SetClientAddress(address);
        t.SetPort(port);
    }

    static void StopAll(FishNet.Managing.NetworkManager nm)
    {
        if (InstanceFinder.IsClientStarted) nm.ClientManager.StopConnection();
        if (InstanceFinder.IsServerStarted) nm.ServerManager.StopConnection(true);
    }
}
