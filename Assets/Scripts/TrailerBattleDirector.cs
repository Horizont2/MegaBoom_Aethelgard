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
    private readonly List<CinemachineCamera> _cams = new List<CinemachineCamera>();
    private readonly List<EnemyAI> _enemies = new List<EnemyAI>();

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
        if (_hero == null) { Debug.LogWarning("[Trailer] Battle: no hero."); yield break; }

        RestorePlayerForCombat();
        DismissScenerySkeletons();
        SpawnRealEnemies();
        BuildCameras();

        var polish = TrailerCinematicPolish.Instance;
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(35f);

        Live(0);                                    // low angle: he is on his feet
        yield return new WaitForSeconds(ringShotTime);

        Live(1);                                    // wide enough to count them
        yield return new WaitForSeconds(clashShotTime - ringShotTime);

        Live(2);                                    // tight, first exchange
        if (polish != null) { polish.TimeRamp(0.4f, 0.5f, 0.05f, 0.4f); polish.ImpactPunch(0.9f, 0.4f); }
        CameraShakeUtil.TryShake(0.4f, 0.2f);

        yield return StartCoroutine(FightRoutine());

        Live(3);                                    // one man, the field cleared
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
            if (ai != null) _enemies.Add(ai);

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

            if (nearest != null)
            {
                Vector3 look = nearest.transform.position - _hero.position; look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    _hero.rotation = Quaternion.Slerp(_hero.rotation, Quaternion.LookRotation(look.normalized), Time.deltaTime * 8f);

                if (Mathf.Sqrt(best) <= swingRange) Swing();
            }

            yield return new WaitForSeconds(swingInterval);
            t += swingInterval;
        }
        Debug.LogWarning($"[Trailer] Battle ran to its {maxFightSeconds}s limit with {_enemies.Count} still standing — cutting anyway so the take cannot hang.");
    }

    private void Swing()
    {
        if (_heroAnim != null && _heroAnim.runtimeAnimatorController != null)
            foreach (var p in _heroAnim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Attack")
                { _heroAnim.ResetTrigger("Attack"); _heroAnim.SetTrigger("Attack"); break; }

        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            if (e == null) continue;
            Vector3 to = e.transform.position - _hero.position;
            if (to.magnitude > swingRange) continue;
            if (Vector3.Dot(_hero.forward, to.normalized) < 0.1f) continue;   // in front only

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

    private void BuildCameras()
    {
        Vector3 fwd = _hero.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 c = _hero.position;

        Add("CM_Part3_Hero", c + fwd * 3.4f + right * 0.8f + Vector3.up * 0.55f, c + Vector3.up * 1.5f, heroFov);
        Add("CM_Part3_Ring", c - fwd * 5.5f + right * 5.0f + Vector3.up * 3.4f, c + Vector3.up * 1.1f, ringFov);
        Add("CM_Part3_Clash", c - fwd * 2.1f - right * 1.9f + Vector3.up * 1.8f, c + fwd * 2.5f + Vector3.up * 1.3f, clashFov);
        Add("CM_Part3_Wide", c - fwd * 9f + Vector3.up * 7.5f, c + Vector3.up * 1f, wideFov);
    }

    private void Add(string name, Vector3 pos, Vector3 lookAt, float fov)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        // Never leave a camera underground: the fall can end on a slope, and a
        // shot from inside the hill is how earlier acts lost their cameras.
        if (TrailerGroundClamp.TryTerrainY(pos, out float gy) && pos.y < gy + 0.4f) pos.y = gy + 0.4f;

        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized);

        var cam = go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = fov;
        var pr = cam.Priority; pr.Value = 0; cam.Priority = pr;

        // The fight cameras are static; the hero moves, so aim them at him.
        var tgt = cam.Target; tgt.LookAtTarget = _hero; cam.Target = tgt;
        var comp = go.AddComponent<CinemachineRotationComposer>();
        comp.Damping = new Vector2(0.6f, 0.6f);

        _cams.Add(cam);
    }

    private void Live(int index)
    {
        for (int i = 0; i < _cams.Count; i++)
        {
            if (_cams[i] == null) continue;
            var pr = _cams[i].Priority;
            pr.Value = (i == index) ? 400 : 0;      // above the Part 2 rig and the fall camera
            _cams[i].Priority = pr;
        }
        if (index < 0 || index >= _cams.Count || _cams[index] == null) return;

        // Cut, don't glide: these shots are metres apart and a blend would sweep
        // the camera straight through the fight.
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        _cams[index].PreviousStateIsValid = false;
        _cams[index].InternalUpdateCameraState(Vector3.up, -1f);
    }
}
