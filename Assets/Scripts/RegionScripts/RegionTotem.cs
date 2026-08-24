using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum EncounterType { Boss, Swarm }

// ─────────────────────────────────────────────────────────────────────
//  Composable encounter templates (optional, data-driven)
// ─────────────────────────────────────────────────────────────────────
// A GROUP is a batch of units; each unit is a RANDOM pick from the group's
// prefab set, so a single group can mix types (a warband of, say, swordsmen +
// archers + a brute). An ENCOUNTER is an ordered sequence of groups (group 0
// could be a boss, group 1 its adds). A totem can hold several encounters and
// rolls one (weighted-random) on activation — so the same totem feels
// different across runs, and you can author bosses, warbands, boss+adds, or
// multi-wave ambushes entirely from the Inspector.
[System.Serializable]
public class TotemSpawnGroup
{
    public string label = "group";
    [Tooltip("Each unit is a random pick from this set → one group can mix enemy types.")]
    public GameObject[] prefabs;
    public int count = 5;
    public float hpMult = 1f;
    public float dmgMult = 1f;
    [Tooltip("Seconds between units within this group.")]
    public float delayBetween = 0.15f;
    [Tooltip("Seconds to wait after this group finishes before the next group.")]
    public float delayAfter = 0.5f;
}

[System.Serializable]
public class TotemEncounter
{
    public string name = "Encounter";
    [Tooltip("Relative chance this encounter is chosen (weighted against the others).")]
    public float weight = 1f;
    [Tooltip("Groups spawn in order. e.g. [Boss] then [Adds], or one mixed [Warband].")]
    public TotemSpawnGroup[] groups;
}

public class RegionTotem : MonoBehaviour
{
    [HideInInspector] public RegionManager manager;

    [Header("Encounter Settings")]
    public EncounterType encounterType = EncounterType.Boss;

    [Header("If Swarm: Setup Mobs")]
    public GameObject[] weakPrefabs;
    public int weakCount = 10;
    public GameObject[] mediumPrefabs;
    public int mediumCount = 5;
    public GameObject[] elitePrefabs;
    public int eliteCount = 2;

    [Header("Encounter Templates (optional — random variety)")]
    [Tooltip("When ON, activating this totem rolls ONE of the encounters below " +
             "(weighted-random) instead of the fixed Boss/Swarm above — a boss, a " +
             "mixed warband, boss+adds, etc., different each run.")]
    public bool useEncounterTemplates = false;
    public TotemEncounter[] encounterTemplates;
    [Tooltip("When ON and no templates are authored above, the totem BUILDS varied " +
             "encounters (warband / ambush / elite pack / boss+adds) from its weak/" +
             "medium/elite + boss pools automatically — instant variety, no wiring.")]
    public bool autoGenerateEncounters = true;

    [Header("Standalone / Side Objective")]
    [Tooltip("Увімкни це, якщо тотем стоїть у тупику як побічна місія (не головний тотем регіону)")]
    public bool isStandalone = false;
    public GameObject[] standaloneBossPrefabs;
    [Tooltip("Diamonds granted when a STANDALONE totem's boss is defeated (region totems reward via RegionManager instead).")]
    public int standaloneDiamondReward = 25;
    [Tooltip("XP granted when a STANDALONE totem's boss is defeated.")]
    public int standaloneXpReward = 40;

    [Header("Visuals & Cinematic")]
    public ParticleSystem idleCorruptionVFX;
    public ParticleSystem activationShieldVFX;
    public ParticleSystem skyBeamVFX;
    [Tooltip("Local-Y offset applied to the sky beam so it looks like it emerges from the top of the totem instead of the totem's origin. Negative = lower.")]
    public float skyBeamYOffset = -1.5f;
    public Light totemLight;

    [Tooltip("Ефект, який працює як маяк і показує, що з тотемом можна взаємодіяти")]
    public GameObject interactHintVFX;

    [Header("Interaction")]
    public float interactionRadius = 8f;

    // ─── AAA multi-phase RAID CAPTURE ────────────────────────────────────
    [Header("Raid Capture (multi-phase: anchors → channel → boss)")]
    [Tooltip("Turn the totem into a 3-phase mini-raid: break the corruption anchors, hold the totem while it channels under reinforcements, then slay the region boss. Falls back to the classic wave when OFF. Standalone roadside altars ignore this and stay quick encounters.")]
    public bool useRaidCapture = true;
    [Header("Phase 1 — Corruption Anchors")]
    [Tooltip("Number of anchors to destroy around the totem before the channel can begin.")]
    public int anchorCount = 3;
    [Tooltip("Optional anchor visual/prefab (needs a CorruptionAnchor or one is added). Empty = procedural glowing crystal.")]
    public GameObject anchorPrefab;
    public float anchorRadius = 13f;
    public float anchorHealth = 120f;
    [Tooltip("Guards spawned around EACH anchor. Falls back to weak/medium pools if empty.")]
    public GameObject[] anchorGuardPrefabs;
    public int guardsPerAnchor = 3;
    [Header("Phase 2 — Channel / Hold")]
    [Tooltip("Seconds the totem channels while you survive reinforcements.")]
    public float purifyDuration = 32f;
    [Tooltip("Seconds between reinforcement waves during the channel.")]
    public float reinforceInterval = 6.5f;
    [Tooltip("Reinforcements that pour in from the compass during the channel. Falls back to weak/medium pools.")]
    public GameObject[] reinforcementPrefabs;
    [Header("Free the Ally (optional)")]
    [Tooltip("An AllyAI companion freed when the first anchor falls — fights at your side for the raid.")]
    public GameObject allyPrefab;
    [Header("World Heal on capture")]
    public bool healWorldOnCapture = true;
    [Tooltip("Optional expanding purification burst spawned at the totem on capture.")]
    public GameObject purifyBurstVFX;
    public Color healedFogColor = new Color(0.62f, 0.72f, 0.82f);
    [Range(0.1f, 1f)] public float healedFogDensityMult = 0.45f;

    [HideInInspector] public bool isPurified = false;

    private bool isLocked = false;
    [HideInInspector] public bool isActivated = false;
    private bool isPromptShowing = false;

    // Scene-wide guard: as soon as ANY totem starts activating this frame,
    // block every other totem's F handler for the rest of the frame. Fixes
    // the two-boss-stack: without this, both totems' Update could observe
    // Input.GetKeyDown(F) in the same tick before OnTotemActivated had a
    // chance to LockTotem the siblings, so both spawned bosses concurrently.
    // Cleared in RegionManager.OnTotemPurified when the wave ends.
    public static bool AnyActivatingRightNow = false;

    private List<GameObject> activeEnemies = new List<GameObject>();
    private Transform player;

    private void Start()
    {
        FindPlayer();
        if (totemLight != null) totemLight.color = Color.red;
        if (activationShieldVFX != null) { activationShieldVFX.Stop(); activationShieldVFX.gameObject.SetActive(false); }

        if (interactHintVFX != null) interactHintVFX.SetActive(true);

        // Nudge the sky beam down along local Y so it visually plugs into the
        // totem instead of hanging above it. Done once in Start so it applies
        // whether the beam is triggered by purification or by re-entering an
        // already-conquered region.
        if (skyBeamVFX != null && Mathf.Abs(skyBeamYOffset) > 0.001f)
        {
            Vector3 lp = skyBeamVFX.transform.localPosition;
            skyBeamVFX.transform.localPosition = new Vector3(lp.x, lp.y + skyBeamYOffset, lp.z);
        }
    }

    private void FindPlayer()
    {
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;
    }

    public void LockTotem(bool locked)
    {
        isLocked = locked;
        if (idleCorruptionVFX != null)
        {
            if (locked) idleCorruptionVFX.Stop();
            else if (!isPurified) idleCorruptionVFX.Play();
        }

        if (interactHintVFX != null) interactHintVFX.SetActive(!locked && !isPurified);
    }

    public void PlayCorruptionFlare()
    {
        if (idleCorruptionVFX != null) idleCorruptionVFX.Emit(50);
        if (totemLight != null) { totemLight.intensity *= 3f; StartCoroutine(DimLightRoutine()); }
    }

    private IEnumerator DimLightRoutine()
    {
        float start = totemLight.intensity;
        float elapsed = 0f;
        while (elapsed < 1f) { elapsed += Time.deltaTime; totemLight.intensity = Mathf.Lerp(start, start / 3f, elapsed); yield return null; }
    }

    private void Update()
    {
        if (isPurified || isActivated || isLocked) return;
        // Second-totem safety: if another totem in the scene started
        // activating this frame, do nothing until it releases the flag.
        if (AnyActivatingRightNow) return;
        if (player == null) { FindPlayer(); if (player == null) return; }

        float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(player.position.x, player.position.z));

        // Pre-gate: the moment the player draws near a raid totem, spawn its
        // corruption anchors. The totem stays locked until they're destroyed.
        if (useRaidCapture && !isStandalone && !_preGateSpawned && dist <= preGateApproachRadius)
        {
            SpawnPreGateAnchors();
        }

        // While anchors still stand, the totem is shielded — show a "destroy the
        // anchors" prompt instead of the purify prompt and refuse activation.
        if (_preGateActive)
        {
            if (dist <= interactionRadius)
            {
                if (!isPromptShowing && GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("DESTROY THE ANCHORS FIRST"));
                    isPromptShowing = true;
                }
            }
            else if (isPromptShowing && GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;
            }
            return; // locked until the shield is down
        }

        if (dist <= interactionRadius)
        {
            if (!isPromptShowing && GlobalHUD.Instance != null)
            {
                if (TutorialHints.Instance != null && !TutorialHints.Instance.HasSeen("Totem"))
                {
                    TutorialHints.Instance.ShowIfNew("Totem",
                        "Stand on the corrupted totem and press <b>F</b> to purify it. A wave of enemies will spawn — survive to claim the region.", 6f);
                }
                else
                {
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("[F] PURIFY TOTEM"));
                }
                isPromptShowing = true;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                // Latch the guard IMMEDIATELY so any sibling totem's
                // Update in the same frame early-returns. RegionManager
                // clears it on OnTotemPurified.
                AnyActivatingRightNow = true;

                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;

                if (interactHintVFX != null) interactHintVFX.SetActive(false);

                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Totem_Activate);

                if (manager != null) manager.OnTotemActivated(this);

                StartCoroutine(ActivationEventRoutine());
            }
        }
        else if (isPromptShowing && GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            isPromptShowing = false;
        }
    }

    private IEnumerator ActivationEventRoutine()
    {
        isActivated = true;
        EnemySpawner.IsSpawningBlocked = true;

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("PurifyWave",
                "Activating a totem summons a wave. Defeat <b>every</b> enemy to purify it — the next totem unlocks afterward.", 6f);

        if (activationShieldVFX != null) { activationShieldVFX.gameObject.SetActive(true); activationShieldVFX.Play(); }
        CameraShakeUtil.TryShake(0.3f, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr(encounterType == EncounterType.Boss ? "SLAY THE OVERLORD!" : "SURVIVE THE SWARM!"));
        yield return new WaitForSeconds(2f);

        int playerPower = PowerSystemManager.Instance != null ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        // CRITICAL: guard `manager` itself, not just `manager.currentRegion`.
        // Standalone roadside totems have NO RegionManager (manager == null),
        // so the old `manager.currentRegion != null` checks threw an NRE
        // right here — killing the coroutine BEFORE the boss spawned. That
        // was the real "roadside totem summons no boss" bug (RoadsideAltar
        // was a red herring — the prefab actually uses RegionTotem).
        RegionData region = manager != null ? manager.currentRegion : null;
        int recommendedPower = region != null ? region.recommendedPower : 100;
        float hpMultBase = region != null ? region.enemyHpMultiplier : 1f;
        float dmgMultBase = region != null ? region.enemyDamageMultiplier : 1f;

        float difficultyMult = PowerSystemManager.CalculateDifficultyMultiplier(playerPower, recommendedPower);
        float finalHpMult = hpMultBase * difficultyMult;
        float finalDmgMult = dmgMultBase * difficultyMult;

        // AAA multi-phase raid capture takes over the whole encounter — but only
        // for real region-location totems, never for standalone roadside altars
        // (those stay quick single-boss/warband encounters).
        if (useRaidCapture && !isStandalone)
        {
            yield return StartCoroutine(RaidCaptureRoutine(finalHpMult, finalDmgMult));
            yield break; // RaidCaptureRoutine starts its own MonitorCombat at the boss phase
        }

        bool haveTemplates = useEncounterTemplates && encounterTemplates != null && encounterTemplates.Length > 0;
        // Auto-build varied encounters from the pools when none were authored.
        if (!haveTemplates && autoGenerateEncounters)
        {
            List<TotemEncounter> auto = BuildAutoEncounters();
            if (auto.Count > 0) { encounterTemplates = auto.ToArray(); haveTemplates = true; }
        }

        if (haveTemplates)
        {
            yield return StartCoroutine(RunEncounterTemplate(finalHpMult, finalDmgMult));
        }
        else if (encounterType == EncounterType.Boss)
        {
            // ФІКС: Якщо це Вівтар у тупику, беремо його власних босів
            if (isStandalone && standaloneBossPrefabs != null && standaloneBossPrefabs.Length > 0)
            {
                for (int i = 0; i < standaloneBossPrefabs.Length; i++)
                {
                    SpawnEntity(standaloneBossPrefabs[i], finalHpMult, finalDmgMult);
                    yield return new WaitForSeconds(0.8f);
                }
            }
            // Оригінальна логіка для Головного Тотему регіону
            else if (manager != null && manager.currentRegion != null)
            {
                if (manager.currentRegion.regionBossPrefabs == null || manager.currentRegion.regionBossPrefabs.Length == 0)
                {
                    Debug.LogError("🚨 ПОМИЛКА: Ти забув додати префаб Боса в 'Region Boss Prefabs' в RegionData цього регіону!");
                    yield break;
                }

                for (int i = 0; i < manager.currentRegion.regionBossPrefabs.Length; i++)
                {
                    SpawnEntity(manager.currentRegion.regionBossPrefabs[i], finalHpMult, finalDmgMult);
                    yield return new WaitForSeconds(0.8f);
                }
            }
        }
        else
        {
            yield return StartCoroutine(SpawnSwarmRoutine(weakPrefabs, weakCount, finalHpMult * 0.8f, finalDmgMult * 0.8f));
            yield return StartCoroutine(SpawnSwarmRoutine(mediumPrefabs, mediumCount, finalHpMult, finalDmgMult));
            yield return StartCoroutine(SpawnSwarmRoutine(elitePrefabs, eliteCount, finalHpMult * 1.5f, finalDmgMult * 1.2f));
        }

        StartCoroutine(MonitorCombatRoutine());
    }

    // Builds a varied set of encounters from whatever prefab pools this totem
    // has (weak / medium / elite + a boss pool), so every road totem offers
    // random variety — a warband, an ambush, an elite pack, or a boss with adds
    // — with zero manual authoring.
    private List<TotemEncounter> BuildAutoEncounters()
    {
        var list = new List<TotemEncounter>();
        bool hasWeak = weakPrefabs != null && weakPrefabs.Length > 0;
        bool hasMed = mediumPrefabs != null && mediumPrefabs.Length > 0;
        bool hasElite = elitePrefabs != null && elitePrefabs.Length > 0;

        // Resolve the final-boss pool. Prefer explicit standalone bosses; then
        // the RegionManager's active region; and finally fall back to
        // GameManager.currentRegion — critical for world-gen-spawned totems,
        // where `manager` is null and the boss phase would otherwise be empty.
        GameObject[] bossPool = null;
        if (standaloneBossPrefabs != null && standaloneBossPrefabs.Length > 0)
            bossPool = standaloneBossPrefabs;
        else if (manager != null && manager.currentRegion != null)
            bossPool = manager.currentRegion.regionBossPrefabs;
        else if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            bossPool = GameManager.Instance.currentRegion.regionBossPrefabs;
        bool hasBoss = bossPool != null && bossPool.Length > 0;

        // Warband — one mixed melee pack.
        if (hasMed || hasWeak)
            list.Add(Encounter("Warband", 1.5f,
                Group("warband", CombinePools(mediumPrefabs, weakPrefabs), hasMed ? 8 : 10, 1f, 1f, 0f)));

        // Ambush — a weak swarm followed by a couple of elites.
        if (hasWeak && hasElite)
            list.Add(Encounter("Ambush", 1.2f,
                Group("swarm", weakPrefabs, 10, 0.85f, 0.85f, 0.8f),
                Group("elites", elitePrefabs, 2, 1.3f, 1.15f, 0f)));

        // Elite pack.
        if (hasElite)
            list.Add(Encounter("Elite Pack", 0.8f,
                Group("elites", elitePrefabs, 3, 1.2f, 1.1f, 0f)));

        // Boss (+ adds if we have fodder).
        if (hasBoss)
        {
            if (hasWeak)
                list.Add(Encounter("Boss & Adds", 0.7f,
                    Group("boss", bossPool, 1, 1f, 1f, 0.6f),
                    Group("adds", weakPrefabs, 6, 0.8f, 0.8f, 0f)));
            else
                list.Add(Encounter("Boss", 1f, Group("boss", bossPool, 1, 1f, 1f, 0f)));
        }

        return list;
    }

    private static TotemEncounter Encounter(string name, float weight, params TotemSpawnGroup[] groups)
    {
        return new TotemEncounter { name = name, weight = weight, groups = groups };
    }

    private static TotemSpawnGroup Group(string label, GameObject[] prefabs, int count, float hp, float dmg, float after)
    {
        return new TotemSpawnGroup { label = label, prefabs = prefabs, count = count, hpMult = hp, dmgMult = dmg, delayBetween = 0.15f, delayAfter = after };
    }

    private static GameObject[] CombinePools(GameObject[] a, GameObject[] b)
    {
        var list = new List<GameObject>();
        if (a != null) list.AddRange(a);
        if (b != null) list.AddRange(b);
        return list.ToArray();
    }

    // Weighted-random pick among this totem's encounter templates.
    private TotemEncounter PickEncounter()
    {
        float total = 0f;
        for (int i = 0; i < encounterTemplates.Length; i++)
            total += Mathf.Max(0f, encounterTemplates[i] != null ? encounterTemplates[i].weight : 0f);

        if (total <= 0f) return encounterTemplates[Random.Range(0, encounterTemplates.Length)];

        float roll = Random.value * total;
        for (int i = 0; i < encounterTemplates.Length; i++)
        {
            if (encounterTemplates[i] == null) continue;
            roll -= Mathf.Max(0f, encounterTemplates[i].weight);
            if (roll <= 0f) return encounterTemplates[i];
        }
        return encounterTemplates[encounterTemplates.Length - 1];
    }

    private IEnumerator RunEncounterTemplate(float baseHpMult, float baseDmgMult)
    {
        TotemEncounter enc = PickEncounter();
        if (enc == null || enc.groups == null) yield break;

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr("CLEAR THE AMBUSH!"));

        for (int g = 0; g < enc.groups.Length; g++)
        {
            TotemSpawnGroup grp = enc.groups[g];
            if (grp == null || grp.prefabs == null || grp.prefabs.Length == 0 || grp.count <= 0) continue;

            for (int i = 0; i < grp.count; i++)
            {
                SpawnEntity(grp.prefabs[Random.Range(0, grp.prefabs.Length)],
                            baseHpMult * grp.hpMult, baseDmgMult * grp.dmgMult);
                if (grp.delayBetween > 0f) yield return new WaitForSeconds(grp.delayBetween);
            }
            if (grp.delayAfter > 0f) yield return new WaitForSeconds(grp.delayAfter);
        }
    }

    private IEnumerator SpawnSwarmRoutine(GameObject[] prefabs, int count, float hpMult, float dmgMult)
    {
        if (prefabs == null || prefabs.Length == 0 || count <= 0) yield break;
        for (int i = 0; i < count; i++)
        {
            SpawnEntity(prefabs[Random.Range(0, prefabs.Length)], hpMult, dmgMult);
            yield return new WaitForSeconds(0.15f);
        }
    }

    private void SpawnEntity(GameObject prefab, float hpMult, float dmgMult)
    {
        Vector3 spawnPos = transform.position + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(6f, 12f));
        spawnPos.y = GetGroundHeight(spawnPos);
        SpawnEntityAt(prefab, spawnPos, hpMult, dmgMult);
    }

    private void SpawnEntityAt(GameObject prefab, Vector3 spawnPos, float hpMult, float dmgMult)
    {
        if (prefab == null) return;
        GameObject entity = Instantiate(prefab, spawnPos, Quaternion.identity);
        activeEnemies.Add(entity);

        // ФІКС: Більше ніяких isBoss прапорців. Перевіряємо напряму, чи це бос!
        TutorialBossAI bossAI = entity.GetComponent<TutorialBossAI>();
        if (bossAI != null)
        {
            bossAI.InitializeBoss(hpMult, dmgMult);
            bossAI.ActivateBoss();
            CameraShakeUtil.TryShake(0.2f, 0.1f);
        }
        else
        {
            EnemyAI enemyAI = entity.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.maxHealth *= hpMult;
                enemyAI.damage *= dmgMult;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AAA multi-phase RAID CAPTURE
    // ─────────────────────────────────────────────────────────────────────
    private int _anchorsRemaining;
    private int _anchorsTotal;
    private bool _allyFreed;

    // ── Pre-gate: the corruption anchors now appear AROUND the totem the moment
    //    the player draws near, and the main totem stays LOCKED until every
    //    anchor is destroyed. Only then does the "[F] PURIFY" prompt appear.
    [Header("Raid pre-gate")]
    [Tooltip("Distance at which approaching the totem spawns its corruption anchors (should exceed interactionRadius).")]
    public float preGateApproachRadius = 32f;
    private bool _preGateSpawned;   // anchors have been placed
    private bool _preGateActive;    // anchors placed AND not all destroyed yet → totem locked
    private float _preGateHpMult = 1f;
    private float _preGateDmgMult = 1f;

    private IEnumerator RaidCaptureRoutine(float hpMult, float dmgMult)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(12f);

        // ── PHASE 1 — break the corruption anchors (sub-points) ──
        // With the pre-gate the anchors were already spawned + destroyed BEFORE
        // the player could activate the totem, so skip straight to the channel.
        if (!_preGateSpawned)
        {
            yield return StartCoroutine(SpawnRaidAnchorsRoutine(hpMult, dmgMult));
            while (_anchorsRemaining > 0) yield return new WaitForSeconds(0.25f);
        }

        // ── PHASE 2 — hold the totem while it channels, under reinforcements ──
        if (activationShieldVFX != null && !activationShieldVFX.isPlaying) activationShieldVFX.Play();
        CameraShakeUtil.TryShake(0.3f, 0.12f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Totem_Activate, transform.position);

        GameObject[] reinforcePool = (reinforcementPrefabs != null && reinforcementPrefabs.Length > 0)
            ? reinforcementPrefabs : CombinePools(mediumPrefabs, weakPrefabs);

        float elapsed = 0f, nextWave = 1.5f;
        while (elapsed < purifyDuration)
        {
            elapsed += Time.deltaTime;
            if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(6f);
            int pct = Mathf.RoundToInt(Mathf.Clamp01(elapsed / purifyDuration) * 100f);
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr("HOLD THE TOTEM — PURIFYING") + "  " + pct + "%");

            if (totemLight != null) totemLight.color = Color.Lerp(Color.red, new Color(0f, 0.8f, 1f), elapsed / purifyDuration);

            if (elapsed >= nextWave)
            {
                nextWave = elapsed + reinforceInterval;
                float frac = elapsed / purifyDuration;
                int count = Mathf.RoundToInt(Mathf.Lerp(3f, 7f, frac));
                float ramp = Mathf.Lerp(0.75f, 1.5f, frac);
                StartCoroutine(ReinforcementWave(reinforcePool, count, hpMult * ramp, dmgMult));
            }
            yield return null;
        }

        // ── PHASE 3 — final wave + region boss ──
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr("SLAY THE OVERLORD!"));
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(25f);
        CameraShakeUtil.TryShake(0.5f, 0.18f);

        for (int i = 0; i < 5 && reinforcePool != null && reinforcePool.Length > 0; i++)
        {
            SpawnEntity(reinforcePool[Random.Range(0, reinforcePool.Length)], hpMult, dmgMult);
            yield return new WaitForSeconds(0.15f);
        }

        // Resolve the final-boss pool. Prefer explicit standalone bosses; then
        // the RegionManager's active region; and finally fall back to
        // GameManager.currentRegion — critical for world-gen-spawned totems,
        // where `manager` is null and the boss phase would otherwise be empty.
        GameObject[] bossPool = null;
        if (standaloneBossPrefabs != null && standaloneBossPrefabs.Length > 0)
            bossPool = standaloneBossPrefabs;
        else if (manager != null && manager.currentRegion != null)
            bossPool = manager.currentRegion.regionBossPrefabs;
        else if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            bossPool = GameManager.Instance.currentRegion.regionBossPrefabs;
        if (bossPool != null && bossPool.Length > 0)
            for (int i = 0; i < bossPool.Length; i++)
            {
                SpawnEntity(bossPool[i], hpMult, dmgMult);
                yield return new WaitForSeconds(0.6f);
            }

        // Now that the boss + final wave are out, watch for the clear → purify.
        StartCoroutine(MonitorCombatRoutine());
    }

    // Spawn the ring of corruption anchors + their guards. Shared by the raid
    // routine (legacy post-activation path) and the pre-gate.
    private IEnumerator SpawnRaidAnchorsRoutine(float hpMult, float dmgMult)
    {
        _anchorsTotal = Mathf.Max(1, anchorCount);
        _anchorsRemaining = _anchorsTotal;
        UpdateAnchorObjective();

        GameObject[] guardPool = (anchorGuardPrefabs != null && anchorGuardPrefabs.Length > 0)
            ? anchorGuardPrefabs : CombinePools(mediumPrefabs, weakPrefabs);

        for (int i = 0; i < _anchorsTotal; i++)
        {
            float ang = (360f / _anchorsTotal) * i + Random.Range(-12f, 12f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 pos = transform.position + dir * anchorRadius;
            pos.y = GetGroundHeight(pos);
            SpawnAnchor(pos, anchorHealth * Mathf.Lerp(1f, hpMult, 0.5f));

            for (int g = 0; g < guardsPerAnchor && guardPool != null && guardPool.Length > 0; g++)
            {
                Vector3 gp = pos + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(2f, 4.5f));
                gp.y = GetGroundHeight(gp);
                SpawnEntityAt(guardPool[Random.Range(0, guardPool.Length)], gp, hpMult, dmgMult);
            }
            yield return new WaitForSeconds(0.35f);
        }
    }

    // Pre-gate: called from Update when the player first approaches. Spawns the
    // anchors around the still-locked totem so they must be cleared before the
    // totem can be purified. Combat begins here, not on activation.
    private void SpawnPreGateAnchors()
    {
        if (_preGateSpawned) return;
        _preGateSpawned = true;
        _preGateActive = true;
        EnemySpawner.IsSpawningBlocked = true;

        // Difficulty scaling, resolved up front (mirrors ActivationEventRoutine).
        int playerPower = PowerSystemManager.Instance != null ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        RegionData region = manager != null ? manager.currentRegion
                          : (GameManager.Instance != null ? GameManager.Instance.currentRegion : null);
        int recommendedPower = region != null ? region.recommendedPower : 100;
        float difficultyMult = PowerSystemManager.CalculateDifficultyMultiplier(playerPower, recommendedPower);
        _preGateHpMult = (region != null ? region.enemyHpMultiplier : 1f) * difficultyMult;
        _preGateDmgMult = (region != null ? region.enemyDamageMultiplier : 1f) * difficultyMult;

        if (idleCorruptionVFX != null && !idleCorruptionVFX.isPlaying) idleCorruptionVFX.Play();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);
        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("CorruptionAnchors",
                "The totem is shielded by <b>corruption anchors</b>. Destroy every anchor to break the shield — only then can you purify the totem.", 6f);

        StartCoroutine(SpawnRaidAnchorsRoutine(_preGateHpMult, _preGateDmgMult));
    }

    private void SpawnAnchor(Vector3 pos, float hp)
    {
        GameObject go = anchorPrefab != null ? Instantiate(anchorPrefab, pos, Quaternion.identity)
                                             : new GameObject("CorruptionAnchor");
        if (anchorPrefab == null) go.transform.position = pos;
        CorruptionAnchor anchor = go.GetComponent<CorruptionAnchor>();
        if (anchor == null) anchor = go.AddComponent<CorruptionAnchor>();
        anchor.Setup(hp);
        anchor.onDestroyed += OnAnchorDestroyed;
    }

    private void OnAnchorDestroyed(CorruptionAnchor a)
    {
        _anchorsRemaining = Mathf.Max(0, _anchorsRemaining - 1);
        UpdateAnchorObjective();
        PlayCorruptionFlare();
        CameraShakeUtil.TryShake(0.25f, 0.1f);

        // Pre-gate: the last anchor down unlocks the main totem.
        if (_preGateActive && _anchorsRemaining <= 0)
        {
            _preGateActive = false;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Region_AnchorDestroy);
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr("THE SHIELD IS DOWN — PURIFY THE TOTEM"));
        }

        // Free the ally the moment the first anchor falls.
        if (!_allyFreed && allyPrefab != null)
        {
            _allyFreed = true;
            Vector3 p = (player != null ? player.position : transform.position) + Vector3.forward * 2f;
            p.y = GetGroundHeight(p);
            Instantiate(allyPrefab, p, Quaternion.identity);
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("AN ALLY JOINS THE FIGHT!"));
        }
    }

    private void UpdateAnchorObjective()
    {
        if (GlobalHUD.Instance == null) return;
        int destroyed = _anchorsTotal - _anchorsRemaining;
        GlobalHUD.Instance.SetLevelObjective(LocalizationManager.Tr("DESTROY THE CORRUPTION ANCHORS") + "  " + destroyed + "/" + _anchorsTotal);
    }

    // A telegraphed reinforcement wave from a random compass edge.
    private IEnumerator ReinforcementWave(GameObject[] pool, int count, float hpMult, float dmgMult)
    {
        if (pool == null || pool.Length == 0 || count <= 0) yield break;

        float ang = Random.Range(0f, 360f);
        Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
        Vector3 edge = transform.position + dir * (anchorRadius + 6f);
        edge.y = GetGroundHeight(edge);

        // Telegraph the incoming direction.
        GameObject marker = new GameObject("ReinforceMarker");
        marker.transform.position = edge + Vector3.up * 1f;
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(marker.transform, 1.4f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, edge);
        yield return new WaitForSeconds(1.3f);

        for (int i = 0; i < count; i++)
        {
            Vector3 p = edge + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(1.5f, 4f));
            p.y = GetGroundHeight(p);
            SpawnEntityAt(pool[Random.Range(0, pool.Length)], p, hpMult, dmgMult);
            yield return new WaitForSeconds(0.12f);
        }
        if (marker != null) Destroy(marker);
    }

    private IEnumerator MonitorCombatRoutine()
    {
        while (true)
        {
            activeEnemies.RemoveAll(item => item == null);

            if (activeEnemies.Count == 0)
            {
                yield return new WaitForSeconds(3f);
                LocalPurify();
                break;
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private void LocalPurify()
    {
        isPurified = true;

        if (activationShieldVFX != null) activationShieldVFX.Stop();
        if (idleCorruptionVFX != null) idleCorruptionVFX.Stop();
        if (interactHintVFX != null) interactHintVFX.SetActive(false);

        if (skyBeamVFX != null) { skyBeamVFX.gameObject.SetActive(true); skyBeamVFX.Play(); }
        if (totemLight != null) { totemLight.color = new Color(0f, 0.8f, 1f); totemLight.intensity *= 3f; }
        CameraShakeUtil.TryShake(0.4f, 0.1f);

        // Purification stinger — the region is cleansed.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Region_PurifyComplete);

        // The world visibly HEALS around the captured totem: a purification burst
        // sweeps out, the corrupt fog lifts to a lighter/warmer tone, and the
        // battle music releases back to calm.
        if (healWorldOnCapture) StartCoroutine(WorldHealRoutine());

        if (manager != null)
        {
            manager.OnTotemPurified(this);
        }
        else
        {
            // Standalone roadside totem — no RegionManager to hand out the
            // region reward, so grant the side-objective payout directly.
            EnemySpawner.IsSpawningBlocked = false; // manager normally clears this
            AnyActivatingRightNow = false;
            AchievementSystem.Unlock("ALTAR_HUNTER");
            if (ResourceManager.Instance != null && standaloneDiamondReward > 0)
                ResourceManager.Instance.AddDiamonds(standaloneDiamondReward);
            var pc = player != null ? player.GetComponent<PlayerController>() : null;
            if (pc != null && standaloneXpReward > 0) pc.GainXP(standaloneXpReward);
        }
    }

    private IEnumerator WorldHealRoutine()
    {
        if (purifyBurstVFX != null) Instantiate(purifyBurstVFX, transform.position + Vector3.up * 1f, Quaternion.identity);

        // Release the battle music so the score eases back to calm.
        if (AudioManager.Instance != null) AudioManager.Instance.UnduckMusicInstance(1.2f);

        // Lift the corrupt fog: ease colour toward the healed tone and thin the
        // density out. Captured from whatever the region currently has.
        Color fromColor = RenderSettings.fogColor;
        float fromDensity = RenderSettings.fogDensity;
        float toDensity = fromDensity * healedFogDensityMult;
        float t = 0f, dur = 3.5f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / dur);
            RenderSettings.fogColor = Color.Lerp(fromColor, healedFogColor, k);
            RenderSettings.fogDensity = Mathf.Lerp(fromDensity, toDensity, k);
            yield return null;
        }
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain", "Ground"))) return hit.point.y;
        if (Terrain.activeTerrain != null) return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}