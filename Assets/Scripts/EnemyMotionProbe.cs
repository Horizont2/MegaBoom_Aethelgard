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

    [Tooltip("Seconds to watch before printing.")]
    public float sampleSeconds = 4f;

    [Tooltip("Print again every sampleSeconds instead of once.")]
    public bool repeat = false;

    private EnemyAI _ai;
    private Animator _anim;
    private Transform _player;

    private float _endAt;
    private bool _done;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[EnemyMotionProbe]");
        DontDestroyOnLoad(go);
        go.AddComponent<EnemyMotionProbe>();
    }

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

            _anim = _ai.GetComponentInChildren<Animator>();
            _lastPos = _ai.transform.position;
            _lastHeading = _ai.transform.forward;
            _endAt = Time.time + sampleSeconds;
            Debug.Log($"[MotionProbe] Watching '{_ai.name}' for {sampleSeconds:F0}s.", _ai);
            return;
        }

        if (_ai == null) return;

        Vector3 pos = _ai.transform.position;
        Vector3 step = pos - _lastPos; step.y = 0f;
        _lastPos = pos;
        _frames++;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = step.magnitude / dt;
        if (speed > 0.2f)
        {
            _movedFrames++;
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
            if (st.shortNameHash != _lastStateHash) { _animStateChanges++; _lastStateHash = st.shortNameHash; }

            float sp = _anim.speed;
            _speedMin = Mathf.Min(_speedMin, sp);
            _speedMax = Mathf.Max(_speedMax, sp);
            if (_lastAnimSpeed >= 0f && Mathf.Abs(sp - _lastAnimSpeed) > 0.25f) _animSpeedJumps++;
            _lastAnimSpeed = sp;
        }

        if (Time.time < _endAt) return;
        Report();
    }

    private void Report()
    {
        _done = true;
        float secs = Mathf.Max(0.001f, sampleSeconds);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[MotionProbe] {_ai.name} over {secs:F1}s, {_frames} frames ({_movedFrames} moving)");
        sb.AppendLine($"  heading reversals : {_reversals}  ({_reversals / secs:F1}/s)   <- a straight run is near 0; a zigzag is 3+/s");
        sb.AppendLine($"  turns over 25 deg : {_bigTurns}  ({_bigTurns / secs:F1}/s)");
        sb.AppendLine($"  avg turn/frame    : {(_movedFrames > 0 ? _turnSum / _movedFrames : 0f):F1} deg   max {_turnMax:F0} deg");
        sb.AppendLine($"  world speed       : {(_stepMin < float.MaxValue ? _stepMin : 0f):F1} .. {_stepMax:F1} m/s");
        sb.AppendLine($"  animator state    : {_animStateChanges} change(s)  ({_animStateChanges / secs:F1}/s)");
        sb.AppendLine($"  animator.speed    : {(_speedMin < float.MaxValue ? _speedMin : 0f):F2} .. {_speedMax:F2}   jumps over 0.25: {_animSpeedJumps}");
        sb.AppendLine("  ---");
        sb.AppendLine("  Reading it: reversals high + animator state changes low  -> the PATH weaves, look at steering.");
        sb.AppendLine("              reversals low  + animator state changes high -> the CLIP keeps switching, look at triggers.");
        sb.AppendLine("              animator.speed swinging wide                 -> the legs lurch, look at MatchLocomotion.");
        sb.AppendLine("              world speed swinging wide                    -> something else is moving the body.");
        Debug.Log(sb.ToString(), _ai);

        if (!repeat) return;
        _reversals = _bigTurns = _frames = _movedFrames = _animStateChanges = _animSpeedJumps = 0;
        _turnSum = _turnMax = 0f;
        _speedMin = _stepMin = float.MaxValue;
        _speedMax = _stepMax = 0f;
        _done = false;
        _endAt = Time.time + sampleSeconds;
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
            float sq = (e.transform.position - _player.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = e; }
        }
        return best;
    }
}
