using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Reusable "summon a pack of minions" ability. Attach to any enemy or boss, or
// have EnemyAI / TutorialBossAI add + Configure() it in code. Autonomous: it
// finds the player and, while the player is within aggroRange and the ability
// is off cooldown, casts — faces the player, plays a cast animation, shows VFX,
// waits a short windup, then spawns a ring of minions on the ground around the
// caster. Caps the number of live summons so a caster can't flood the arena.
[DisallowMultipleComponent]
public class MinionSummonAbility : MonoBehaviour
{
    [Header("What to summon")]
    public GameObject[] minionPrefabs;
    public int minCount = 2;
    public int maxCount = 4;
    [Tooltip("Cap on simultaneously-alive summons from THIS caster.")]
    public int maxActiveMinions = 8;
    [Tooltip("When true the caster refuses to summon again until EVERY previous minion is dead (e.g. the Skeleton Mage). The cooldown timer still gates the next cast once they're gone.")]
    public bool requireAllDead = false;

    [Header("Timing")]
    public float cooldown = 12f;
    public float initialDelay = 4f;
    [Tooltip("Delay between the cast animation and the minions appearing, so the summon reads.")]
    public float castWindup = 0.8f;

    [Header("Placement")]
    public float aggroRange = 30f;
    public float spawnRadius = 3.5f;

    [Header("Presentation")]
    [Tooltip("Animator trigger played on cast. 'Attack' exists on the shared enemy animator; use a dedicated 'Cast' state if the model has one.")]
    public string castAnimTrigger = "Attack";
    public GameObject castVFX;         // spawned at the caster during the cast
    public GameObject minionSpawnVFX;  // spawned at each minion's emerge point

    private float nextCastTime;
    private Transform player;
    private Animator anim;
    private bool casting;
    private readonly List<GameObject> active = new List<GameObject>(16);

    // Code-driven setup from EnemyAI / TutorialBossAI. Any null/empty VFX or
    // trigger is left at its serialized default.
    public void Configure(GameObject[] prefabs, int min, int max, int maxActive,
                          float cd, float windup, float range, float radius,
                          string trigger, GameObject castFx, GameObject spawnFx,
                          bool reqAllDead = false)
    {
        minionPrefabs = prefabs;
        minCount = Mathf.Max(1, min);
        // Cap the pack size + how often it can cast — summoners were flooding the
        // arena into a "soup" of skeletons. A shorter pack and a real cooldown
        // floor keep them threatening without overwhelming.
        maxCount = Mathf.Clamp(Mathf.Max(minCount, max), minCount, 3);
        maxActiveMinions = Mathf.Clamp(maxActive, 1, 4);
        requireAllDead = reqAllDead;
        cooldown = Mathf.Max(10f, cd);
        castWindup = Mathf.Max(0f, windup);
        aggroRange = Mathf.Max(1f, range);
        spawnRadius = Mathf.Max(1f, radius);
        if (!string.IsNullOrEmpty(trigger)) castAnimTrigger = trigger;
        if (castFx != null) castVFX = castFx;
        if (spawnFx != null) minionSpawnVFX = spawnFx;
        nextCastTime = Time.time + initialDelay;
    }

    private void OnEnable()
    {
        // Reset the cooldown on (re)spawn — pooled casters reuse this component.
        nextCastTime = Time.time + initialDelay;
        casting = false;
    }

    private void Update()
    {
        if (casting || minionPrefabs == null || minionPrefabs.Length == 0) return;
        if (Time.time < nextCastTime) return;

        PruneActive();
        // "Can't summon while my children live" casters wait for the whole
        // previous pack to die before the next batch (checked again, not just
        // the cap). Poll cheaply until they're gone.
        if (requireAllDead && active.Count > 0) { nextCastTime = Time.time + 1f; return; }
        if (active.Count >= maxActiveMinions) { nextCastTime = Time.time + 2f; return; }

        Transform p = ResolvePlayer();
        if (p == null) return;
        if ((p.position - transform.position).sqrMagnitude > aggroRange * aggroRange) return;

        StartCoroutine(CastRoutine());
    }

    private IEnumerator CastRoutine()
    {
        casting = true;

        Transform p = ResolvePlayer();
        if (p != null)
        {
            Vector3 look = p.position - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null && !string.IsNullOrEmpty(castAnimTrigger)) anim.SetTriggerSafe(castAnimTrigger);
        // World-space + graceful fade (VFXAutoFade) — these VFX prefabs loop, so
        // an un-cleaned instance would hang around forever, and a hard Destroy
        // would cut them mid-emission.
        if (castVFX != null)
        {
            var cfx = Instantiate(castVFX, transform.position + Vector3.up * 0.1f, Quaternion.identity);
            ShaderRepair.Fix(cfx);   // never let the cast aura render magenta
            cfx.AddComponent<VFXAutoFade>().Configure(2.5f, world: true);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Totem_Activate, transform.position);

        yield return new WaitForSeconds(castWindup);

        PruneActive();
        int count = Random.Range(minCount, maxCount + 1);
        count = Mathf.Min(count, Mathf.Max(0, maxActiveMinions - active.Count));

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = minionPrefabs[Random.Range(0, minionPrefabs.Length)];
            if (prefab == null) continue;

            float ang = (360f / Mathf.Max(1, count)) * i + Random.Range(-15f, 15f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 pos = GroundAt(transform.position + dir * spawnRadius);

            if (minionSpawnVFX != null)
            {
                var sfx = Instantiate(minionSpawnVFX, pos, Quaternion.identity);
                ShaderRepair.Fix(sfx);   // never let the spawn puff render magenta
                sfx.AddComponent<VFXAutoFade>().Configure(2f, world: true);
            }
            GameObject m = Instantiate(prefab, pos, Quaternion.Euler(0f, ang + 180f, 0f));
            if (m != null)
            {
                // Summoned skeletons were rendering magenta — their prefab
                // material used a shader missing from the build. Repair on spawn.
                ShaderRepair.Fix(m);
                active.Add(m);
            }
        }

        nextCastTime = Time.time + cooldown;
        casting = false;
    }

    private Vector3 GroundAt(Vector3 p)
    {
        // Prefer a real downward raycast (mesh floors / decks), fall back to terrain.
        if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
            return hit.point;
        if (Terrain.activeTerrain != null)
            p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
        return p;
    }

    private void PruneActive()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (active[i] == null) active.RemoveAt(i);
    }

    private Transform ResolvePlayer()
    {
        if (player != null) return player;
        GameObject pgo = GameObject.FindGameObjectWithTag("Player");
        if (pgo != null) player = pgo.transform;
        return player;
    }
}
