using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

// PART 4 — the climb and the reveal. Built for its OWN scene, per the plan: the
// survivor walks a ridge, crests it, and the skeleton castle opens up below.
//
// The reveal is staged in three moves rather than one, because a single wide of a
// castle is a screenshot, not a beat:
//   1. tight and LOW behind him as he climbs, the horizon deliberately withheld —
//      the audience should want to see over the ridge before they are allowed to,
//   2. the crest: the camera keeps rising as he stops, and the castle enters frame
//      from the bottom edge as the land falls away,
//   3. a slow push toward it while he stands small in the foreground, so the
//      scale reads off HIM.
//
// Put it in the reveal scene, assign the walk path and the castle, press Play.
public class TrailerCastleReveal : MonoBehaviour
{
    [Header("Cast")]
    [Tooltip("The survivor. Left empty, the object tagged Player is used.")]
    public Transform hero;
    [Tooltip("The castle, or any transform at its centre — the reveal frames THIS.")]
    public Transform castle;

    [Header("Climb")]
    [Tooltip("Where he starts, below the crest.")]
    public Transform climbStart;
    [Tooltip("The crest he stops on. The castle should not be visible until he reaches it.")]
    public Transform crest;
    public float climbSeconds = 7f;
    [Tooltip("Animator float driven while walking, so his own locomotion plays.")]
    public string speedParam = "Speed";
    public float walkSpeedParam = 1.6f;

    [Header("Reveal")]
    [Tooltip("Seconds the camera keeps rising at the crest while the castle comes into frame.")]
    public float revealSeconds = 4.5f;
    [Tooltip("Seconds of slow push toward the castle afterwards.")]
    public float pushSeconds = 6f;
    public float climbFov = 40f, revealFov = 55f, pushFov = 38f;

    [Header("Sound")]
    public string revealStingId = "Enemy/Boss/Cinematic Whoosh";
    public string ambienceId = "AMB/AMB_Wind";

    private CinemachineCamera _cam;
    private Animator _anim;

    private void Start() { StartCoroutine(Run()); }

    private IEnumerator Run()
    {
        if (hero == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) hero = p.transform;
        }
        if (hero == null || climbStart == null || crest == null)
        {
            Debug.LogWarning("[Trailer] Castle reveal needs a hero, a climb start and a crest.");
            yield break;
        }

        _anim = hero.GetComponentInChildren<Animator>();
        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(ambienceId))
            AudioManager.Instance.PlaySFX(ambienceId);

        BuildCamera();
        hero.position = climbStart.position;
        hero.rotation = Quaternion.LookRotation(Flat(crest.position - climbStart.position));

        // ── 1. THE CLIMB ────────────────────────────────────────────────
        SetSpeed(walkSpeedParam);
        float t = 0f;
        while (t < climbSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / climbSeconds);
            Vector3 p = Vector3.Lerp(climbStart.position, crest.position, k);
            if (TrailerGroundClamp.TryTerrainY(p, out float gy)) p.y = gy;
            hero.position = p;

            // LOW and close behind, so the ridge line hides what is beyond it.
            // Withholding the horizon is what makes cresting it worth anything.
            Vector3 back = -hero.forward;
            Vector3 camPos = hero.position + back * 4.2f + Vector3.up * 1.5f;
            if (TrailerGroundClamp.TryTerrainY(camPos, out float cy) && camPos.y < cy + 0.8f) camPos.y = cy + 0.8f;
            Place(camPos, hero.position + Vector3.up * 1.6f, climbFov);
            yield return null;
        }
        SetSpeed(0f);

        // ── 2. THE CREST ────────────────────────────────────────────────
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(revealStingId))
            AudioManager.Instance.PlaySFX(revealStingId);
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(20f);

        Vector3 riseFrom = _cam.transform.position;
        Vector3 riseTo = hero.position + (-hero.forward) * 7f + Vector3.up * 6.5f;
        Vector3 aim = castle != null ? castle.position : hero.position + hero.forward * 60f;

        t = 0f;
        while (t < revealSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / revealSeconds));
            // Rising while the land falls away is what brings the castle up into
            // frame from the bottom edge, instead of it simply being there.
            Place(Vector3.Lerp(riseFrom, riseTo, k),
                  Vector3.Lerp(hero.position + Vector3.up * 1.6f, aim, k),
                  Mathf.Lerp(climbFov, revealFov, k));
            yield return null;
        }

        // ── 3. THE PUSH ─────────────────────────────────────────────────
        Vector3 pushFrom = _cam.transform.position;
        Vector3 toCastle = Flat(aim - pushFrom).normalized;
        Vector3 pushTo = pushFrom + toCastle * 9f + Vector3.up * 1.2f;

        t = 0f;
        while (t < pushSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / pushSeconds));
            // Narrowing as it pushes compresses the distance and makes the castle
            // loom; he stays in the near frame so the scale is read off him.
            Place(Vector3.Lerp(pushFrom, pushTo, k), aim, Mathf.Lerp(revealFov, pushFov, k));
            yield return null;
        }

        polish.FadeToBlack(2.2f);
        Debug.Log("[Trailer] Castle revealed — end of the piece.");
    }

    private void BuildCamera()
    {
        var go = new GameObject("CM_Part4_Reveal");
        go.transform.SetParent(transform, false);
        _cam = go.AddComponent<CinemachineCamera>();
        var pr = _cam.Priority; pr.Value = 500; _cam.Priority = pr;

        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
    }

    private void Place(Vector3 pos, Vector3 lookAt, float fov)
    {
        if (_cam == null) return;
        _cam.transform.position = pos;
        Vector3 dir = lookAt - pos;
        if (dir.sqrMagnitude > 0.0001f) _cam.transform.rotation = Quaternion.LookRotation(dir.normalized);
        var lens = _cam.Lens; lens.FieldOfView = fov; _cam.Lens = lens;
    }

    private void SetSpeed(float v)
    {
        if (_anim == null || _anim.runtimeAnimatorController == null || string.IsNullOrEmpty(speedParam)) return;
        foreach (var p in _anim.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == speedParam)
            { _anim.SetFloat(speedParam, v); return; }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
