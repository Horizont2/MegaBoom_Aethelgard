using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Playables;
using Unity.Cinemachine;

public class Level1_QuestManager : MonoBehaviour
{
    public static Level1_QuestManager Instance;

    [Header("Cinematic & UI")]
    public PlayableDirector introDirector;
    public MissionUIElement objectiveUI;
    public TextMeshProUGUI subtitleText;

    [Header("Quest Settings")]
    public int requiredWood = 15;

    [Header("Enemies (Wave 1 & 2)")]
    public GameObject skeletonsWave1;
    public GameObject skeletonsHordeWave2;
    public GameObject evacuationHorse;

    [Header("Tutorial Trail")]
    [Tooltip("Animated dashed trail that leads the player toward the current tutorial objective (Stranger, then horse).")]
    public TutorialTrail tutorialTrail;
    [Tooltip("Stranger NPC transform — the first target of the tutorial trail.")]
    public Transform strangerTransform;

    [Header("Cinematic Settings")]
    public float spawnDistanceBehind = 15f;
    public float typingSpeed = 0.04f;

    private int currentQuestStep = 0;
    private int startingWood = 0;
    private bool isAmbushTriggered = false;
    private Transform playerTransform;

    private int totalSkeletonsW1 = 0;
    private int defeatedSkeletonsW1 = 0;
    private bool isDialogueStarted = false;

    private void Awake()
    {
        Instance = this;

        // Block player controls immediately so they can't move during the frame
        // before LevelStartRoutine runs. Without this, WASD could slip through
        // between Player.Update and our coroutine on frame 0.
        if (introDirector != null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                PlayerController pc = pObj.GetComponent<PlayerController>();
                if (pc != null) pc.isControlBlocked = true;
            }

            // Hide the HUD immediately — SetGameplayPanelsActive from inside
            // LevelStartRoutine runs a frame later, and the HP/stamina bars
            // were briefly visible during the very first frame of the intro
            // cutscene. Hide the objective tile too so the placeholder from
            // last run doesn't flash on screen.
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);
            if (objectiveUI != null) objectiveUI.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        if (objectiveUI != null)
        {
            CanvasGroup cg = objectiveUI.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 0f;
            objectiveUI.animateAppearance = true;
        }

        if (subtitleText != null) { subtitleText.text = ""; subtitleText.maxVisibleCharacters = 99999; }

        if (skeletonsWave1 != null) { totalSkeletonsW1 = skeletonsWave1.transform.childCount; skeletonsWave1.SetActive(false); }
        if (skeletonsHordeWave2 != null) skeletonsHordeWave2.SetActive(false);
        if (evacuationHorse != null) evacuationHorse.SetActive(false);

        Invoke("FindPlayer", 0.1f);

        if (introDirector != null)
        {
            SetCinematicMode(true);
            var brain = Camera.main.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = true;
            introDirector.Play();
        }

        StartCoroutine(LevelStartRoutine());
    }

    private void FindPlayer() { GameObject p = GameObject.FindGameObjectWithTag("Player"); if (p != null) playerTransform = p.transform; }

    private IEnumerator LevelStartRoutine()
    {
        if (introDirector != null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) pObj.GetComponent<PlayerController>().isControlBlocked = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

            yield return null;
            while (introDirector.state == PlayState.Playing) yield return null;

            // Disable CinemachineBrain FIRST. Otherwise brain keeps writing the
            // final vcam position to Camera.main in LateUpdate (brain runs after
            // CameraFollow), which overwrites the handoff blend and leaves the
            // camera frozen at the intro's overview shot.
            var brain = Camera.main.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;

            CameraFollow cf = Camera.main.GetComponent<CameraFollow>();
            if (cf != null)
            {
                Vector3 currentRot = Camera.main.transform.eulerAngles;
                float pitchX = currentRot.x;
                if (pitchX > 180f) pitchX -= 360f;
                cf.SyncRotation(currentRot.y, pitchX);

                // Smoothly blend from wherever Cinemachine left the camera into
                // CameraFollow's natural orbit. Without this the camera snapped
                // on frame 0 of gameplay and felt jarring.
                cf.BeginHandoffBlend(0.7f);
            }

            SetCinematicMode(false);

            // Keep player controls locked until the camera finishes blending
            // back to CameraFollow. Otherwise the player can already run around
            // while the camera is mid-transition and it feels broken.
            if (cf != null)
            {
                while (cf.IsHandoffBlending) yield return null;
            }

            if (pObj != null) pObj.GetComponent<PlayerController>().isControlBlocked = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);
            // Bring the objective tile back — Awake hid it so its default
            // "New Mission" state wouldn't flash during the intro.
            if (objectiveUI != null && !objectiveUI.gameObject.activeSelf) objectiveUI.gameObject.SetActive(true);

            // Point the animated dashed trail at the Stranger so the first
            // thing the player sees after the cutscene is a clear golden
            // path toward their objective.
            if (tutorialTrail != null && strangerTransform != null)
                tutorialTrail.SetTarget(strangerTransform);

            UpdateObjectiveUI();
        }
        else UpdateObjectiveUI();
    }

    public void StartIntroDialogue()
    {
        if (isDialogueStarted) return;
        isDialogueStarted = true;
        // Player reached the Stranger — hide the "walk here" trail so it
        // doesn't distract during the dialogue and the tree-chopping phase.
        // The trail comes back on for the escape phase.
        if (tutorialTrail != null) tutorialTrail.Hide();
        StartCoroutine(IntroDialogueRoutine());
    }

    private void SetCinematicMode(bool isCinematic)
    {
        CameraFollow cf = Camera.main.GetComponent<CameraFollow>();
        if (cf != null) cf.isCinematicMode = isCinematic;
        CameraCollision cc = Camera.main.GetComponent<CameraCollision>();
        if (cc != null) cc.isCinematicMode = isCinematic;
    }

    private IEnumerator IntroDialogueRoutine()
    {
        // dialogueId maps to AudioManager.dialogue6..10 FMOD slots (1-5
        // are owned by the camp tutorial in CampDirector).
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Thank the heavens you're here! My cart is busted and this forest is cursed.", 2.5f, 6));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: I need wood to fix the wheels. Gather 12 pieces, or we're not getting out of here alive!", 3f, 7));

        AdvanceQuest();
        StartCoroutine(ShowTutorialHint("[TIP] Walk up to a tree and press Left Mouse Button to attack and gather wood.", 5f));
    }

    public void AdvanceQuest()
    {
        if (objectiveUI != null) objectiveUI.CompleteMission();
        currentQuestStep++;

        if (currentQuestStep == 1 && ResourceManager.Instance != null) startingWood = ResourceManager.Instance.runWood;
        else if (currentQuestStep == 2) StartCoroutine(TriggerAmbushWave1Routine());
        else if (currentQuestStep == 3) StartCoroutine(TriggerHordeAndFleeRoutine());

        StartCoroutine(DelayedUIUpdateRoutine());
    }

    private IEnumerator DelayedUIUpdateRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        UpdateObjectiveUI();

        if (currentQuestStep == 1 && ResourceManager.Instance != null && objectiveUI != null)
        {
            int gatheredWood = ResourceManager.Instance.runWood - startingWood;
            objectiveUI.UpdateProgress(gatheredWood, requiredWood);
        }
        else if (currentQuestStep == 2 && objectiveUI != null)
        {
            objectiveUI.UpdateProgress(defeatedSkeletonsW1, totalSkeletonsW1);
        }
    }

    private void Update()
    {
        // --- �����: ������� в��� ---
        if (Input.GetKeyDown(KeyCode.F8))
        {
            DebugSkipToEscape();
        }

        if (ResourceManager.Instance != null)
        {
            int maxWoodAllowed = startingWood + 20;
            if (ResourceManager.Instance.runWood > maxWoodAllowed) ResourceManager.Instance.runWood = maxWoodAllowed;
            if (ResourceManager.Instance.runStone > 0) ResourceManager.Instance.runStone = 0;
            if (ResourceManager.Instance.runFood > 0) ResourceManager.Instance.runFood = 0;
        }

        if (currentQuestStep == 1 && ResourceManager.Instance != null && objectiveUI != null)
        {
            int gatheredWood = ResourceManager.Instance.runWood - startingWood;
            objectiveUI.UpdateProgress(gatheredWood, requiredWood);
            if (gatheredWood >= requiredWood && !isAmbushTriggered) AdvanceQuest();
        }
    }

    // ����� ����� ������
    private void DebugSkipToEscape()
    {
        StopAllCoroutines(); // ��������� �� ������ � ���-�����
        if (introDirector != null) introDirector.Stop();

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = false;
        SetCinematicMode(false);

        if (playerTransform != null)
        {
            PlayerController pController = playerTransform.GetComponent<PlayerController>();
            if (pController != null) pController.isControlBlocked = false;
        }

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        // ������ ������
        if (skeletonsWave1 != null) skeletonsWave1.SetActive(false);
        if (skeletonsHordeWave2 != null) skeletonsHordeWave2.SetActive(false);

        // ������� ����
        if (evacuationHorse != null)
        {
            evacuationHorse.SetActive(true);

            // ����������� ������ �� ����
            if (playerTransform != null)
            {
                CharacterController cc = playerTransform.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                Vector3 newPos = evacuationHorse.transform.position + evacuationHorse.transform.right * 3f;
                newPos = GetTerrainPos(newPos);
                playerTransform.position = newPos;

                if (cc != null) cc.enabled = true;
            }
        }

        currentQuestStep = 3;
        UpdateObjectiveUI();
        if (subtitleText != null) subtitleText.text = "<color=#FFFF00>[DEBUG] Skipped to Escape Phase</color>";
    }

    private Vector3 GetTerrainPos(Vector3 pos)
    {
        return new Vector3(pos.x, FindGroundY(pos), pos.z);
    }

    // Finds the actual visual ground Y at a world XZ. Uses a raycast from
    // 500m above straight down so it works with Unity Terrain OR mesh
    // terrain OR any other MeshCollider surface — Terrain.SampleHeight
    // silently returns 0 when the scene has no Terrain.activeTerrain,
    // which was placing tutorial enemies at whatever elevated Y the
    // player happened to be at.
    private float FindGroundY(Vector3 pos)
    {
        Vector3 origin = new Vector3(pos.x, pos.y + 500f, pos.z);
        // ~0 = every layer; ignore triggers so region trigger volumes and
        // extraction-portal sensors don't count as ground.
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        // Fall back to Terrain if the raycast missed absolutely everything.
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;

        return pos.y;
    }

    private IEnumerator TriggerAmbushWave1Routine()
    {
        isAmbushTriggered = true;

        // Guard against missing player — was a hidden source of NullRef that
        // silently killed the coroutine, leaving the wave unspawned.
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
            if (playerTransform == null) yield break;
        }

        PlayerController pController = playerTransform.GetComponent<PlayerController>();
        if (pController != null) pController.isControlBlocked = true;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        // Anchor the group on flat ground behind the player, then LAY OUT the
        // children in a small formation. Precise order matters:
        //  1) Freeze children FIRST (public field write works on inactive
        //     components) so EnemyAI.SpawnRoutine takes the "just wait" branch
        //     when Start fires.
        //  2) SetActive(true) — Awake/Start run, SpawnRoutine is a no-op
        //     because we already froze it.
        //  3) THEN LayoutEnemyFormation. Setting transform.position on the
        //     children of an INACTIVE parent doesn't persist reliably —
        //     Unity recomputes world position from stale local coords when
        //     the parent activates. That's why the wave came back "high in
        //     the air" even after freezing.
        Vector3 spawnPos = GetTerrainPos(playerTransform.position - playerTransform.forward * spawnDistanceBehind);
        skeletonsWave1.transform.position = spawnPos;
        skeletonsWave1.transform.LookAt(new Vector3(playerTransform.position.x, spawnPos.y, playerTransform.position.z));

        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null) ai.isCinematicFrozen = true;
        }

        skeletonsWave1.SetActive(true);

        // Now that the hierarchy is active, place every enemy on the ground.
        LayoutEnemyFormation(skeletonsWave1.transform, playerTransform.position);
        Physics.SyncTransforms();

        Coroutine cameraFly = StartCoroutine(DroneCameraFlyAndTrack(spawnPos, 3.5f));

        TriggerGroupRise(skeletonsWave1.transform, 2.5f);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Watch out! They're crawling from the dirt!", 2f, 8));
        yield return cameraFly;

        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>())
        {
            if (ai != null) { ai.ForceStop(); ai.isCinematicFrozen = false; }
        }

        // Short breather before the player is thrown into combat — otherwise
        // the ambush cutscene ends and enemies immediately swarm before the
        // control-lock UI even fades out.
        yield return new WaitForSeconds(0.4f);

        if (pController != null) pController.isControlBlocked = false;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        StartCoroutine(ShowTutorialHint("[TIP] Enemies are attacking! Use Left Mouse Button to fight back and watch your health.", 5f));
    }

    // Places every EnemyAI child in a fan-shaped formation on the ACTUAL
    // physical ground under each slot. Uses FindGroundY (a raycast) rather
    // than Terrain.SampleHeight because the tutorial scene doesn't have a
    // Unity Terrain component set as activeTerrain — SampleHeight was
    // returning 0/player.y which is why enemies kept floating.
    private void LayoutEnemyFormation(Transform groupParent, Vector3 lookAtTarget)
    {
        List<EnemyAI> enemies = new List<EnemyAI>();
        foreach (EnemyAI e in groupParent.GetComponentsInChildren<EnemyAI>(true))
            if (e != null) enemies.Add(e);
        if (enemies.Count == 0) return;

        // Base plane = ground directly under the player. Player is standing
        // on something solid by definition, so this is the safest reference.
        Vector3 playerPos = playerTransform != null ? playerTransform.position : lookAtTarget;
        float playerGroundY = FindGroundY(playerPos);

        Vector3 groupCenter = groupParent.position;
        groupCenter.y = playerGroundY;
        groupParent.position = groupCenter;

        Vector3 forward = (new Vector3(lookAtTarget.x, playerGroundY, lookAtTarget.z) - groupCenter).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = groupParent.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        int count = enemies.Count;
        float lateralSpread = 6f;
        float rowDepth = 2.2f;
        for (int i = 0; i < count; i++)
        {
            float tx = count == 1 ? 0f : Mathf.Lerp(-1f, 1f, (float)i / (count - 1));
            int row = i % 2;
            Vector3 slot = groupCenter + right * (tx * lateralSpread * 0.5f) - forward * (row * rowDepth);

            // Raycast for the actual ground at this slot's XZ. If it fails
            // (slot is over a hole / off-mesh), pin to the player's ground
            // plane so we never end up floating.
            float slotGround = FindGroundY(slot);
            slot.y = Mathf.Abs(slotGround - playerGroundY) < 8f ? slotGround : playerGroundY;

            Transform et = enemies[i].transform;
            et.position = slot;
            et.rotation = Quaternion.LookRotation(forward);
        }
    }

    public void EnemyDefeated()
    {
        if (currentQuestStep == 2)
        {
            defeatedSkeletonsW1++;
            if (objectiveUI != null) objectiveUI.UpdateProgress(defeatedSkeletonsW1, totalSkeletonsW1);
            if (defeatedSkeletonsW1 >= totalSkeletonsW1) AdvanceQuest();
        }
    }

    private IEnumerator TriggerHordeAndFleeRoutine()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
            if (playerTransform == null) yield break;
        }

        PlayerController pController = playerTransform.GetComponent<PlayerController>();
        if (pController != null) pController.isControlBlocked = true;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);
        // Explicitly hide the tutorial's mission tile and any auxiliary HUD
        // groups so the escape cutscene reads clean. SetGameplayPanelsActive
        // only touches the panels registered on GlobalHUD; scene-authored
        // pieces like the objective tile need to be hidden by hand.
        if (objectiveUI != null) objectiveUI.gameObject.SetActive(false);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Good job! Wait... do you hear that?", 1.5f));

        Vector3 hordePos = GetTerrainPos(playerTransform.position + playerTransform.right * spawnDistanceBehind);

        skeletonsHordeWave2.transform.position = hordePos;
        skeletonsHordeWave2.transform.LookAt(new Vector3(playerTransform.position.x, hordePos.y, playerTransform.position.z));

        // Freeze BEFORE SetActive so SpawnRoutine's first-frame position-cache
        // takes the else branch. LayoutEnemyFormation runs AFTER SetActive
        // (see wave 1 comment) because setting positions on inactive children
        // doesn't persist through the activation transform-recompute.
        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null)
            {
                ai.MakeInvincibleAndFurious();
                ai.isCinematicFrozen = true;
            }
        }

        skeletonsHordeWave2.SetActive(true);

        LayoutEnemyFormation(skeletonsHordeWave2.transform, playerTransform.position);
        Physics.SyncTransforms();

        TriggerGroupRise(skeletonsHordeWave2.transform, 2.5f);
        yield return StartCoroutine(DroneCameraFlyAndTrack(hordePos, 3f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!", 2f, 9));

        if (evacuationHorse != null)
        {
            evacuationHorse.SetActive(true);
            // Guarantee the horse is on the ground and visible before the
            // camera flies to it. Belt & braces: some scene-authored horses
            // ended up at Y=0 world (below terrain) OR with a disabled child
            // renderer, which is what made the escape shot look empty.
            evacuationHorse.transform.position = GetTerrainPos(evacuationHorse.transform.position);
            foreach (Renderer r in evacuationHorse.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;
            Physics.SyncTransforms();

            yield return StartCoroutine(DroneCameraFlyAndTrack(evacuationHorse.transform.position, 2.5f));
        }

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: RUN TO THE HORSE, NOW!!", 2f, 10));

        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>())
        {
            if (ai != null) { ai.ForceStop(); ai.isCinematicFrozen = false; }
        }

        yield return new WaitForSeconds(0.4f);

        if (pController != null) pController.isControlBlocked = false;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);
        if (objectiveUI != null && !objectiveUI.gameObject.activeSelf) objectiveUI.gameObject.SetActive(true);

        // Show the golden dashed trail toward the horse so the player has an
        // unmistakable visual guide while sprinting away from the invincible
        // horde.
        if (tutorialTrail != null && evacuationHorse != null)
            tutorialTrail.SetTarget(evacuationHorse.transform);

        StartCoroutine(ShowTutorialHint("[TIP] You can't kill them! Hold SHIFT to sprint and reach the Extraction Point!", 6f));
    }

    private IEnumerator DroneCameraFlyAndTrack(Vector3 targetPosition, float flyDuration)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) yield break;

        SetCinematicMode(true);

        Vector3 startPos = mainCam.transform.position;
        Quaternion startRot = mainCam.transform.rotation;

        // Compute the final framing: 8m back and 5m above the target, based
        // on the direction from the player to the enemy group. This keeps the
        // squad centred in frame no matter which direction they spawned in.
        Vector3 toTarget = new Vector3(targetPosition.x - startPos.x, 0, targetPosition.z - startPos.z);
        Vector3 approachDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward;
        Vector3 endPos = targetPosition - approachDir * 8f + Vector3.up * 5f;

        // Clamp the end height so it stays near enemy eye-level rather than
        // shooting into orbit if the terrain sample fails.
        float endGround = SampleGroundY(endPos);
        endPos.y = Mathf.Min(endPos.y, endGround + 6f);
        endPos.y = Mathf.Max(endPos.y, endGround + 3f);

        // Midpoint arc: halfway between start and end, +2m up. The old code
        // added (15, 5, 0) unconditionally which threw the arc off-screen —
        // now the arc height scales with the horizontal distance so long
        // flights get a gentle rise, short ones stay tight.
        float dist = Vector3.Distance(startPos, endPos);
        float arcHeight = Mathf.Clamp(dist * 0.15f, 1f, 5f);
        Vector3 midPos = Vector3.Lerp(startPos, endPos, 0.5f) + Vector3.up * arcHeight;

        float elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / flyDuration);

            // Quadratic Bezier via De Casteljau (start -> mid -> end).
            Vector3 m1 = Vector3.Lerp(startPos, midPos, t);
            Vector3 m2 = Vector3.Lerp(midPos, endPos, t);
            Vector3 camPos = Vector3.Lerp(m1, m2, t);

            // Terrain clearance floor AND ceiling — tight bounds. 10m above
            // ground is enough for an ambush framing; any higher and the
            // enemies look like ants. If the target itself is above ground
            // (broken spawn placement) the camera stays low and the audience
            // sees empty sky above rather than a helicopter shot.
            float g = SampleGroundY(camPos);
            camPos.y = Mathf.Clamp(camPos.y, g + 1.5f, g + 10f);

            mainCam.transform.position = camPos;

            Vector3 lookDir = targetPosition - camPos;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion cinematicRot = Quaternion.LookRotation(lookDir);
                mainCam.transform.rotation = Quaternion.Slerp(startRot, cinematicRot, t);
            }

            yield return null;
        }

        CameraFollow cf = mainCam.GetComponent<CameraFollow>();
        if (cf != null)
        {
            Vector3 currentRot = mainCam.transform.eulerAngles;
            float pitchX = currentRot.x;
            if (pitchX > 180f) pitchX -= 360f;
            cf.SyncRotation(currentRot.y, pitchX);
            // Smooth-blend back to the follow rig instead of snapping — makes
            // the transition after each cinematic feel like one continuous
            // camera instead of a cut.
            cf.BeginHandoffBlend(0.5f);
        }
        SetCinematicMode(false);
    }

    private float SampleGroundY(Vector3 worldPos)
    {
        if (Terrain.activeTerrain == null) return worldPos.y;
        return Terrain.activeTerrain.SampleHeight(worldPos) + Terrain.activeTerrain.transform.position.y;
    }

    private void TriggerGroupRise(Transform groupParent, float duration)
    {
        // GetComponentsInChildren so enemies nested inside sub-groups still
        // get the rise animation. The old foreach over direct children silently
        // skipped anyone one level deeper.
        foreach (EnemyAI ai in groupParent.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null) StartCoroutine(RiseSingleAnim(ai.transform, duration));
        }
    }

    private IEnumerator RiseSingleAnim(Transform enemy, float duration)
    {
        // finalPos = wherever LayoutEnemyFormation just placed this enemy.
        // DELIBERATELY do NOT re-sample the terrain here — if the enemy's
        // XZ happens to lie over a mountain slope, SampleHeight returns
        // that slope's Y and the enemy rises out of a mountain instead of
        // out of the flat ground next to the player.
        Vector3 finalPos = enemy.position;
        Vector3 startPos = finalPos - new Vector3(0, 2.5f, 0);
        enemy.position = startPos;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t);

            enemy.position = Vector3.Lerp(startPos, finalPos, t);
            yield return null;
        }
        enemy.position = finalPos;
    }

    private IEnumerator ShowSubtitleTypewriter(string text, float duration, int dialogueId = 0)
    {
        if (subtitleText != null)
        {
            text = LocalizationManager.Tr(text);
            // Duck the score during the line so the reader isn't
            // competing with battle music. Released once the subtitle
            // clears below.
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.DuckMusic(0.35f, 0.25f, -1f, 0f);
                if (dialogueId > 0) AudioManager.Instance.PlayDialogue(dialogueId);
            }
            subtitleText.text = text;
            subtitleText.ForceMeshUpdate();
            int totalChars = subtitleText.textInfo.characterCount;
            subtitleText.maxVisibleCharacters = 0;

            for (int i = 0; i <= totalChars; i++)
            {
                subtitleText.maxVisibleCharacters = i;
                yield return new WaitForSeconds(typingSpeed);
            }

            yield return new WaitForSeconds(duration);
            subtitleText.text = "";
            if (AudioManager.Instance != null) AudioManager.Instance.UnduckMusic(0.5f);
        }
    }

    private IEnumerator ShowTutorialHint(string text, float duration)
    {
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr(text));
            yield return new WaitForSeconds(duration);
            GlobalHUD.Instance.HidePrompt();
        }
    }

    private void UpdateObjectiveUI()
    {
        if (objectiveUI == null) return;
        switch (currentQuestStep)
        {
            case 0: objectiveUI.Setup(LocalizationManager.Tr("Main Quest"), LocalizationManager.Tr("Investigate the Outpost"), 0, 1); break;
            case 1: objectiveUI.Setup(LocalizationManager.Tr("Stranger's Request"), LocalizationManager.Tr("Gather Wood"), 0, requiredWood); break;
            case 2: objectiveUI.Setup(LocalizationManager.Tr("Ambush!"), LocalizationManager.Tr("Survive the Skeletons"), 0, totalSkeletonsW1); break;
            case 3: objectiveUI.Setup(LocalizationManager.Tr("Escape!"), LocalizationManager.Tr("REACH THE HORSE BEFORE THEY KILL YOU!"), 0, 1); break;
        }
    }

    public void TriggerGameOver()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.UI_GameOver);
        StartCoroutine(TutorialGameOverRoutine());
    }

    private IEnumerator TutorialGameOverRoutine()
    {
        if (subtitleText != null)
        {
            subtitleText.maxVisibleCharacters = 99999;
            subtitleText.text = LocalizationManager.Tr("YOU HAVE FALLEN...");
        }

        yield return new WaitForSeconds(2.5f);
        if (subtitleText != null) subtitleText.text = "";

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Lvl_1");
        else UnityEngine.SceneManagement.SceneManager.LoadScene("Lvl_1");
    }
}