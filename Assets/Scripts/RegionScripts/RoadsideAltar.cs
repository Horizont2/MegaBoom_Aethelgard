using System.Collections;
using UnityEngine;

// Interactive altar spawned at the end of dead-end roads by the world
// generator. Independent of RegionTotem / RegionManager — a standalone
// F-prompt encounter that spawns a mini-boss the moment the player
// activates it, then rewards diamonds / xp on the boss's death and
// self-purifies (visuals swap, prompt disappears).
//
// Attach this to the altar prefab you assign to WorldGenerator.altarPrefabs.
// The prefab needs:
//   - a Collider on the root (for the ground-snap logic)
//   - `bossPrefabs` populated in Inspector
//   - an optional `interactHintVFX` child (small floating icon)
//   - optional `activatedVFX` / `purifiedVFX` for feedback
[DisallowMultipleComponent]
public class RoadsideAltar : MonoBehaviour
{
    [Header("Encounter")]
    // One entry per possible boss — a random pick spawns on activate.
    // Usually a normal skeleton overlord prefab already used elsewhere.
    public GameObject[] bossPrefabs;
    public float spawnRadius = 4f;
    // Extra HP / damage multipliers applied to the boss on top of the
    // normal difficulty scaling. Kept low by default so a roadside
    // encounter reads as "tough" but not "gate-boss."
    public float bossHpMultiplier = 1.0f;
    public float bossDamageMultiplier = 1.0f;

    [Header("Reward on kill")]
    public int diamondReward = 25;
    public int xpReward = 40;

    [Header("Interaction")]
    public float interactionRadius = 6f;
    public string interactPromptKey = "PROMPT_ACTIVATE_ALTAR";

    [Header("Visuals (optional)")]
    public GameObject interactHintVFX;
    public GameObject activatedVFX;
    public GameObject purifiedVFX;
    public Light altarLight;
    public Color idleColor = new Color(0.9f, 0.35f, 0.35f);
    public Color purifiedColor = new Color(0.35f, 0.85f, 0.95f);

    [Header("Audio")]
    public string activateAudioID; // e.g. AudioID.Totem_Activate

    private bool activated = false;
    private bool purified = false;
    private bool isPromptShowing = false;
    private Transform player;
    private GameObject spawnedBoss;

    private void Start()
    {
        if (interactHintVFX != null) interactHintVFX.SetActive(true);
        if (activatedVFX != null) activatedVFX.SetActive(false);
        if (purifiedVFX != null) purifiedVFX.SetActive(false);
        if (altarLight != null) altarLight.color = idleColor;

        // Self-ground on spawn. WorldGenerator.GroundPrefabToTerrain only
        // measures the ROOT collider / renderer, so an altar whose mesh or
        // collider is nested under a child (common) floated at pivot
        // height. Here we measure the ACTUAL instantiated bounds (all
        // child renderers) and snap the bottom onto the terrain — robust
        // regardless of prefab pivot or hierarchy.
        SnapToTerrain();

        // Loud one-shot config check — the #1 reason an altar "doesn't
        // summon a boss" is an empty bossPrefabs array on the prefab.
        if (bossPrefabs == null || bossPrefabs.Length == 0)
            Debug.LogWarning($"[RoadsideAltar] '{name}' has NO bossPrefabs wired — activating it will purify with no fight. Assign at least one boss prefab on the altar prefab in the Inspector.");

        var pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;
    }

    private void SnapToTerrain()
    {
        // --- Visual bottom offset ---
        // Only ACTIVE MeshRenderer / SkinnedMeshRenderer count. The old
        // code used GetComponentsInChildren<Renderer>(true), which
        // included INACTIVE renderers (whose bounds are often stale/at
        // world origin) and PARTICLE renderers (VFX glows that extend
        // far below the base). Both corrupted the bounds and buried the
        // altar underground.
        float lowestY = float.MaxValue;
        bool anyMesh = false;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(false))
        {
            if (mr == null || !mr.enabled) continue;
            lowestY = Mathf.Min(lowestY, mr.bounds.min.y);
            anyMesh = true;
        }
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (smr == null || !smr.enabled) continue;
            lowestY = Mathf.Min(lowestY, smr.bounds.min.y);
            anyMesh = true;
        }
        float pivotToBottom = anyMesh ? transform.position.y - lowestY : 0f;

        // --- Real surface height under the altar ---
        // Raycast DOWN so the altar grounds on whatever's actually there
        // — terrain OR a rock/boulder it landed on. terrain.SampleHeight
        // alone returned the TERRAIN height even when a rock sat above it,
        // so an altar on a rock either floated (rock above terrain) or was
        // buried (pivot below rock surface). We ignore the altar's own
        // colliders during the cast.
        Collider[] ownCols = GetComponentsInChildren<Collider>(true);
        foreach (var c in ownCols) if (c != null) c.enabled = false;

        float groundY;
        Vector3 rayStart = transform.position + Vector3.up * 60f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
        }
        else
        {
            Terrain terrain = Terrain.activeTerrain;
            groundY = terrain != null
                ? terrain.SampleHeight(transform.position) + terrain.transform.position.y
                : transform.position.y;
        }

        foreach (var c in ownCols) if (c != null) c.enabled = true;

        Vector3 p = transform.position;
        p.y = groundY + pivotToBottom;
        transform.position = p;
    }

    private void Update()
    {
        if (purified) return;
        if (player == null)
        {
            var pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.transform;
            if (player == null) return;
        }

        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(player.position.x, player.position.z));

        if (!activated && dist <= interactionRadius)
        {
            if (!isPromptShowing && GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr(interactPromptKey));
                isPromptShowing = true;
                // First-time hint when the player first meets an altar.
                if (TutorialHints.Instance != null)
                {
                    TutorialHints.Instance.ShowIfNew("RoadsideAltar",
                        "Roadside altars summon a mini-boss on activation. Defeat it for a diamond + XP bonus. Optional but tempting.", 6f);
                }
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;
                if (interactHintVFX != null) interactHintVFX.SetActive(false);
                StartCoroutine(ActivateRoutine());
            }
        }
        else if (isPromptShowing && GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            isPromptShowing = false;
        }

        // Monitor for the boss's death — reward once, then purify.
        if (activated && spawnedBoss == null)
        {
            OnBossDefeated();
        }
    }

    private IEnumerator ActivateRoutine()
    {
        activated = true;
        if (activatedVFX != null) activatedVFX.SetActive(true);
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(activateAudioID))
            AudioManager.Instance.PlaySFX3D(activateAudioID, transform.position);
        if (Camera.main != null)
        {
            var cf = Camera.main.GetComponent<CameraFollow>();
            if (cf != null) cf.TriggerShake(0.25f, 0.1f);
        }

        yield return new WaitForSeconds(1.2f);

        if (bossPrefabs != null && bossPrefabs.Length > 0)
        {
            GameObject prefab = bossPrefabs[Random.Range(0, bossPrefabs.Length)];
            if (prefab != null)
            {
                Vector3 offset = Random.insideUnitCircle.normalized * spawnRadius;
                Vector3 spawnPos = transform.position + new Vector3(offset.x, 0f, offset.y);
                spawnPos.y = GetGroundY(spawnPos);
                spawnedBoss = Instantiate(prefab, spawnPos, Quaternion.identity);

                // Apply HP/damage multipliers so this altar's boss is
                // slightly beefier than a stock roaming skeleton.
                var bossAI = spawnedBoss.GetComponent<TutorialBossAI>();
                if (bossAI != null)
                {
                    bossAI.InitializeBoss(bossHpMultiplier, bossDamageMultiplier);
                    bossAI.ActivateBoss();
                }
                else
                {
                    var enemyAI = spawnedBoss.GetComponent<EnemyAI>();
                    if (enemyAI != null)
                    {
                        enemyAI.maxHealth *= bossHpMultiplier;
                        enemyAI.damage *= bossDamageMultiplier;
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning($"[RoadsideAltar] '{name}' активовано, але 'bossPrefabs' пустий. Додай префаб боса в Інспекторі.");
            purified = true;
        }
    }

    private void OnBossDefeated()
    {
        purified = true;
        if (activatedVFX != null) activatedVFX.SetActive(false);
        if (purifiedVFX != null) purifiedVFX.SetActive(true);
        if (altarLight != null) altarLight.color = purifiedColor;
        AchievementSystem.Unlock("ALTAR_HUNTER");

        if (ResourceManager.Instance != null && diamondReward > 0)
            ResourceManager.Instance.AddDiamonds(diamondReward);

        var pc = player != null ? player.GetComponent<PlayerController>() : null;
        if (pc != null && xpReward > 0) pc.GainXP((float)xpReward);

        ToastManager.Show(
            LocalizationManager.Tr("TOAST_ALTAR_PURIFIED", diamondReward),
            ToastManager.ToastKind.Achievement);
    }

    private float GetGroundY(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f,
            LayerMask.GetMask("Default", "Terrain", "Ground"))) return hit.point.y;
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}
