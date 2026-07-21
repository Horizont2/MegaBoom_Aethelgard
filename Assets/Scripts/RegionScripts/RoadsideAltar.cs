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

        var pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;
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
