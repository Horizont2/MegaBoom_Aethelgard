using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ResourceNode : MonoBehaviour, IDamageable
{
    public enum NodeType { Tree, Rock, Barrel }

    [Header("Type Settings")]
    public NodeType nodeType = NodeType.Tree;
    public float minHealth = 50f;
    public float maxHealth = 200f;

    [Header("Drops")]
    public GameObject dropPrefab;
    public int minDrops = 2;
    public int maxDrops = 5;

    // ==== SIZE IS THE PRICE AND THE PRIZE ====
    //
    // The generator already scatters these at wildly different scales — trees
    // between 0.7x and 1.45x, rocks between 0.5x and 1.3x — and until now every
    // one of them had the same health and dropped the same amount. A boulder and
    // a pebble took the same number of swings and paid the same, which teaches
    // the player that size is decoration and there is nothing to choose between
    // two nodes. It also makes the small ones strictly a waste of time, because
    // the swings cost the same and the reward is identical.
    //
    // Reading the scale the generator ALREADY applied costs nothing and turns
    // every node into a small decision: the pebble is three seconds for one
    // stone, the boulder is fifteen for three. Nothing has to be authored — the
    // variety is already out there in the world.
    [Header("Size scaling")]
    [Tooltip("Let the node's real size decide health and yield. The generator varies these hugely already; this is what makes that variation mean something.")]
    public bool scaleWithSize = true;
    [Tooltip("Metres tall at or below which this counts as the smallest node — minimum health, one drop. Zero picks a sensible default for the node type.")]
    public float smallHeight = 0f;
    [Tooltip("Metres tall at or above which this counts as the largest. Zero picks a sensible default for the node type.")]
    public float largeHeight = 0f;
    [Tooltip("Fewest units a node can ever give, however small. One, because a node that pays nothing is a node the player learns to walk past.")]
    public int minDropsAtSmallest = 1;
    [Tooltip("Most a single node can give at full size.")]
    public int maxDropsAtLargest = 3;
    [Tooltip("Health of the smallest node as a fraction of its rolled health. A pebble should be a couple of swings, not a formality.")]
    [Range(0.1f, 1f)] public float smallHealthFraction = 0.35f;

    // 0 for the smallest node of its kind, 1 for the largest.
    //
    // Measured in METRES off the renderer bounds, not from localScale. The
    // generator multiplies each prefab's AUTHORED scale by a random factor, and
    // those authored scales are not 1 — a rock prefab modelled at 3x with a 0.5x
    // roll ends up at 1.5, which localScale would call "large" while the thing
    // on screen is small. What matters is how big it looks, and that is the only
    // number the player can see.
    private float _sizeFactor = -1f;
    private Vector3 _lastHitDirection = Vector3.zero;

    private float SizeFactor01
    {
        get
        {
            if (_sizeFactor >= 0f) return _sizeFactor;

            float height = 1f;
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                height = b.size.y;
            }

            float small = smallHeight, large = largeHeight;
            if (small <= 0f || large <= small)
            {
                // Defaults per kind, because a "big rock" and a "big tree" are
                // nowhere near the same number of metres.
                switch (nodeType)
                {
                    case NodeType.Tree: small = 3.5f; large = 9f; break;
                    case NodeType.Rock: small = 0.8f; large = 2.6f; break;
                    default:            small = 0.6f; large = 1.8f; break;
                }
            }

            _sizeFactor = Mathf.Clamp01(Mathf.InverseLerp(small, large, height));
            return _sizeFactor;
        }
    }

    [Header("Timber")]
    [Tooltip("A falling tree crushes whatever is underneath it. It topples away from the last blow, so the player chooses the direction by choosing where to stand.")]
    public bool crushesEnemies = true;
    [Tooltip("Damage at full size, scaled down for smaller trees. High on purpose — being under a falling tree should not be survivable for ORDINARY enemies, or the moment is not worth setting up. Elites and bosses are capped separately below.")]
    public float crushBaseDamage = 140f;
    [Tooltip("Most of an elite's maximum health a single tree may take. Enough to matter, nowhere near enough to replace the fight.")]
    [Range(0.02f, 0.6f)] public float crushEliteHealthCap = 0.18f;
    [Tooltip("Most of a boss's maximum health a single tree may take. Deliberately small: a boss fight that ends because a tree fell on it is a boss fight nobody remembers.")]
    [Range(0f, 0.2f)] public float crushBossHealthCap = 0.06f;
    [Tooltip("Stagger a boss instead of hurting it much. The tree still lands on it and the boss still reels — it just does not decide the fight.")]
    public bool crushStaggersBosses = true;
    [Tooltip("How wide the trunk's kill zone is. A little generous, because being clipped by a tree and shrugging it off looks worse than being caught by one that missed slightly.")]
    public float crushRadius = 1.4f;

    [Header("Effects")]
    public ParticleSystem hitEffect;
    public GameObject stumpPrefab;

    private float currentHealth;
    private float actualMaxHealth;
    private Vector3 originalScale;
    private bool isDead = false;

    // Tracked handle for the last hit-SFX instance so we can manually stop
    // it before the FMOD event's tail finishes — otherwise the rustle
    // outlived the bush that made it and stacked over the next chop.
    private int hitSfxHandle = -1;
    // Timer that fades the hit-SFX out shortly after the last hit. Restarted
    // on every TakeDamage; if it completes we know the player stopped hitting.
    private Coroutine autoStopSfxRoutine;

    private IEnumerator AutoStopHitSfxRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.35f);
            hitSfxHandle = -1;
        }
        autoStopSfxRoutine = null;
    }

    private void Start()
    {
        originalScale = transform.localScale;

        actualMaxHealth = Random.Range(minHealth, maxHealth);
        if (scaleWithSize)
        {
            // Big nodes keep their full rolled health; small ones are quick.
            actualMaxHealth *= Mathf.Lerp(smallHealthFraction, 1f, SizeFactor01);
            actualMaxHealth = Mathf.Max(1f, actualMaxHealth);
        }
        currentHealth = actualMaxHealth;
    }

    // How much this node gives up. Size decides the band; the roll inside it
    // keeps two identical-looking boulders from being interchangeable.
    private int RollDropCount()
    {
        if (!scaleWithSize) return Random.Range(minDrops, maxDrops + 1);

        int ceiling = Mathf.Max(minDropsAtSmallest,
                                Mathf.RoundToInt(Mathf.Lerp(minDropsAtSmallest, maxDropsAtLargest, SizeFactor01)));
        // A small node can still roll its single unit; a large one rolls
        // somewhere between one below its ceiling and the ceiling itself, so
        // the biggest rocks are reliably worth the swings without being fixed.
        int floor = Mathf.Max(minDropsAtSmallest, ceiling - 1);
        return Random.Range(floor, ceiling + 1);
    }

    private void OnDestroy()
    {
        // Belt & braces — if the node is destroyed by an external system
        // (pool return, scene unload) mid-hit, don't leak the FMOD instance.
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0f);
            hitSfxHandle = -1;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (isDead) return;

        currentHealth -= info.Amount;

        // Which way the blow came from, so the trunk topples AWAY from whoever
        // is swinging. A tree that falls in a random direction is scenery
        // collapsing; a tree that falls away from you is a tree you AIMED, and
        // the whole appeal of felling one onto a crowd is that you chose the
        // side to stand on.
        Vector3 push = info.PushDirection; push.y = 0f;
        if (push.sqrMagnitude > 0.001f) _lastHitDirection = push.normalized;

        // Fire the impact SFX at the exact moment damage lands. The old
        // code fired it back in PlayerController.ExecuteAttack, which is
        // driven by an animation event whose placement varies per swing
        // clip — some clips triggered it late (thud after the visible
        // contact), some very early (thud on mouse-down instead of on
        // impact). Playing here couples the sound to the actual
        // physics moment the axe connects with the resource.
        if (AudioManager.Instance != null)
        {
            string clip = nodeType == NodeType.Rock
                ? AudioID.Player_HitResource_Stone
                : AudioID.Player_HitResource_Wood;

            // Tracked 3D instance: kill any previous hit-sfx from THIS node
            // before starting the new one so successive chops don't stack a
            // long-tailed rustle on top of itself. Handle is stopped again
            // in DeathRoutine so the sound cannot outlive the bush.
            if (hitSfxHandle != -1)
                AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.05f);
            hitSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(clip, transform.position);
        }

        if (hitEffect != null)
        {
            if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
            hitEffect.Play();
        }

        StopAllCoroutines();

        // Restart auto-fade timer AFTER StopAllCoroutines so it doesn't
        // get killed by its own siblings. If the player stops attacking,
        // this fades the hit loop out ~0.4 s later instead of leaving the
        // FMOD instance ringing until the node dies.
        autoStopSfxRoutine = StartCoroutine(AutoStopHitSfxRoutine(0.4f));

        if (currentHealth <= 0)
        {
            isDead = true;
            StartCoroutine(DeathRoutine());
        }
        else
        {
            if (nodeType == NodeType.Rock)
            {
                float healthPercent = currentHealth / actualMaxHealth;
                Vector3 targetScale = originalScale * Mathf.Max(0.4f, healthPercent);
                StartCoroutine(SquishRoutine(targetScale));
            }
            else
            {
                StartCoroutine(WobbleRoutine());
            }
        }
    }

    private IEnumerator SquishRoutine(Vector3 targetScale)
    {
        float t = 0;
        Vector3 squishScale = new Vector3(targetScale.x * 1.1f, targetScale.y * 0.9f, targetScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(transform.localScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, targetScale, t); yield return null; }
        transform.localScale = targetScale;
    }

    private IEnumerator WobbleRoutine()
    {
        float t = 0;
        Vector3 squishScale = new Vector3(originalScale.x * 1.1f, originalScale.y * 0.9f, originalScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(originalScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, originalScale, t); yield return null; }
        transform.localScale = originalScale;
    }

    // Length of the trunk, so the sweep covers the whole thing rather than a
    // guessed distance. Falls back to something sensible for a node with no
    // renderers rather than sweeping zero metres and crushing nothing.
    private float TrunkLength()
    {
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 6f;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return Mathf.Max(2f, b.size.y);
    }

    private static readonly Collider[] s_crushBuffer = new Collider[32];

    // Damages anything under the trunk RIGHT NOW, once each.
    //
    // Swept every frame of the fall rather than tested once at the end, because
    // the interesting case is an enemy walking through the arc mid-topple — and
    // a single check at the end would miss anything that was hit on the way
    // down and has since been knocked clear.
    private void SweepCrush(Vector3 pivot, float length, float damage, HashSet<EnemyAI> already, bool finalHit = false)
    {
        // The trunk runs from the pivot along the tree's own up axis, which the
        // rotation is currently sweeping toward the ground.
        Vector3 tip = pivot + transform.up * length;
        float radius = crushRadius * (finalHit ? 1.5f : 1f);

        int n = Physics.OverlapCapsuleNonAlloc(pivot + Vector3.up * 0.3f, tip, radius,
                                               s_crushBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (s_crushBuffer[i] == null) continue;
            var foe = s_crushBuffer[i].GetComponentInParent<EnemyAI>();
            if (foe == null || foe.IsDead || already.Contains(foe)) continue;
            already.Add(foe);

            // ==== A TREE DOES NOT DECIDE A BOSS FIGHT ====
            //
            // Flat damage is right for trash: a falling oak should simply end
            // them, and it is the reason setting one up is worth doing. It is
            // badly wrong for anything the player is supposed to actually fight.
            // A boss that dies because the player found a convenient tree is a
            // boss nobody remembers beating, and an elite deleted the same way
            // stops being an elite.
            //
            // So the ceiling is a FRACTION OF THEIR OWN MAXIMUM HEALTH rather
            // than a flat number. That keeps the physics honest — the tree lands
            // on them, they reel, it hurts — while leaving the fight where it
            // belongs. It also scales itself: a tougher boss added later is
            // automatically no more vulnerable to this than the current ones.
            float dealt = damage;
            float stun = 2f;
            float knock = 16f;

            if (foe.isBoss)
            {
                dealt = Mathf.Min(dealt, foe.maxHealth * crushBossHealthCap);
                // Reeling, not launched. A boss knocked across the arena reads
                // as a physics accident rather than as a hit.
                knock = crushStaggersBosses ? 3f : 0f;
                stun = crushStaggersBosses ? 1.2f : 0f;
            }
            else if (foe.isElite)
            {
                dealt = Mathf.Min(dealt, foe.maxHealth * crushEliteHealthCap);
                knock = 8f;
                stun = 1.4f;
            }

            if (dealt <= 0.01f) continue;

            Vector3 push = (foe.transform.position - pivot); push.y = 0f;
            if (push.sqrMagnitude < 0.001f) push = transform.up;

            foe.TakeDamage(new DamageInfo
            {
                Amount = dealt,
                IsCritical = true,
                PushDirection = push.normalized,
                KnockbackForce = knock,
                StunDuration = stun,
                HitPoint = foe.transform.position,
            });
        }
    }

    private IEnumerator DeathRoutine()
    {
        // Kill the last hit rustle with a short fade so it doesn't sing
        // out over the destroyed bush / falling tree.
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.25f);
            hitSfxHandle = -1;
        }

        int dropCount = RollDropCount();
        for (int i = 0; i < dropCount; i++)
        {
            if (dropPrefab != null) Instantiate(dropPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        if (nodeType == NodeType.Tree)
        {
            // Timber! Play the tree-fall crack/crash as the trunk starts to topple.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Env_TreeFall, transform.position);

            Vector3 pivotPoint = transform.position;
            Collider[] cols = GetComponentsInChildren<Collider>();

            // ��������� �������� ����� (�����) ��� �� ���������
            if (cols.Length > 0 && cols[0] != null) pivotPoint.y = cols[0].bounds.min.y;

            // ==========================================
            // Բ�� 2: ������ �������� �� ��������� ������, ��� ���� �� ���������� ������
            // ==========================================
            foreach (Collider c in cols)
            {
                if (c != null) c.enabled = false;
            }

            // ���в����� ��������� ����ֲ� ������ �� ���� �� ���� �����
            Quaternion initialRotation = transform.rotation;

            // ==== TIMBER ====
            //
            // The trunk used to rotate around its own local right axis, which is
            // whatever yaw the generator happened to roll — so a felled tree fell
            // in a random direction and meant nothing. It falls away from the
            // last blow now, which turns chopping into aiming: stand on the far
            // side of a crowd, swing, and drop several tonnes of oak on them.
            //
            // That is the entire idea. Everything below is in service of making
            // the moment land — the sweep that actually crushes, the weight of
            // the impact, and damage that scales with how big the tree was,
            // because a sapling flattening a warband would be a joke.
            Vector3 fallDir = _lastHitDirection.sqrMagnitude > 0.001f ? _lastHitDirection : transform.forward;
            fallDir.y = 0f;
            if (fallDir.sqrMagnitude < 0.001f) fallDir = Vector3.forward;
            fallDir.Normalize();
            Vector3 fallAxis = Vector3.Cross(Vector3.up, fallDir);   // topples along fallDir

            float trunkLength = TrunkLength();
            float crushDamage = crushBaseDamage * Mathf.Lerp(0.45f, 1f, SizeFactor01);
            var crushed = new HashSet<EnemyAI>();

            float fallDuration = 0.7f;   // natural topple — 0.32 whipped over far too fast
            float fallSpeed = 90f / fallDuration;
            float t = 0;

            while (t < fallDuration)
            {
                t += Time.deltaTime;
                transform.RotateAround(pivotPoint, fallAxis, fallSpeed * Time.deltaTime);
                if (crushesEnemies) SweepCrush(pivotPoint, trunkLength, crushDamage, crushed);
                yield return null;
            }

            if (crushesEnemies)
            {
                // The landing itself. A last, wider sweep so anything the swinging
                // trunk clipped past still gets flattened by the crown coming
                // down, and a thump the player can feel.
                SweepCrush(pivotPoint, trunkLength, crushDamage, crushed, finalHit: true);
                if (AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, pivotPoint + fallDir * trunkLength * 0.6f);
                // A directional thump along the way the trunk went, so the
                // weight of it registers in the hands as well as the eyes.
                if (crushed.Count > 0)
                    CameraShakeUtil.TryDirectionalShake(fallDir, 0.5f, 0.35f, 0.4f);
            }

            if (hitEffect != null)
            {
                if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
                hitEffect.Play();
            }

            // ����������Ӫ�� ��������� ����ֲ� ��� ������
            if (stumpPrefab != null) Instantiate(stumpPrefab, pivotPoint, initialRotation);

            yield return new WaitForSeconds(0.5f);
            Destroy(gameObject);
        }
        else
        {
            // Rock/ore shatters — play the stone-break crumble.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, transform.position);

            if (hitEffect != null)
            {
                if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
                hitEffect.Play();
            }

            // Hide every renderer AND every collider in the hierarchy —
            // compound rocks/nodes use child colliders that used to keep
            // ghosting invisible walls when only the root got disabled.
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            yield return new WaitForSeconds(1.5f);
            Destroy(gameObject);
        }
    }
}