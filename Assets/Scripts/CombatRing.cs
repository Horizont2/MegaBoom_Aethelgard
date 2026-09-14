using System.Collections.Generic;
using UnityEngine;

// Decides which enemies are allowed to be attacking the player right now.
//
// ==== WHY A CROWD NEEDS A CONDUCTOR ====
//
// Eight enemies each deciding independently to swing produces a wall of damage
// with no gaps in it. That is not difficulty, it is arithmetic: there is no
// answer, no read, and nothing to do but back away — which is exactly the
// "draining" feeling this fight has had. It also makes a shield pointless
// before it is written, because no stamina pool survives eight simultaneous
// attackers, and it means the player can never lower their guard to counter.
//
// So attacking is a PRIVILEGE, granted to two of them at a time. The rest stay
// in the fight — they are not switched off — they circle, close, feint and wait
// for a slot. The crowd stays threatening and becomes readable.
//
// ==== THE HARD PART IS MAKING IT NOT LOOK LIKE A QUEUE ====
//
// The obvious implementation — waiters stop and stand at a fixed radius — looks
// worse than the problem it solves. A ring of statues politely waiting their
// turn destroys the fiction instantly. Everything in the Post section below
// exists to prevent that:
//
//   THEY HOLD STATION AND RESETTLE. A waiter stands at its post facing the
//   player, then after a few seconds steps to a new one. It does NOT orbit —
//   continuous lateral movement needs a strafe animation and there is not one,
//   so it read as sliding sideways however slowly it was done. Movement in
//   bursts is movement the animation set can actually show.
//
//   THE RING IS NOT A CIRCLE. Each waiter has its own preferred distance and
//   its own angular offset, so the shape is ragged. A perfect circle reads as
//   spawned geometry.
//
//   WAITERS THREATEN. They feint — a step in, weapon raised, no swing — often
//   enough to look eager and rarely enough not to be noise.
//
//   SLOTS ROTATE. A token is taken away after a while and given to someone
//   else, so it is not the same two enemies all fight. Being circled by a crowd
//   that keeps swapping who lunges is far more menacing than two attackers and
//   six spectators.
//
//   AND THEY ARRIVE FROM THE RING. An enemy granted a slot lunges in from where
//   it was standing. Nothing teleports into range.
[DisallowMultipleComponent]
public class CombatRing : MonoBehaviour
{
    public static CombatRing Instance { get; private set; }

    [Header("How many may press at once")]
    [Tooltip("Attackers allowed simultaneously in a small fight. Two is the number a player can read, answer and punish; three already feels like being mobbed.")]
    public int baseSlots = 2;
    [Tooltip("Attackers allowed once the crowd is large. One more, so a big fight IS heavier without becoming unanswerable.")]
    public int crowdSlots = 3;
    // CALIBRATED AGAINST WHAT "ENGAGED" NOW MEANS. This was 6, set when enemies
    // only reported in once they were already at melee range — so it took six
    // bodies literally touching the player to trip. Enemies now report in from
    // the whole engagement band, roughly twelve metres, where six is an ordinary
    // group rather than a mob. Left at 6 the third slot would open in almost
    // every fight, which is the opposite of what the ring is for.
    [Tooltip("Enemies engaged (anywhere in the ~12m engagement band, not just at melee range) before the extra slot opens.")]
    public int crowdThreshold = 9;

    [Header("Rotation")]
    [Tooltip("Seconds a holder may keep its slot before it is offered to somebody else. Stops the same two doing all the fighting while the rest look like spectators.")]
    public float maxHoldTime = 4.5f;
    [Tooltip("Seconds an enemy must wait after losing a slot before it can take another. Gives the others a genuine turn.")]
    public float cooldownAfterHold = 1.6f;

    [Header("The ring")]
    [Tooltip("Closest a waiting enemy stands. Must be beyond the player's melee reach or the ring reads as enemies clipping into you and refusing to swing.")]
    public float innerRadius = 3.6f;
    [Tooltip("Furthest a waiting enemy stands.")]
    public float outerRadius = 6.2f;

    [Header("Facing")]
    [Tooltip("How many attackers at once may swing from outside the player's guard arc. One keeps being flanked a real threat without making the shield pointless.")]
    public int maxRearAttackers = 1;
    [Tooltip("Half-angle, in degrees, counted as 'in front of the player'. Slightly wider than the guard arc, so an enemy at the very edge of cover is not treated as a rear attacker.")]
    public float frontHalfAngle = 80f;

    private Transform _player;

    private Transform Player
    {
        get
        {
            if (_player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _player = p.transform;
            }
            return _player;
        }
    }

    private bool IsBehindPlayer(EnemyAI who)
    {
        var p = Player;
        if (p == null || who == null) return false;
        Vector3 to = who.transform.position - p.position; to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return false;
        return Vector3.Angle(p.forward, to.normalized) > frontHalfAngle;
    }

    private int CountRearHolders()
    {
        int n = 0;
        for (int i = 0; i < _holders.Count; i++)
            if (_holders[i] != null && IsBehindPlayer(_holders[i])) n++;
        return n;
    }

    private readonly List<EnemyAI> _holders = new List<EnemyAI>(4);
    private readonly Dictionary<EnemyAI, float> _grantedAt = new Dictionary<EnemyAI, float>(8);
    private readonly Dictionary<EnemyAI, float> _blockedUntil = new Dictionary<EnemyAI, float>(8);
    private readonly List<EnemyAI> _engaged = new List<EnemyAI>(16);
    private float _prune;

    // ==== THE OPENING A BLOCK BUYS ====
    //
    // Staggering the enemy that was blocked is not enough on its own: the other
    // two in the rotation simply swing while it reels, so the player is punished
    // for defending well and the counter-attack the whole system promises never
    // arrives. A successful block makes the WHOLE ring hesitate for a moment.
    //
    // It reads as the crowd flinching when the shield rings, which is both fair
    // and legible — and it is the only place in the fight where the player is
    // given a turn rather than having to steal one.
    private float _hesitateUntil = -1f;

    public void Hesitate(float seconds)
    {
        _hesitateUntil = Mathf.Max(_hesitateUntil, Time.time + seconds);
    }

    public bool Hesitating => Time.time < _hesitateUntil;

    // ==== NO TWO SWINGS AT ONCE ====
    //
    // Slots alone did not deliver what they promised. A holder released its
    // token the instant its swing ended, so with three enemies and two slots
    // the rotation was A and B swing, A finishes, C starts immediately — all
    // three landing inside about two seconds, which from the player's seat is
    // indistinguishable from all three attacking at once.
    //
    // The token says WHO may attack. This says WHEN, and it is the rule that
    // actually paces the fight: whoever holds a slot, no two attacks may BEGIN
    // within this gap of each other. It turns a burst into a rhythm the player
    // can hear coming.
    [Tooltip("Minimum seconds between any two attacks starting anywhere in the crowd. The single most effective pacing control here — raise it if fights still feel like a wall.")]
    public float globalAttackGap = 0.85f;

    private float _lastSwingAt = -99f;

    public bool SwingWindowOpen => Time.time - _lastSwingAt >= globalAttackGap;

    // Called by an enemy the instant it commits, so the next one has to wait.
    public void NoteSwingStarted() => _lastSwingAt = Time.time;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Instance != null) return;
        var go = new GameObject("[CombatRing]");
        DontDestroyOnLoad(go);
        go.AddComponent<CombatRing>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Enemies announce that they are in the fight, so the slot count can scale
    // with the size of it without this having to search the scene.
    public void ReportEngaged(EnemyAI who)
    {
        if (who == null || _engaged.Contains(who)) return;
        _engaged.Add(who);
    }

    public void ReportDisengaged(EnemyAI who)
    {
        _engaged.Remove(who);
        Release(who);
    }

    private int Slots => _engaged.Count >= crowdThreshold ? crowdSlots : baseSlots;

    // "May I swing?" Bosses never ask — a boss fight is not a crowd, and a boss
    // waiting politely for a token behind two skeletons would be absurd.
    public bool RequestAttack(EnemyAI who)
    {
        if (who == null || who.IsDead) return false;

        // ==== THE FIGHT HAPPENS IN FRONT OF THE PLAYER ====
        //
        // A guard is a direction, so an attack from behind cannot be blocked —
        // it can only be turned into. That is fine as an occasional pressure and
        // ruinous as the default: with attackers picked without regard to where
        // the player is looking, roughly half of every fight arrived from
        // outside the guard arc, the player was asked to spin a full circle for
        // each one, and the shield stopped being worth holding at all.
        //
        // The conductor already decides WHO may swing. Deciding it with the
        // player's facing in mind costs nothing and is what makes a shield a
        // real answer rather than a lottery. Being flanked still happens — one
        // slot may always come from behind — so turning around remains part of
        // the fight instead of becoming most of it.
        if (!who.isBoss && IsBehindPlayer(who) && !_holders.Contains(who)
            && CountRearHolders() >= maxRearAttackers) return false;
        // Even a boss waits out the flinch. A boss that swings through a parry
        // teaches the player that parrying a boss is pointless, which is the one
        // fight where it matters most.
        if (Hesitating) return false;
        // A boss skips the token queue but NOT the rhythm — otherwise a boss
        // with adds produces exactly the overlapping wall this exists to stop.
        if (!SwingWindowOpen) return false;
        if (who.isBoss) return true;

        if (_holders.Contains(who)) return true;
        if (_blockedUntil.TryGetValue(who, out float until) && Time.time < until) return false;

        Prune();
        if (_holders.Count >= Slots) return false;

        _holders.Add(who);
        _grantedAt[who] = Time.time;
        return true;
    }

    public bool IsAttacking(EnemyAI who) => who != null && _holders.Contains(who);

    // Hands the slot back. `penalise` is for an enemy that has just been blocked
    // or parried: it loses its turn AND waits longer than usual, which is what
    // makes a successful block create the opening the player counters into.
    public void Release(EnemyAI who, bool penalise = false, float extraWait = 0f)
    {
        if (who == null) return;
        _holders.Remove(who);
        _grantedAt.Remove(who);
        if (penalise || extraWait > 0f)
            _blockedUntil[who] = Time.time + cooldownAfterHold + extraWait;
    }

    private void Update()
    {
        _prune -= Time.deltaTime;
        if (_prune > 0f) return;
        _prune = 0.25f;
        Prune();
    }

    private void Prune()
    {
        for (int i = _holders.Count - 1; i >= 0; i--)
        {
            var h = _holders[i];
            if (h == null || h.IsDead)
            {
                if (h != null) { _grantedAt.Remove(h); _blockedUntil.Remove(h); }
                _holders.RemoveAt(i);
                continue;
            }

            // TIME-SHARE THE SLOTS. Without this the two nearest enemies hold
            // their tokens for the whole fight and everyone else is scenery,
            // which is the version of this system that looks fake.
            if (_grantedAt.TryGetValue(h, out float since) && Time.time - since > maxHoldTime)
            {
                _holders.RemoveAt(i);
                _grantedAt.Remove(h);
                _blockedUntil[h] = Time.time + cooldownAfterHold;
            }
        }

        for (int i = _engaged.Count - 1; i >= 0; i--)
            if (_engaged[i] == null || _engaged[i].IsDead) _engaged.RemoveAt(i);
    }

    // ---- the ring ------------------------------------------------------------

    // Where an enemy without a slot should be standing right now.
    //
    // Deterministic per enemy and continuous in time, so it can be called every
    // frame from the AI's own movement without any state being stored here — and
    // so two enemies never resolve to the same spot.
    public Vector3 PostFor(EnemyAI who, Vector3 playerPos, float time)
    {
        int seed = who != null ? who.GetInstanceID() : 0;
        // Fixed per enemy: its own place in the ring, its own distance, its own
        // direction and pace of circling. A crowd where everyone orbits at the
        // same speed in the same direction looks like a carousel.
        float angle0 = Frac(seed * 0.6180339887f) * Mathf.PI * 2f;
        float radius = Mathf.Lerp(innerRadius, outerRadius, Frac(seed * 0.7548776662f));

        // ==== THEY HOLD STATION AND RESETTLE. THEY DO NOT ORBIT. ====
        //
        // This rotated the post continuously, so a waiting enemy was always
        // walking sideways — at 0.35-0.75 rad/s it was a full sprint crabwise,
        // and even after dropping it to a fifth of that it was still a constant
        // slow slide. Three separate reports of enemies "running left and right"
        // were all this, and no speed makes continuous lateral movement look
        // right without a strafe animation, which this project does not have.
        //
        // So the post STEPS. It stays put for a few seconds, then jumps to a new
        // angle, and the enemy walks there and stops. Standing still facing you,
        // then repositioning, then standing still again is what a pack actually
        // does — and every metre of movement is now forward movement, which is
        // the only kind the animation set can show.
        //
        // Each enemy gets its own dwell time and its own step size, so the group
        // never resettles in unison.
        float dwell = Mathf.Lerp(2.6f, 5.2f, Frac(seed * 0.3247179572f));
        float phase = Frac(seed * 0.8912f) * dwell;
        int step = Mathf.FloorToInt((time + phase) / dwell);

        // A modest arc per step — a third of the way round would read as
        // teleporting from one side to the other.
        float stepArc = Mathf.Lerp(0.35f, 0.9f, Frac(seed * 0.5771f));
        float dir = (seed & 1) == 0 ? 1f : -1f;
        // Deterministic wobble per step so the arc is not perfectly regular.
        float jitter = (Frac((seed * 0.318f) + step * 0.6180339887f) - 0.5f) * 0.5f;

        float a = angle0 + (step * stepArc * dir) + jitter;

        // Distance varies per step too, instead of breathing continuously —
        // same reason: movement should happen in bursts, not always.
        float ring = radius + (Frac((seed * 0.7717f) + step * 0.3247179572f) - 0.5f) * 1.4f;

        return playerPos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
    }

    // Should this waiter throw a feint right now? Rare, staggered per enemy, and
    // never while somebody is mid-swing at the same moment — a crowd that all
    // lurches at once looks choreographed rather than eager.
    public bool ShouldFeint(EnemyAI who, float time)
    {
        if (who == null || _holders.Contains(who)) return false;
        int seed = who.GetInstanceID();
        float period = Mathf.Lerp(3.2f, 6.5f, Frac(seed * 0.9142f));
        float phase = Frac(seed * 0.4531f) * period;
        return Mathf.Repeat(time + phase, period) < Time.deltaTime * 2f;
    }

    private static float Frac(float v) => v - Mathf.Floor(v);
}
