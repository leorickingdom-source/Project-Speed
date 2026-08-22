using UnityEngine;
using UnityEngine.InputSystem;

// Wires the recorder and the player to the match, and is the only thing either of them knows
// about the game being over.
//
// Split out rather than folded into MatchManager because a replay is not part of a match: it
// has no authority, no score, and must keep working offline where there is no MatchManager at
// all. Keeping the dependency pointing this way means the recorder can be deleted from the
// project without touching a line of match code.
public class PlayOfTheGame : MonoBehaviour
{
    [Tooltip("Seconds to wait after the match ends before the replay starts, so the final kill " +
             "and the scoreboard land before the screen is taken over.")]
    public float delayAfterMatch = 2.5f;

    [Tooltip("Offline testing: replays the best clip so far on demand. F10 by default, clear of " +
             "PassivePicker's F1-F7 and ThirdPersonView's F9.")]
    public Key previewKey = Key.F10;

    MatchManager match;
    bool fired;
    float playAt = -1f;

    // Both halves are created on demand — no scene has to remember to place them, the pattern
    // ImpactFx and the head caps already use.
    void Awake()
    {
        MatchRecorder.Ensure();
        ReplayPlayer.Ensure();
    }

    void Update()
    {
        if (match == null) match = FindAnyObjectByType<MatchManager>();

        // Armed once. MatchOver stays true for the rest of the match, so without the latch this
        // would try to restart the replay on every frame of the post-match screen.
        if (!fired && match != null && match.MatchOver)
        {
            fired = true;
            playAt = Time.time + delayAfterMatch;
        }
        if (playAt > 0f && Time.time >= playAt)
        {
            playAt = -1f;
            var rec = MatchRecorder.Instance;
            // No clip means nobody killed anybody. Showing an empty replay would be worse than
            // showing none, so the match simply ends the way it did before this existed.
            if (rec != null && rec.Best != null) ReplayPlayer.Ensure().Play(rec.Best);
        }

        var kb = Keyboard.current;
        if (kb != null && kb[previewKey].wasPressedThisFrame)
        {
            var player = ReplayPlayer.Ensure();
            if (player.Playing) player.Stop();
            else
            {
                var rec = MatchRecorder.Instance;
                if (rec != null && rec.Best != null) player.Play(rec.Best);
                else Debug.Log("[PlayOfTheGame] No highlight recorded yet — get a kill first.");
            }
        }
    }
}
