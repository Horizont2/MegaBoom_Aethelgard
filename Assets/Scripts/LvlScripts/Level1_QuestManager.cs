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

            UpdateObjectiveUI();
        }
        else UpdateObjectiveUI();
    }

    public void StartIntroDialogue() { if (isDialogueStarted) return; isDialogueStarted = true; StartCoroutine(IntroDialogueRoutine()); }

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
        if (Terrain.activeTerrain != null)
        {
            float terrainHeight = Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
            return new Vector3(pos.x, terrainHeight, pos.z);
        }
        return pos;
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
        // children in a small formation. The scene prefab had children at
        // baked local offsets so a plain SetActive(true) left them floating
        // above the terrain or off-screen — that's why "the squad doesn't
        // spawn at all". We now place each enemy directly on terrain in a
        // 2-row arc facing the player.
        Vector3 spawnPos = GetTerrainPos(playerTransform.position - playerTransform.forward * spawnDistanceBehind);
        skeletonsWave1.transform.position = spawnPos;
        skeletonsWave1.transform.LookAt(new Vector3(playerTransform.position.x, spawnPos.y, playerTransform.position.z));
        skeletonsWave1.SetActive(true);

        LayoutEnemyFormation(skeletonsWave1.transform, playerTransform.position);

        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null) ai.isCinematicFrozen = true;
        }

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

    // Places every EnemyAI child in a fan-shaped formation around the parent
    // group facing toward the player. Terrain-samples each slot so nobody
    // floats. Called after SetActive to override the prefab's baked local
    // positions (which were the reason wave 1 sometimes didn't visibly spawn).
    private void LayoutEnemyFormation(Transform groupParent, Vector3 lookAtTarget)
    {
        List<EnemyAI> enemies = new List<EnemyAI>();
        foreach (EnemyAI e in groupParent.GetComponentsInChildren<EnemyAI>(true))
            if (e != null) enemies.Add(e);
        if (enemies.Count == 0) return;

        Vector3 groupCenter = groupParent.position;
        Vector3 forward = (new Vector3(lookAtTarget.x, groupCenter.y, lookAtTarget.z) - groupCenter).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = groupParent.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // Fan enemies across a 6m front, two rows deep, offset so they're
        // visible and don't all pile on the same spot.
        int count = enemies.Count;
        float lateralSpread = 6f;
        float rowDepth = 2.2f;
        for (int i = 0; i < count; i++)
        {
            float tx = count == 1 ? 0f : Mathf.Lerp(-1f, 1f, (float)i / (count - 1));
            int row = i % 2;
            Vector3 slot = groupCenter + right * (tx * lateralSpread * 0.5f) - forward * (row * rowDepth);
            slot = GetTerrainPos(slot);

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

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Good job! Wait... do you hear that?", 1.5f));

        Vector3 hordePos = GetTerrainPos(playerTransform.position + playerTransform.right * spawnDistanceBehind);

        skeletonsHordeWave2.transform.position = hordePos;
        skeletonsHordeWave2.transform.LookAt(new Vector3(playerTransform.position.x, hordePos.y, playerTransform.position.z));
        skeletonsHordeWave2.SetActive(true);

        LayoutEnemyFormation(skeletonsHordeWave2.transform, playerTransform.position);

        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null)
            {
                ai.MakeInvincibleAndFurious();
                ai.isCinematicFrozen = true;
            }
        }

        TriggerGroupRise(skeletonsHordeWave2.transform, 2.5f);
        yield return StartCoroutine(DroneCameraFlyAndTrack(hordePos, 3f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!", 2f, 9));

        if (evacuationHorse != null)
        {
            evacuationHorse.SetActive(true);
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

            // Terrain clearance floor AND ceiling — the previous version only
            // floored the camera, so if a stale interpolant sent it high it
            // stayed high. 20m above ground is plenty for any framing.
            float g = SampleGroundY(camPos);
            camPos.y = Mathf.Clamp(camPos.y, g + 1.5f, g + 20f);

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
        foreach (Transform child in groupParent)
        {
            if (child.GetComponent<EnemyAI>() != null)
            {
                StartCoroutine(RiseSingleAnim(child, duration));
            }
        }
    }

    private IEnumerator RiseSingleAnim(Transform enemy, float duration)
    {
        Vector3 finalPos = enemy.position;
        if (Terrain.activeTerrain != null)
        {
            finalPos.y = Terrain.activeTerrain.SampleHeight(finalPos) + Terrain.activeTerrain.transform.position.y;
        }

        enemy.position = finalPos - new Vector3(0, 2.5f, 0);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t);

            enemy.position = Vector3.Lerp(finalPos - new Vector3(0, 2.5f, 0), finalPos, t);
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