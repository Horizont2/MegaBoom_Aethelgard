using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// PART 3 — the fight, BLOCKED like a scene rather than simulated.
//
// The previous version spawned real enemies and let EnemyAI run them. That is
// exactly what a staged fight must not do: each skeleton decided for itself when
// to approach and when to swing, so they arrived at random, crowded, and stood
// idle whenever their own logic had nothing to do — while the hero rooted to one
// spot hit whatever wandered into reach. Nothing had a role, so nothing read as
// intentional.
//
// Everything here is directed. The prefabs, hit reactions, death VFX and audio
// are still the game's — damage goes through EnemyAI.TakeDamage — but position,
// facing and timing belong to this script.
//
// The beats, in order:
//   1. THE RING      they close and stop, weapons up. He rises. Nobody moves.
//   2. FIRST BLOOD   one breaks ranks. He kills it on the counter, in slow motion.
//   3. TWO AT ONCE   a pair flanks. He takes one — and the other lands a hit.
//   4. HE GOES DOWN  knocked off his feet. The ring steps in around him.
//   5. HE RISES      up through them, and the ring breaks.
//   6. THE LAST ONE  one left. It backs away, turns toward the castle, and falls.
//   7. ALONE         held wide, then out.
public class TrailerBattleDirector : MonoBehaviour
{
    [Header("The horde")]
    public int attackerCount = 7;
    [Tooltip("Radius they close to and hold. Tight enough to be a threat, wide enough that the hero is readable inside it.")]
    public float ringRadius = 4.2f;
    [Tooltip("Where they start before closing.")]
    public float spawnRadius = 11f;
    public GameObject[] enemyPrefabs;

    [Header("Combat")]
    public float swingDamage = 500f;
    public float swingRange = 3.2f;
    [Tooltip("Delay between a swing starting and the damage landing, so the kill follows the blow instead of preceding it.")]
    public float swingImpactDelay = 0.28f;

    [Header("Look")]
    public float heroFov = 34f, ringFov = 52f, clashFov = 32f, wideFov = 58f;

    [Tooltip("Where the castle lies, so the last skeleton can look toward it before it falls — that glance is what says where they CAME from and turns the fight into part of the story rather than an encounter.")]
    public Transform castleDirection;

    private Transform _hero;
    private Animator _heroAnim;
    private readonly List<TrailerFighter> _horde = new List<TrailerFighter>();
    private CinemachineCamera _cam;
    private int _kills;

    public static TrailerBattleDirector Begin(Transform hero, GameObject[] prefabs = null, int count = 0)
    {
        var go = new GameObject("TrailerBattleDirector");
        var d = go.AddComponent<TrailerBattleDirector>();
        d._hero = hero;
        if (prefabs != null && prefabs.Length > 0) d.enemyPrefabs = prefabs;
        if (count > 0) d.attackerCount = count;
        d.StartCoroutine(d.Run());
        return d;
    }

    private IEnumerator Run()
    {
        if (_hero == null) { Debug.LogWarning("[Trailer] Battle: no hero."); yield break; }

        RestorePlayerForCombat();
        DismissScenerySkeletons();
        Spawn();
        if (_horde.Count == 0) yield break;

        var polish = TrailerCinematicPolish.Instance;
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(35f);

        // ── 1. THE RING ──────────────────────────────────────────────────
        // They close and STOP. The pause before the first blow is what gives the
        // fight a beginning; without it the audience never sees the odds.
        Stage(Shot.Hero);
        FormRing(ringRadius);
        yield return new WaitForSeconds(1.4f);

        Stage(Shot.Ring);
        yield return new WaitForSeconds(1.6f);

        // ── 2. FIRST BLOOD ───────────────────────────────────────────────
        var first = Nearest();
        yield return StartCoroutine(Lunge(first));
        Stage(Shot.Kill, first);
        if (polish != null) { polish.TimeRamp(0.28f, 0.5f, 0.04f, 0.5f); polish.ImpactPunch(1f, 0.45f); }
        yield return StartCoroutine(HeroStrike(first));
        yield return new WaitForSeconds(0.5f);

        // ── 3. TWO AT ONCE ───────────────────────────────────────────────
        var pair = Take(2);
        foreach (var f in pair) if (f != null) StartCoroutine(Lunge(f));
        Stage(Shot.Clash, pair.Count > 0 ? pair[0] : null);
        yield return new WaitForSeconds(0.7f);

        if (pair.Count > 0) yield return StartCoroutine(HeroStrike(pair[0]));

        // ── 4. HE GOES DOWN ──────────────────────────────────────────────
        // The turn. He has to lose the initiative for the recovery to be worth
        // anything, and the ring has to CLOSE while he is down — a circle that
        // politely waits is the thing that reads as broken NPCs.
        if (pair.Count > 1 && pair[1] != null)
        {
            pair[1].Attack(swingImpactDelay);
            yield return new WaitForSeconds(swingImpactDelay);

            Stage(Shot.Down);
            HeroTrigger("Fall");
            CameraShakeUtil.TryShake(0.5f, 0.25f);
            if (polish != null) polish.ImpactPunch(1f, 0.5f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Player_Land, _hero.position);

            FormRing(ringRadius * 0.62f);          // they crowd in over him
            yield return new WaitForSeconds(1.5f);
        }

        // ── 5. HE RISES ──────────────────────────────────────────────────
        Stage(Shot.Rise);
        HeroTrigger("GetUp");
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(30f);
        yield return new WaitForSeconds(1.1f);

        FormRing(ringRadius);                       // the ring is driven back
        if (polish != null) polish.TimeRamp(0.45f, 0.4f, 0.05f, 0.5f);

        // Cut them down, one shot per kill, alternating angle.
        while (_horde.Count > 1)
        {
            var f = Nearest();
            if (f == null) break;
            Stage((_kills % 2 == 0) ? Shot.Clash : Shot.Kill, f);
            yield return StartCoroutine(HeroStrike(f));
            yield return new WaitForSeconds(0.45f);
        }

        // ── 6. THE LAST ONE ──────────────────────────────────────────────
        var last = _horde.Count > 0 ? _horde[0] : null;
        if (last != null)
        {
            Stage(Shot.Kill, last);
            // It backs off and looks the way it came. Whatever sent them is out
            // there, and this is the only line in the piece that says so.
            Vector3 away = Flat(last.transform.position - _hero.position).normalized;
            last.targetPosition = last.transform.position + away * 3.5f;
            yield return new WaitForSeconds(1.2f);

            if (castleDirection != null)
            {
                last.hero = castleDirection;        // turns to face the castle
                yield return new WaitForSeconds(1.1f);
            }
            yield return StartCoroutine(HeroStrike(last));
        }

        // ── 7. ALONE ─────────────────────────────────────────────────────
        Stage(Shot.Wide);
        SetHeroSpeed(0f);
        yield return new WaitForSeconds(2.8f);

        if (polish != null) polish.FadeToBlack(1.8f);
        Debug.Log("[Trailer] Battle won. Next: the climb, and the castle.");
    }

    // ── Staging ──────────────────────────────────────────────────────────

    private void FormRing(float radius)
    {
        for (int i = 0; i < _horde.Count; i++)
        {
            var f = _horde[i];
            if (f == null) continue;
            float ang = (360f / Mathf.Max(1, _horde.Count)) * i;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            f.targetPosition = _hero.position + dir * radius;
        }
    }

    // One breaks ranks and comes at him.
    private IEnumerator Lunge(TrailerFighter f)
    {
        if (f == null) yield break;
        Vector3 dir = Flat(f.transform.position - _hero.position).normalized;
        f.targetPosition = _hero.position + dir * (swingRange * 0.8f);
        f.moveSpeed = 4.6f;                        // a charge, not a walk
        yield return new WaitForSeconds(0.55f);
        f.Attack(swingImpactDelay);
    }

    // The hero turns, swings, and this one dies on the blow.
    private IEnumerator HeroStrike(TrailerFighter f)
    {
        if (f == null) yield break;

        Vector3 to = Flat(f.transform.position - _hero.position);
        if (to.sqrMagnitude > 0.01f)
            _hero.rotation = Quaternion.LookRotation(to.normalized);

        // Step in if he is out of reach, so the blow connects instead of swinging
        // at air while the target stands somewhere else.
        float t = 0f;
        while (to.magnitude > swingRange * 0.8f && t < 1.4f && f != null)
        {
            t += Time.deltaTime;
            var cc = _hero.GetComponent<CharacterController>();
            Vector3 step = to.normalized * 3.2f * Time.deltaTime;
            if (cc != null && cc.enabled) cc.Move(step); else _hero.position += step;
            SetHeroSpeed(3.2f);
            to = Flat(f.transform.position - _hero.position);
            yield return null;
        }
        SetHeroSpeed(0f);

        HeroTrigger("Attack");
        yield return new WaitForSeconds(swingImpactDelay);

        if (f == null) yield break;
        var ai = f.GetComponent<EnemyAI>();
        if (ai != null)
            ai.TakeDamage(new DamageInfo
            {
                Amount = swingDamage,
                IsCritical = true,
                HitPoint = f.transform.position + Vector3.up * 1.1f,
                PushDirection = Flat(f.transform.position - _hero.position).normalized,
                KnockbackForce = 8f,
                StunDuration = 0.3f,
                SourceName = "The Watcher"
            });

        _horde.Remove(f);
        _kills++;
        CameraShakeUtil.TryShake(0.2f, 0.1f);
    }

    private TrailerFighter Nearest()
    {
        _horde.RemoveAll(f => f == null || !f.gameObject.activeInHierarchy);
        TrailerFighter best = null; float bd = float.MaxValue;
        foreach (var f in _horde)
        {
            float d = Vector3.SqrMagnitude(f.transform.position - _hero.position);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    private List<TrailerFighter> Take(int n)
    {
        var list = new List<TrailerFighter>();
        _horde.RemoveAll(f => f == null);
        for (int i = 0; i < n && i < _horde.Count; i++) list.Add(_horde[i]);
        return list;
    }

    // ── Setup ────────────────────────────────────────────────────────────

    private void RestorePlayerForCombat()
    {
        if (_hero.parent != null) _hero.SetParent(null, true);

        var pc = _hero.GetComponent<PlayerController>();
        if (pc != null) { pc.enabled = true; pc.isCinematicInvincible = true; }

        var cc = _hero.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        var rb = _hero.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        _heroAnim = _hero.GetComponentInChildren<Animator>();

        var clamp = _hero.GetComponent<TrailerGroundClamp>();
        if (clamp != null) Destroy(clamp);
    }

    private void DismissScenerySkeletons()
    {
        foreach (var s in Object.FindObjectsByType<TrailerUndeadPursuit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (s != null) s.gameObject.SetActive(false);
    }

    private void Spawn()
    {
        var pool = ResolvePrefabs();
        if (pool.Count == 0)
        {
            Debug.LogWarning("[Trailer] Battle: no enemy prefabs. Assign them on TrailerRideEvent (Setup Part 2 does this).");
            return;
        }

        EnemySpawner.IsSpawningBlocked = true;

        for (int i = 0; i < attackerCount; i++)
        {
            float ang = (360f / attackerCount) * i + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 pos = _hero.position + dir * spawnRadius;
            if (TrailerGroundClamp.TryTerrainY(pos, out float gy)) pos.y = gy;

            var go = Instantiate(pool[Random.Range(0, pool.Count)], pos,
                                 Quaternion.LookRotation(-dir));

            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.suppressDrops = true;   // no pickups in a trailer shot

            var f = go.GetComponent<TrailerFighter>() ?? go.AddComponent<TrailerFighter>();
            f.hero = _hero;
            f.targetPosition = pos;
            _horde.Add(f);

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Spawn, pos);
        }
        Debug.Log($"[Trailer] Battle: {_horde.Count} choreographed attacker(s).");
    }

    private List<GameObject> ResolvePrefabs()
    {
        var list = new List<GameObject>();
        if (enemyPrefabs != null) foreach (var p in enemyPrefabs) if (p != null) list.Add(p);
        if (list.Count > 0) return list;

        foreach (var sp in Object.FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sp == null || sp.enemyPool == null) continue;
            foreach (var se in sp.enemyPool)
                if (se != null && se.enemyPrefab != null && !list.Contains(se.enemyPrefab)) list.Add(se.enemyPrefab);
        }
        if (list.Count > 0) return list;

        foreach (var g in Resources.LoadAll<GameObject>("TrailerEnemies")) if (g != null) list.Add(g);
        return list;
    }

    // ── Hero animation ───────────────────────────────────────────────────

    private void HeroTrigger(string param)
    {
        if (_heroAnim == null || _heroAnim.runtimeAnimatorController == null) return;
        foreach (var p in _heroAnim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param)
            { _heroAnim.ResetTrigger(param); _heroAnim.SetTrigger(param); return; }
    }

    private void SetHeroSpeed(float v)
    {
        if (_heroAnim == null || _heroAnim.runtimeAnimatorController == null) return;
        foreach (var p in _heroAnim.parameters)
        {
            if (p.type != AnimatorControllerParameterType.Float) continue;
            if (p.name == "Speed") _heroAnim.SetFloat("Speed", v);
            else if (p.name == "MoveZ") _heroAnim.SetFloat("MoveZ", v > 0.1f ? 1f : 0f);
        }
    }

    // ── Camera ───────────────────────────────────────────────────────────

    private enum Shot { Hero, Ring, Clash, Kill, Down, Rise, Wide }

    private void Stage(Shot shot, TrailerFighter focus = null)
    {
        Vector3 fwd = _hero.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 c = _hero.position;

        Transform foe = focus != null ? focus.transform : (Nearest() != null ? Nearest().transform : null);
        Vector3 toFoe = foe != null ? Flat(foe.position - c).normalized : fwd;
        Vector3 foeRight = Vector3.Cross(Vector3.up, toFoe);

        switch (shot)
        {
            case Shot.Hero:
                Place(c + fwd * 3.6f + right * 0.9f + Vector3.up * 0.5f, c + Vector3.up * 1.5f, heroFov);
                break;
            case Shot.Ring:
                Place(c - fwd * 6f + right * 5.5f + Vector3.up * 3.6f, c + Vector3.up * 1.1f, ringFov);
                break;
            case Shot.Clash:
                Place(c - toFoe * 2.3f - foeRight * 1.8f + Vector3.up * 1.85f,
                      (foe != null ? foe.position : c + toFoe * 3f) + Vector3.up * 1.2f, clashFov);
                break;
            case Shot.Kill:
                // Side-on: the blow crosses the frame instead of going away from
                // the lens, which is the difference between seeing a kill and
                // seeing a back.
                Place(c + foeRight * 3.8f + Vector3.up * 1.1f,
                      Vector3.Lerp(c, foe != null ? foe.position : c + toFoe * 3f, 0.5f) + Vector3.up * 1.2f,
                      clashFov + 6f);
                break;
            case Shot.Down:
                // Low, at his level, looking UP past him at the ring closing in.
                Place(c + toFoe * 2.6f + Vector3.up * 0.45f, c + Vector3.up * 0.7f, clashFov + 10f);
                break;
            case Shot.Rise:
                // Behind and low, rising with him.
                Place(c - fwd * 3.2f + Vector3.up * 0.8f, c + Vector3.up * 1.4f, heroFov + 4f);
                break;
            case Shot.Wide:
                Place(c - fwd * 9.5f + Vector3.up * 7.5f, c + Vector3.up * 1f, wideFov);
                break;
        }
    }

    private void Place(Vector3 pos, Vector3 lookAt, float fov)
    {
        if (_cam == null)
        {
            var go = new GameObject("CM_Part3_Fight");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<CinemachineCamera>();
            var pr = _cam.Priority; pr.Value = 400; _cam.Priority = pr;

            var tgt = _cam.Target; tgt.LookAtTarget = _hero; _cam.Target = tgt;
            var comp = go.AddComponent<CinemachineRotationComposer>();
            comp.Damping = new Vector2(0.5f, 0.5f);
        }

        // Never underground: the fall can end on a slope, and a shot from inside
        // the hill is how earlier acts lost their cameras.
        if (TrailerGroundClamp.TryTerrainY(pos, out float gy) && pos.y < gy + 0.4f) pos.y = gy + 0.4f;

        _cam.transform.position = pos;
        _cam.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized);
        var lens = _cam.Lens; lens.FieldOfView = fov; _cam.Lens = lens;

        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        _cam.PreviousStateIsValid = false;
        _cam.InternalUpdateCameraState(Vector3.up, -1f);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
