using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// PART 3 — the fight. Picks up the instant the rider is back on his feet, with
// the skeletons that chased him down closing the ring.
//
// Cameras are built at RUNTIME, around wherever he actually ended up. They cannot
// be authored in the scene: his landing spot depends on the fall, and a camera
// placed in the editor would be pointing at empty ground — which is exactly how
// the earlier acts ended up with cameras stranded at the world origin.
//
// Started by TrailerRideEvent; nothing to place by hand.
public class TrailerBattleDirector : MonoBehaviour
{
    [Header("Staging")]
    [Tooltip("Radius of the ring the horde closes to before the first lunge.")]
    public float ringRadius = 3.2f;
    [Tooltip("Seconds the horde takes to close before the first attack.")]
    public float closeInTime = 1.8f;
    [Tooltip("Minimum attackers. Any that never rose during the chase are woken so the ring is never half empty.")]
    public int minimumAttackers = 6;

    [Header("Shot timing (seconds from the start of the fight)")]
    public float heroShotTime = 0f;
    public float ringShotTime = 1.5f;
    public float clashShotTime = 3.2f;
    public float wideShotTime = 5.0f;
    public float endTime = 7.5f;

    [Header("Look")]
    public float heroFov = 34f;
    public float ringFov = 50f;
    public float clashFov = 30f;
    public float wideFov = 58f;

    private Transform _hero;
    private readonly List<CinemachineCamera> _cams = new List<CinemachineCamera>();

    public static TrailerBattleDirector Begin(Transform hero)
    {
        var go = new GameObject("TrailerBattleDirector");
        var d = go.AddComponent<TrailerBattleDirector>();
        d._hero = hero;
        d.StartCoroutine(d.Run());
        return d;
    }

    private IEnumerator Run()
    {
        if (_hero == null) { Debug.LogWarning("[Trailer] Battle: no hero — nothing to fight around."); yield break; }

        var horde = ReleaseTheHorde();
        Debug.Log($"[Trailer] Battle begins — {horde.Count} attacker(s) closing on '{_hero.name}'.");

        BuildCameras();
        var polish = TrailerCinematicPolish.Instance;

        // 1. LOW HERO SHOT. He is on his feet, they are still coming — the beat
        //    that says he has decided to fight rather than run.
        Live(0);
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(30f);
        yield return new WaitForSeconds(ringShotTime - heroShotTime);

        // 2. THE RING. Wide enough to count them, which is the threat.
        Live(1);
        yield return new WaitForSeconds(clashShotTime - ringShotTime);

        // 3. FIRST CLASH — tight, and time drops out from under it.
        Live(2);
        if (polish != null) { polish.TimeRamp(0.4f, 0.5f, 0.05f, 0.4f); polish.ImpactPunch(0.9f, 0.4f); }
        CameraShakeUtil.TryShake(0.4f, 0.2f);
        StrikeAt(horde);
        yield return new WaitForSeconds(wideShotTime - clashShotTime);

        // 4. WIDE — one man in a circle of them. The image the whole trailer has
        //    been building toward, so it is the one that gets held.
        Live(3);
        yield return new WaitForSeconds(endTime - wideShotTime);

        if (polish != null) polish.FadeToBlack(1.8f);
        Debug.Log("[Trailer] Battle held out on the wide and faded. End of the piece.");
    }

    // Wake anything still buried and turn the whole chase into an attack.
    private List<TrailerUndeadPursuit> ReleaseTheHorde()
    {
        var all = new List<TrailerUndeadPursuit>(
            Object.FindObjectsByType<TrailerUndeadPursuit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

        // Nearest first, so if there are more than we need the ring is the ones
        // actually around him rather than a scattering across the map.
        all.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - _hero.position)
                  .CompareTo(Vector3.SqrMagnitude(b.transform.position - _hero.position)));

        foreach (var s in all)
        {
            if (s == null) continue;
            s.target = _hero;
            s.Charge(ringRadius);
        }

        if (all.Count < minimumAttackers)
            Debug.LogWarning($"[Trailer] Battle: only {all.Count} attacker(s) available (wanted {minimumAttackers}). Run 'Dress Roadside' on spline_p3 so more skeletons line the route.");

        return all;
    }

    private void StrikeAt(List<TrailerUndeadPursuit> horde)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, _hero.position);

        // The hero swings. HeroAnimator carries Attack, so this is the same
        // trigger the game uses rather than a cutscene-only clip.
        var anim = _hero.GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var p in anim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Attack")
                { anim.ResetTrigger("Attack"); anim.SetTrigger("Attack"); break; }
    }

    private void BuildCameras()
    {
        // Anchored to the hero's facing, so the ring reads consistently whichever
        // way the fall happened to leave him pointing.
        Vector3 fwd = _hero.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 c = _hero.position;

        // Low and close, looking up at him: the angle that makes a man standing
        // up read as defiance rather than recovery.
        Add("CM_Part3_Hero", c + fwd * 3.4f + right * 0.8f + Vector3.up * 0.55f, c + Vector3.up * 1.5f, heroFov);
        // Raised three-quarter, far enough back to see the whole circle.
        Add("CM_Part3_Ring", c - fwd * 5.5f + right * 5.0f + Vector3.up * 3.4f, c + Vector3.up * 1.1f, ringFov);
        // Tight over the shoulder for the first exchange.
        Add("CM_Part3_Clash", c - fwd * 1.9f - right * 1.7f + Vector3.up * 1.8f, c + fwd * 2.5f + Vector3.up * 1.3f, clashFov);
        // High wide: one man, surrounded.
        Add("CM_Part3_Wide", c - fwd * 9f + Vector3.up * 7.5f, c + Vector3.up * 1f, wideFov);
    }

    private void Add(string name, Vector3 pos, Vector3 lookAt, float fov)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        // Never leave a camera underground — the fall can end on a slope, and a
        // shot from inside the hill is how earlier acts lost their cameras.
        if (TrailerGroundClamp.TryTerrainY(pos, out float gy) && pos.y < gy + 0.4f) pos.y = gy + 0.4f;

        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized);

        var cam = go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = fov;
        var pr = cam.Priority; pr.Value = 0; cam.Priority = pr;
        _cams.Add(cam);
    }

    private void Live(int index)
    {
        for (int i = 0; i < _cams.Count; i++)
        {
            if (_cams[i] == null) continue;
            var pr = _cams[i].Priority;
            pr.Value = (i == index) ? 400 : 0;   // above the Part 2 rig and the fall camera
            _cams[i].Priority = pr;
        }

        if (index < 0 || index >= _cams.Count || _cams[index] == null) return;
        // Cut, don't glide: these shots are metres apart and a blend would sweep
        // the camera through the fight.
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        _cams[index].PreviousStateIsValid = false;
        _cams[index].InternalUpdateCameraState(Vector3.up, -1f);
    }
}
