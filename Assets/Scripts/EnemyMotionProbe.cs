using UnityEngine;

// Measures what an enemy is ACTUALLY doing, and prints it once.
//
// ==== WHY THIS EXISTS ====
//
// The zigzag has now survived eleven fixes. Every one of them was aimed at a
// cause that was real — crowd repulsion in the heading, the orbiting ring post,
// slot-rotation round trips, three coroutines fighting the movement update,
// knockback as a fourth writer, an avoidance limit cycle, stale state coming
// back out of the object pool. Each was a genuine defect and each was fixed, and
// the enemies still weave. So the remaining cause is something not yet guessed
// at, and guessing a twelfth time is not a plan.
//
// This measures instead. Attach it to any enemy — or leave AutoAttach on and it
// finds the nearest aggroed one by itself — and after a few seconds of running
// it prints the numbers that separate the candidate causes:
//
//   HEADING REVERSALS say whether the path itself is weaving. A straight run at
//   the player is near zero; a zigzag is several per second.
//
//   BRANCH CHANGES say whether the AI is switching its mind between chase,
//   footwork and holding station. If reversals are high and branch changes are
//   low, the weave is inside one branch; if both are high, it is the branch
//   selection.
//
//   DEFLECTION CHANGES isolate obstacle avoidance specifically.
//
//   ANIM STATE CHANGES and SPEED SWINGS separate "the clip keeps changing" from
//   "the clip is playing at a lurching rate", which look identical on screen and
//   have completely different causes.
//
//   And POSITION WRITERS PER FRAME is the one that matters most: more than one
//   system moving the body in a single frame is the shape of every version of
//   this bug found so far.
[DisallowMultipleComponent]
public class EnemyMotionProbe : MonoBehaviour
{
    [Tooltip("Leave on and a probe installs itself on the nearest aggroed enemy a few seconds into the scene. Off, it only measures the enemy it is attached to.")]
    public bool autoAttach = true;

    [Tooltip("Seconds of ACTUAL MOVEMENT to collect before printing. Frames where the subject is standing still do not count — the first run spent 1403 frames measuring a skeleton standing over a dead player and learned nothing.")]
    public float sampleSeconds = 4f;

    [Tooltip("Print again every sampleSeconds instead of once.")]
    public bool repeat = false;

    private EnemyAI _ai;
    private Animator _anim;
    private Transform _player;

    private float _endAt;
    private float _movingTime;
    private bool _done;

    private bool PlayerDead()
    {
        var pc = PlayerController.LocalInstance;
        return pc != null && pc.IsDead;
    }

    private Vector3 _lastPos;
    private Vector3 _lastHeading;
    private int _frames, _movedFrames;
    private int _reversals;          // heading turned more than the threshold, the other way than last time
    private int _bigTurns;           // heading turned more than 25 degrees in one frame
    private float _turnSum, _turnMax;
    private int _lastTurnSign;

    private int _animStateChanges;
    private int _lastStateHash;
    private float _speedMin = float.MaxValue, _speedMax;
    private float _lastAnimSpeed = -1f;
    private int _animSpeedJumps;

    private float _stepMin = float.MaxValue, _stepMax;

    private int _writeFrames, _writesTotal, _maxWritesInAFrame;
    private float _worstStep;
    private string _worstWriter = "-";
    private float _worstActual;
    private string _worstActualHit = "-";
    private readonly System.Collections.Generic.List<string> _wNames = new System.Collections.Generic.List<string>(8);
    private readonly System.Collections.Generic.List<int> _wCounts = new System.Collections.Generic.List<int>(8);

    private void CountWriter(string w)
    {
        for (int i = 0; i < _wNames.Count; i++)
            if (_wNames[i] == w) { _wCounts[i]++; return; }
        if (_wNames.Count >= 12) return;
        _wNames.Add(w); _wCounts.Add(1);
    }

    private bool _hasMovingBool;
    private bool _lastMoving;
    private int _movingFlips;

    // Which pairs it flips between, and how often. Small and fixed: a handful
    // of states, so a list beats a dictionary and allocates nothing per frame.
    private readonly System.Collections.Generic.List<string> _flipNames = new System.Collections.Generic.List<string>(8);
    private readonly System.Collections.Generic.List<int> _flipCounts = new System.Collections.Generic.List<int>(8);

    private void RecordFlip(int fromHash, int toHash)
    {
        string key = StateName(fromHash) + " -> " + StateName(toHash);
        for (int i = 0; i < _flipNames.Count; i++)
            if (_flipNames[i] == key) { _flipCounts[i]++; return; }
        if (_flipNames.Count >= 12) return;
        _flipNames.Add(key);
        _flipCounts.Add(1);
    }

    // The controller's own state names, resolved by hash. Cheap because the set
    // is tiny and the answer is looked up only when a flip happens.
    private static readonly string[] s_known =
        { "Idle_A", "Running_A", "Attack", "Hit_A", "Death_A", "Dizzy", "Bow_Attack" };

    private static string StateName(int hash)
    {
        if (hash == 0) return "<start>";
        foreach (var n in s_known)
            if (Animator.StringToHash(n) == hash) return n;
        return "<" + hash + ">";
    }

    // ==== A DIAGNOSTIC MUST NOT SHIP ====
    //
    // This installed itself in EVERY build, including release, and printed a
    // twenty-five line report at the player. It also switched on
    // EnemyAI.OnControllerColliderHit's string building for the whole horde —
    // see the note there — so a tool that watches ONE enemy for a few seconds
    // was costing every enemy in the game four allocations per contact for the
    // entire run.
    //
    // Editor and development builds only, and it tells EnemyAI to start
    // recording contacts only while it actually has a subject.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[EnemyMotionProbe]");
        DontDestroyOnLoad(go);
        go.AddComponent<EnemyMotionProbe>();
    }
#endif

    private void Start()
    {
        _ai = GetComponent<EnemyAI>();
        _endAt = Time.time + 6f;   // let the scene settle before looking for a subject
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _player = p.transform;
    }

    private void Update()
    {
        if (_done && !repeat) return;

        // Auto mode: this component is on its own object until it finds a
        // subject, then it measures that one from here.
        if (_ai == null)
        {
            if (!autoAttach || Time.time < _endAt) return;
            _ai = NearestAggroed();
            if (_ai == null) { _endAt = Time.time + 1f; return; }

            // Only NOW does the horde start paying for contact strings, and only
            // in an editor or development build. See EnemyAI.DbgTrackContacts.
            EnemyAI.DbgTrackContacts = true;

            _anim = _ai.GetComponentInChildren<Animator>();
            if (_anim != null)
                foreach (var prm in _anim.parameters)
                    if (prm.type == AnimatorControllerParameterType.Bool && prm.name == "isMoving") _hasMovingBool = true;
            _lastPos = _ai.transform.position;
            _lastHeading = _ai.transform.forward;
            _movingTime = 0f;
            Debug.Log($"[MotionProbe] Watching '{_ai.name}' — will report after {sampleSeconds:F0}s of it actually moving while aggroed.", _ai);
            return;
        }

        if (_ai == null) return;

        // ==== ONLY MEASURE WHAT WE ARE ASKING ABOUT ====
        //
        // The first run sampled a skeleton standing over a dead player: 1403
        // frames, 85 of them moving, and naturally zero reversals. The question
        // is what an enemy does while CHASING, so that is the only thing that
        // counts toward the sample — and the clock is unscaled, because the
        // death screen slows time and a scaled clock stops advancing with it.
        if (_ai.IsDead || !_ai.IsAggroed || PlayerDead())
        {
            _lastPos = _ai.transform.position;
            return;
        }

        Vector3 pos = _ai.transform.position;
        Vector3 step = pos - _lastPos; step.y = 0f;
        _lastPos = pos;
        _frames++;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        float speed = step.magnitude / dt;
        if (speed > 0.2f)
        {
            _movedFrames++;
            _movingTime += dt;
            _stepMin = Mathf.Min(_stepMin, speed);
            _stepMax = Mathf.Max(_stepMax, speed);

            Vector3 heading = step.normalized;
            float turn = Vector3.SignedAngle(_lastHeading, heading, Vector3.up);
            _lastHeading = heading;

            float mag = Mathf.Abs(turn);
            _turnSum += mag;
            _turnMax = Mathf.Max(_turnMax, mag);
            if (mag > 25f) _bigTurns++;

            // A reversal is a meaningful turn back the way it came. That is what
            // a zigzag IS, and it is what a curve around an obstacle is not.
            if (mag > 8f)
            {
                int sign = turn > 0f ? 1 : -1;
                if (_lastTurnSign != 0 && sign != _lastTurnSign) _reversals++;
                _lastTurnSign = sign;
            }
        }

        if (_anim != null)
        {
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != _lastStateHash)
            {
                _animStateChanges++;
                // ==== NAME THE STATES, NOT JUST THE COUNT ====
                //
                // "The clip keeps switching" narrowed it to half the problem;
                // WHICH two states it flips between names the cause outright.
                // Running<->Idle is the isMoving bool flapping. Running<->Hit is
                // the damage trigger. Running<->Attack is the swing. Each has a
                // different fix and they are indistinguishable on screen.
                RecordFlip(_lastStateHash, st.shortNameHash);
                _lastStateHash = st.shortNameHash;
            }

            // The bool behind half of those flips, sampled as it actually reads.
            if (_hasMovingBool)
            {
                bool m = _anim.GetBool("isMoving");
                if (_lastMoving != m) { _movingFlips++; _lastMoving = m; }
            }

            float sp = _anim.speed;
            _speedMin = Mathf.Min(_speedMin, sp);
            _speedMax = Mathf.Max(_speedMax, sp);
            if (_lastAnimSpeed >= 0f && Mathf.Abs(sp - _lastAnimSpeed) > 0.25f) _animSpeedJumps++;
            _lastAnimSpeed = sp;
        }

        // ==== WHICH SYSTEM MOVED THE BODY, AND HOW MANY DID ====
        //
        // The remaining unknown. A solo enemy that was never hit cannot reach
        // 9.7 m/s from anything the AI asks for, so this records the tag of
        // whichever writer produced the largest step each frame, and how many
        // writers ran. More than one in a frame is the shape of this bug in
        // every form it has taken so far.
        // Read from the SUBJECT now. These used to be static, so every number
        // was the whole horde summed together and described nobody.
        if (_ai.DbgWritesThisFrame > 0)
        {
            _writeFrames++;
            _writesTotal += _ai.DbgWritesThisFrame;
            if (_ai.DbgWritesThisFrame > _maxWritesInAFrame) _maxWritesInAFrame = _ai.DbgWritesThisFrame;
            if (_ai.DbgBiggestStep > _worstStep)
            {
                _worstStep = _ai.DbgBiggestStep;
                _worstWriter = _ai.DbgBiggestWriter.ToString();
            }
            // The gap between what was asked for and what the body did is the
            // remaining unknown, so track the worst of each.
            if (_ai.DbgActualStep > _worstActual)
            {
                _worstActual = _ai.DbgActualStep;
                _worstActualHit = _ai.DbgLastHit;
            }
            CountWriter(_ai.DbgBiggestWriter.ToString());
        }

        // Enough MOVEMENT, not enough wall clock.
        if (_movingTime < sampleSeconds) return;
        Report();
    }

    private void Report()
    {
        _done = true;
        EnemyAI.DbgTrackContacts = false;   // stop charging the horde for it
        float secs = Mathf.Max(0.001f, _movingTime);

        // The verdict first: the console shows two lines collapsed, and the
        // whole point of this is that the first line already answers it.
        string verdict;
        float revPerSec = _reversals / secs;
        float statePerSec = _animStateChanges / secs;
        float speedSpread = (_speedMax > 0f ? _speedMax : 0f) - (_speedMin < float.MaxValue ? _speedMin : 0f);

        if (revPerSec >= 2f && statePerSec < 1.5f) verdict = "THE PATH WEAVES -> steering";
        else if (revPerSec < 2f && statePerSec >= 1.5f) verdict = "THE CLIP KEEPS SWITCHING -> animator triggers";
        else if (revPerSec >= 2f && statePerSec >= 1.5f) verdict = "BOTH -> branch selection is flipping";
        else if (speedSpread > 0.8f) verdict = "LEGS LURCH (one clip, uneven rate) -> MatchLocomotion";
        else verdict = "NOTHING ABNORMAL in this sample";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[MotionProbe] {_ai.name}: {verdict}");
        sb.AppendLine($"  sample: {secs:F1}s of movement, {_frames} frames ({_movedFrames} moving)");
        sb.AppendLine($"  heading reversals : {_reversals}  ({_reversals / secs:F1}/s)   <- a straight run is near 0; a zigzag is 3+/s");
        sb.AppendLine($"  turns over 25 deg : {_bigTurns}  ({_bigTurns / secs:F1}/s)");
        sb.AppendLine($"  avg turn/frame    : {(_movedFrames > 0 ? _turnSum / _movedFrames : 0f):F1} deg   max {_turnMax:F0} deg");
        sb.AppendLine($"  world speed       : {(_stepMin < float.MaxValue ? _stepMin : 0f):F1} .. {_stepMax:F1} m/s");
        sb.AppendLine($"  animator state    : {_animStateChanges} change(s)  ({_animStateChanges / secs:F1}/s)");
        sb.AppendLine($"  animator.speed    : {(_speedMin < float.MaxValue ? _speedMin : 0f):F2} .. {_speedMax:F2}   jumps over 0.25: {_animSpeedJumps}");
        sb.AppendLine($"  isMoving flips    : {_movingFlips}  ({_movingFlips / secs:F1}/s)   <- a steady chase should be 0");
        sb.AppendLine($"  position writers  : {(_writeFrames > 0 ? (float)_writesTotal / _writeFrames : 0f):F2} per frame, worst frame had {_maxWritesInAFrame}   <- more than 1 is the bug");
        sb.AppendLine($"  fastest ASKED-FOR step: {_worstStep:F1} m/s by {_worstWriter}");
        sb.AppendLine($"  fastest ACHIEVED step : {_worstActual:F1} m/s   last thing the capsule touched: {_worstActualHit}");
        if (_worstActual > _worstStep * 1.5f && _worstStep > 0.1f)
            sb.AppendLine("    -> the body moved far further than it was asked to. That is CharacterController");
        sb.AppendLine(_worstActual > _worstStep * 1.5f && _worstStep > 0.1f
            ? "       depenetration: the capsule is overlapping the collider named above and being shoved out."
            : "    -> request and result agree, so nothing is shoving it.");
        if (_wNames.Count > 0)
        {
            sb.AppendLine("  frames each writer led, most first:");
            for (int pass = 0; pass < _wNames.Count; pass++)
            {
                int best = -1, bestN = -1;
                for (int i = 0; i < _wNames.Count; i++)
                    if (_wCounts[i] > bestN) { bestN = _wCounts[i]; best = i; }
                if (best < 0 || bestN <= 0) break;
                sb.AppendLine($"    {_wCounts[best],4}x  {_wNames[best]}");
                _wCounts[best] = -1;
            }
        }
        if (_flipNames.Count > 0)
        {
            sb.AppendLine("  state flips, most frequent first:");
            for (int pass = 0; pass < _flipNames.Count; pass++)
            {
                int best = -1, bestN = -1;
                for (int i = 0; i < _flipNames.Count; i++)
                    if (_flipCounts[i] > bestN) { bestN = _flipCounts[i]; best = i; }
                if (best < 0 || bestN <= 0) break;
                sb.AppendLine($"    {_flipCounts[best],4}x  {_flipNames[best]}");
                _flipCounts[best] = -1;
            }
        }
        sb.AppendLine("  ---");
        sb.AppendLine("  Reading it: reversals high + animator state changes low  -> the PATH weaves, look at steering.");
        sb.AppendLine("              reversals low  + animator state changes high -> the CLIP keeps switching, look at triggers.");
        sb.AppendLine("              animator.speed swinging wide                 -> the legs lurch, look at MatchLocomotion.");
        sb.AppendLine("              world speed swinging wide                    -> something else is moving the body.");
        Debug.Log(sb.ToString(), _ai);

        if (!repeat) return;
        _movingTime = 0f;
        _flipNames.Clear(); _flipCounts.Clear(); _movingFlips = 0;
        _wNames.Clear(); _wCounts.Clear();
        _writeFrames = _writesTotal = _maxWritesInAFrame = 0;
        _worstStep = 0f; _worstWriter = "-";
        _worstActual = 0f; _worstActualHit = "-";
        _reversals = _bigTurns = _frames = _movedFrames = _animStateChanges = _animSpeedJumps = 0;
        _turnSum = _turnMax = 0f;
        _speedMin = _stepMin = float.MaxValue;
        _speedMax = _stepMax = 0f;
        _done = false;
    }

    private EnemyAI NearestAggroed()
    {
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return null;
            _player = p.transform;
        }

        EnemyAI best = null;
        float bestSq = 40f * 40f;
        foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
            if (!e.IsAggroed) continue;   // a patrolling enemy is not the question
            float sq = (e.transform.position - _player.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = e; }
        }
        return best;
    }
}
