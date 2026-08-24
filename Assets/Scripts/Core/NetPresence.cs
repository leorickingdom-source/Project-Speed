using FishNet.Managing;
using FishNet.Object;
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

    // The safe form of NetworkBehaviour.IsSpawned, and the reason this class grew a
    // per-behaviour question at all.
    //
    // IsSpawned dereferences a NetworkObject cache FishNet fills in only when it initialises
    // THAT behaviour. Ask before it has — a scene object during scene load, a prefab between
    // Instantiate and Spawn — and it throws inside FishNet instead of answering false. So does
    // IsServerStarted, which reads a cache the same initialisation sets; use the parameterless
    // IsServerStarted above for that, since it goes through the manager rather than the
    // behaviour.
    //
    // Three call sites were fixed for this one at a time before it was worth naming:
    // PlayerNetwork.Start, MatchManager.OnSceneLoaded and KillFeed.Announce. NetworkObject
    // returns the same field without dereferencing it, which is the whole fix.
    public static bool IsSpawned(NetworkBehaviour nb) =>
        nb != null && nb.NetworkObject != null && nb.NetworkObject.IsSpawned;
}
