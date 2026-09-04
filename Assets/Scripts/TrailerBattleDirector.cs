using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// PART 3 — the fight, played with the REAL game.
//
// The skeletons here are the game's own enemy prefabs, spawned from the scene
// EnemySpawner's pool, so they arrive with EnemyAI, their colliders, health bars,
// hit and death VFX, audio and drops intact. The chase skeletons are scenery —
// TrailerRoadsideDresser strips their AI and colliders precisely so they cannot
// interfere with the ride — so they are dismissed here and replaced by real ones.
// The player is likewise handed back his own controller and collider.
//
// The outcome is scripted: he is made cinematically invincible and his blows are
// dealt through the ordinary EnemyAI.TakeDamage path, so every hit spark, popup,
// death effect and sound is the game's, and he cannot lose a take.
//
// Cameras are built at RUNTIME around wherever he actually ended up: his landing
// spot depends on the fall, and an editor-placed camera would be aiming at empty
// ground — which is how the earlier acts ended up with cameras at the origin.
public class TrailerBattleDirector : MonoBehaviour
{
    [Header("The horde")]
    [Tooltip("How many real enemies to bring. They are spawned from the scene EnemySpawner's pool — the same prefabs the game uses.")]
    public int attackerCount = 7;
    [Tooltip("Radius of the ring they appear on, around the fallen rider.")]
    public float ringRadius = 9f;
    [Tooltip("Prefabs to use. Left empty, the scene EnemySpawner's pool is used.")]
    public GameObject[] enemyPrefabs;

    [Header("Scripted outcome")]
    [Tooltip("Damage per swing. High enough that each blow kills, so the fight reads as him carving through them rather than trading hits.")]
    public float swingDamage = 500f;
    [Tooltip("Reach of a swing, in metres.")]
    public float swingRange = 3.4f;
    [Tooltip("Seconds between swings.")]
    public float swingInterval = 0.85f;
    [Tooltip("Give up and fade out after this long even if something is still standing, so a take can never hang.")]
    public float maxFightSeconds = 26f;

    [Header("Shot timing (seconds)")]
    public float ringShotTime = 1.6f;
    public float clashShotTime = 3.2f;

    [Header("Look")]
    public float heroFov = 34f, ringFov = 52f, clashFov = 32f, wideFov = 58f;

    private Transform _hero;
    private Animator _heroAnim;
    private readonly List<EnemyAI> _enemies = new List<EnemyAI>();
    private bool _firstBlood;
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
        SpawnRealEnemies();

        var polish = TrailerCinematicPolish.Instance;
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(35f);

        Stage(Shot.Hero);                           // low angle: he is on his feet
        yield return new WaitForSeconds(ringShotTime);

        Stage(Shot.Ring);                           // wide enough to count them
        yield return new WaitForSeconds(clashShotTime - ringShotTime);

        Stage(Shot.Clash);                          // tight, first exchange
        CameraShakeUtil.TryShake(0.4f, 0.2f);

        yield return StartCoroutine(FightRoutine());

        Stage(Shot.Wide);                           // one man, the field cleared
        yield return new WaitForSeconds(2.4f);

        if (polish != null) polish.FadeToBlack(1.8f);
        Debug.Log("[Trailer] Battle won. Next: the climb, and the castle.");
    }

    // Hand the player back to himself: his own controller, collider and physics,
    // off the horse. Without this he is still the parked passenger from Act I —
    // control disabled, CharacterController off — and cannot fight at all.
    private void RestorePlayerForCombat()
    {
        if (_hero.parent != null) _hero.SetParent(null, true);

        var pc = _hero.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.enabled = true;
            // Scripted outcome: he cannot lose a take, but everything else about
            // the fight is the real thing.
            pc.isCinematicInvincible = true;
        }

        var cc = _hero.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        var rb = _hero.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        _heroAnim = _hero.GetComponentInChildren<Animator>();
        // The clamp held him on the terrain through the fall; the CharacterController
        // owns his position from here.
        var clamp = _hero.GetComponent<TrailerGroundClamp>();
        if (clamp != null) Destroy(clamp);
    }

    // The chase skeletons are decoration with their AI and colliders stripped, so
    // they cannot be fought. Sink them out of frame rather than leaving statues
    // standing in the middle of a real fight.
    private void DismissScenerySkeletons()
    {
        foreach (var s in Object.FindObjectsByType<TrailerUndeadPursuit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (s != null) s.gameObject.SetActive(false);
    }

    private void SpawnRealEnemies()
    {
        var pool = ResolvePrefabs();
        if (pool.Count == 0)
        {
            Debug.LogWarning("[Trailer] Battle: no enemy prefabs. Assign Enemy Prefabs on TrailerBattleDirector, or give the scene an EnemySpawner with a populated pool.");
            return;
        }

        EnemySpawner.IsSpawningBlocked = true;      // only OUR attackers, no ambient waves

        for (int i = 0; i < attackerCount; i++)
        {
            float ang = (360f / attackerCount) * i + Random.Range(-12f, 12f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 pos = _hero.position + dir * Random.Range(ringRadius * 0.8f, ringRadius * 1.25f);
            if (TrailerGroundClamp.TryTerrainY(pos, out float gy)) pos.y = gy;

            var prefab = pool[Random.Range(0, pool.Count)];
            var go = Instantiate(prefab, pos, Quaternion.LookRotation((_hero.position - pos).normalized));

            var ai = go.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.suppressDrops = true;    // no XP crystals arcing out of a trailer shot
                _enemies.Add(ai);
            }

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Spawn, pos);
        }
        Debug.Log($"[Trailer] Battle: spawned {_enemies.Count} real enemies from {pool.Count} prefab(s).");
    }

    private List<GameObject> ResolvePrefabs()
    {
        var list = new List<GameObject>();
        if (enemyPrefabs != null)
            foreach (var p in enemyPrefabs) if (p != null) list.Add(p);
        if (list.Count > 0) return list;

        foreach (var sp in Object.FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sp == null || sp.enemyPool == null) continue;
            foreach (var se in sp.enemyPool)
                if (se != null && se.enemyPrefab != null && !list.Contains(se.enemyPrefab)) list.Add(se.enemyPrefab);
        }
        if (list.Count > 0) return list;

        // Nothing wired and no spawner in this scene — which is exactly the case
        // in the trailer scene. Fall back to a Resources lookup so the fight can
        // still happen rather than silently not starting.
        var fromResources = Resources.LoadAll<GameObject>("TrailerEnemies");
        foreach (var g in fromResources) if (g != null) list.Add(g);
        return list;
    }

    // He swings on a beat and everything in reach dies. Damage goes through the
    // ordinary EnemyAI.TakeDamage, so the hit sparks, popups, death VFX, sounds
    // and drops are all the game's own.
    private IEnumerator FightRoutine()
    {
        float t = 0f;
        while (t < maxFightSeconds)
        {
            _enemies.RemoveAll(e => e == null || !e.gameObject.activeInHierarchy);
            if (_enemies.Count == 0) yield break;

            EnemyAI nearest = null; float best = float.MaxValue;
            foreach (var e in _enemies)
            {
                float d = Vector3.SqrMagnitude(e.transform.position - _hero.position);
                if (d < best) { best = d; nearest = e; }
            }

            int before = _enemies.Count;

            if (nearest != null)
            {
                // WALK IN, then strike. Standing still and hitting whatever
                // wandered into reach is what made him look inert.
                if (Mathf.Sqrt(best) > swingRange * 0.85f)
                {
                    yield return StartCoroutine(StepToward(nearest.transform, 1.6f));
                    t += 1.6f;
                }
                yield return StartCoroutine(Swing());
                t += swingImpactDelay;
            }

            // CUT ON THE KILLS. A melee cut to a stopwatch drifts out of sync
            // with what is happening in it within a couple of shots; cutting when
            // something dies keeps every change of angle on a beat.
            yield return new WaitForSeconds(swingInterval);
            t += swingInterval;

            _enemies.RemoveAll(e => e == null || !e.gameObject.activeInHierarchy);
            if (_enemies.Count < before)
            {
                _kills += before - _enemies.Count;
                // Alternate between the two fighting angles so consecutive kills
                // never repeat the same frame.
                Stage((_kills % 2 == 0) ? Shot.Clash : Shot.Kill);
            }
        }
        Debug.LogWarning($"[Trailer] Battle ran to its {maxFightSeconds}s limit with {_enemies.Count} still standing — cutting anyway so the take cannot hang.");
    }

    [Tooltip("Delay between starting the swing animation and the damage landing. Without it they died the instant the swing began — before the blade had moved — which is what made the kills look unearned.")]
    public float swingImpactDelay = 0.28f;

    private IEnumerator Swing()
    {
        if (_heroAnim != null && _heroAnim.runtimeAnimatorController != null)
            foreach (var p in _heroAnim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Attack")
                { _heroAnim.ResetTrigger("Attack"); _heroAnim.SetTrigger("Attack"); break; }

        // Let the blade actually travel before anything dies.
        yield return new WaitForSeconds(swingImpactDelay);

        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            if (e == null) continue;
            Vector3 to = e.transform.position - _hero.position;
            if (to.magnitude > swingRange) continue;
            if (Vector3.Dot(_hero.forward, to.normalized) < 0.1f) continue;   // in front only

            // FIRST KILL ONLY. A slow beat used once is an accent; used on every
            // swing it becomes the tempo and stops meaning anything.
            if (!_firstBlood)
            {
                _firstBlood = true;
                if (TrailerCinematicPolish.Instance != null)
                {
                    TrailerCinematicPolish.Instance.TimeRamp(0.28f, 0.45f, 0.04f, 0.55f);
                    TrailerCinematicPolish.Instance.ImpactPunch(1f, 0.45f);
                }
            }

            e.TakeDamage(new DamageInfo
            {
                Amount = swingDamage,
                IsCritical = Random.value > 0.6f,
                HitPoint = e.transform.position + Vector3.up * 1.1f,
                PushDirection = to.normalized,
                KnockbackForce = 7f,
                StunDuration = 0.3f,
                SourceName = "The Watcher"
            });
        }
        CameraShakeUtil.TryShake(0.18f, 0.09f);
    }

    // He does not stand still and let them come. Between swings he closes on the
    // next one — a hero rooted to a spot while a ring shuffles around him reads
    // as a placeholder, not a fight.
    private IEnumerator StepToward(Transform foe, float seconds)
    {
        if (foe == null) yield break;
        var cc = _hero.GetComponent<CharacterController>();
        float t = 0f;
        while (t < seconds && foe != null)
        {
            t += Time.deltaTime;
            Vector3 to = Flat(foe.position - _hero.position);
            float d = to.magnitude;
            if (d <= swingRange * 0.85f) break;          // close enough to strike

            Vector3 step = to.normalized * 2.6f * Time.deltaTime;
            if (cc != null && cc.enabled) cc.Move(step);
            else _hero.position += step;

            _hero.rotation = Quaternion.Slerp(_hero.rotation, Quaternion.LookRotation(to.normalized), Time.deltaTime * 9f);
            SetHeroSpeed(2.6f);
            yield return null;
        }
        SetHeroSpeed(0f);
    }

    // Drives the same locomotion parameters PlayerController would, so his own
    // run/idle blend plays instead of him sliding in an idle pose.
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

    // Shots are STAGED FRESH each time one goes live, around where the fight
    // actually is at that moment. Cameras fixed at the start of the fight end up
    // pointing at ground the action has already left — a static coverage plan
    // cannot follow a melee that moves.
    private enum Shot { Hero, Ring, Clash, Wide, Kill }

    private void Stage(Shot shot)
    {
        Vector3 fwd = _hero.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 c = _hero.position;

        // Frame relative to the nearest attacker where it matters, so the enemy
        // is inside the shot rather than off the edge of it.
        Transform foe = NearestEnemy();
        Vector3 toFoe = foe != null ? Flat(foe.position - c).normalized : fwd;
        Vector3 foeRight = Vector3.Cross(Vector3.up, toFoe);

        switch (shot)
        {
            case Shot.Hero:   // low, looking up: he has decided to fight
                Place(c + fwd * 3.6f + right * 0.9f + Vector3.up * 0.5f, c + Vector3.up * 1.5f, heroFov);
                break;
            case Shot.Ring:   // raised three-quarter, wide enough to count them
                Place(c - fwd * 6f + right * 5.5f + Vector3.up * 3.6f, c + Vector3.up * 1.1f, ringFov);
                break;
            case Shot.Clash:  // over his shoulder INTO the nearest attacker
                Place(c - toFoe * 2.3f - foeRight * 1.8f + Vector3.up * 1.85f,
                      (foe != null ? foe.position : c + toFoe * 3f) + Vector3.up * 1.2f, clashFov);
                break;
            case Shot.Kill:   // side-on and low, so the blow crosses the frame
                Place(c + foeRight * 3.6f + Vector3.up * 1.1f,
                      Vector3.Lerp(c, foe != null ? foe.position : c + toFoe * 3f, 0.5f) + Vector3.up * 1.2f,
                      clashFov + 6f);
                break;
            case Shot.Wide:   // one man, the field cleared
                Place(c - fwd * 9.5f + Vector3.up * 7.5f, c + Vector3.up * 1f, wideFov);
                break;
        }
    }

    private Transform NearestEnemy()
    {
        Transform best = null; float bd = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (e == null) continue;
            float d = Vector3.SqrMagnitude(e.transform.position - _hero.position);
            if (d < bd) { bd = d; best = e.transform; }
        }
        return best;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private CinemachineCamera _cam;

    private void Place(Vector3 pos, Vector3 lookAt, float fov)
    {
        if (_cam == null)
        {
            var go = new GameObject("CM_Part3_Fight");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<CinemachineCamera>();
            var pr = _cam.Priority; pr.Value = 400; _cam.Priority = pr;   // above the Part 2 rig and the fall camera

            // The hero moves during the fight, so every shot tracks him — but
            // loosely, so the framing drifts rather than locking on like a turret.
            var tgt = _cam.Target; tgt.LookAtTarget = _hero; _cam.Target = tgt;
            var comp = go.AddComponent<CinemachineRotationComposer>();
            comp.Damping = new Vector2(0.5f, 0.5f);
        }

        // Never leave a camera underground: the fall can end on a slope, and a
        // shot from inside the hill is how earlier acts lost their cameras.
        if (TrailerGroundClamp.TryTerrainY(pos, out float gy) && pos.y < gy + 0.4f) pos.y = gy + 0.4f;

        _cam.transform.position = pos;
        _cam.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized);
        var lens = _cam.Lens; lens.FieldOfView = fov; _cam.Lens = lens;

        // Cut, don't glide: consecutive shots are metres apart and a blend would
        // sweep the camera straight through the fight.
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        _cam.PreviousStateIsValid = false;
        _cam.InternalUpdateCameraState(Vector3.up, -1f);
    }
}
