using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// The ONE passive this player brings, chosen on the connect screen and locked for the match.
//
// SYNCED, unlike the weapon choice. Passives change the SIMULATION — Featherweight alters
// capsule radius, Dash gates a movement ability, Vitality alters server-owned health — so a
// local-only choice would leave the client and server disagreeing about your hitbox and your
// movement. The owner sends its pick once; the server owns the value from then on.
//
// A single value rather than a list on purpose: with a list every passive has to be balanced
// against simply having it for free. Pick-one makes them compete instead.
public class PassiveLoadout : NetworkBehaviour
{
    [Tooltip("Set in the Inspector for offline play. Networked play overwrites it with the " +
             "owner's connect-screen pick.")]
    public PassiveType passive = PassiveType.None;

    readonly SyncVar<PassiveType> synced = new SyncVar<PassiveType>(PassiveType.None);

    // Fired when the passive changes. PlayerMotor caches radius and the dash flag, so it
    // subscribes and re-resolves; components that read Has() every frame don't need it.
    public event System.Action Changed;

    // Offline keeps the Inspector value; networked reads the synced one, so both work.
    PassiveType Active => IsSpawned ? synced.Value : passive;

    // Guards None so Has(None) is false rather than matching an empty loadout.
    public bool Has(PassiveType type) => type != PassiveType.None && Active == type;

    void Awake() => synced.OnChange += OnSyncedChanged;

    void OnDestroy() => synced.OnChange -= OnSyncedChanged;

    void OnSyncedChanged(PassiveType prev, PassiveType next, bool asServer)
    {
        passive = next;      // keep the inspector field readable/debuggable
        Changed?.Invoke();   // makes PlayerMotor re-resolve radius + dash on every client
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Tell the server what we picked. Owner-only — you cannot set anyone else's passive.
        if (IsOwner) SubmitPassive(PassiveChoice.Selected);
    }

    [ServerRpc]
    void SubmitPassive(PassiveType type)
    {
        synced.Value = type;
    }

    // Offline / editor path: change directly and notify. Does nothing meaningful once
    // networked, since the server owns the value.
    public void Equip(PassiveType type)
    {
        if (IsSpawned || type == passive) return;
        passive = type;
        Changed?.Invoke();
    }
}
