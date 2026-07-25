using FishNet.Managing;
using UnityEngine;

// Silent, rate-limited access to the NetworkManager for code that runs every frame.
//
// FishNet's InstanceFinder logs a FULL STACK TRACE every time it is asked for a
// NetworkManager it cannot find. Several HUD components ask per frame (some per OnGUI pass),
// which in a scene without one wrote the same message hundreds of times a second — a 25 GB
// Editor.log was the receipt.
//
// An earlier version of this class only gated those callers with a "does one exist?" bool and
// let them call InstanceFinder anyway. That still spammed, because the two disagree: this
// probe finds the NetworkManager object, while InstanceFinder additionally requires the
// registration its Awake performs, so in the window between "object exists" and "FishNet
// initialised it" every frame produced a log line. Holding the reference HERE removes
// InstanceFinder from the per-frame path entirely, so the disagreement cannot cost anything.
//
// Re-probed in both directions rather than latched: leaving a match destroys the manager, and
// a latched hit would hand out a dead reference forever.
public static class NetPresence
{
    const float ProbeInterval = 2f;

    static NetworkManager cached;
    static float nextProbeAt;

    public static NetworkManager Manager
    {
        get
        {
            // The == null check is Unity's, so a destroyed manager re-probes rather than
            // returning a fake-alive reference.
            if (cached == null && Time.unscaledTime >= nextProbeAt)
            {
                cached = Object.FindAnyObjectByType<NetworkManager>();
                nextProbeAt = Time.unscaledTime + ProbeInterval;
            }
            return cached;
        }
    }

    public static bool HasNetworkManager => Manager != null;

    // Mirrors of the InstanceFinder properties the HUD used to poll, minus the logging.
    public static bool IsClientStarted
    {
        get { var m = Manager; return m != null && m.IsClientStarted; }
    }

    public static bool IsServerStarted
    {
        get { var m = Manager; return m != null && m.IsServerStarted; }
    }

    public static bool IsRunning => IsClientStarted || IsServerStarted;
}
