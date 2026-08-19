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

    [Header("New-game story cutscene (optional)")]
    [Tooltip("Illustrated opening cutscene shown once when a new game begins. " +
             "If assigned, it plays FIRST; the existing Timeline intro (camera " +
             "descent + handoff) only starts after the cutscene finishes. Leave " +
             "empty to keep the old behaviour exactly.")]
    public StoryIntroPlayer storyIntro;

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

    // Cached at Awake time so pause-scene camera switching (which retargets
    // Camera.main to the WaterfallPos camera) can't make us disable the
    // wrong CinemachineBrain / never clear isCinematicMode on the real
    // gameplay camera. Without this, pausing during the intro left the
    // gameplay CameraFollow stuck in cinematic mode after unpause and the
    // camera just froze in place while the player could still run around.
    private Camera cachedGameplayCam;
    private CameraFollow cachedGameplayFollow;
    private CameraCollision cachedGameplayCollision;
    private CinemachineBrain cachedGameplayBrain;

    private void Awake()
    {
        Instance = this;

        CameraFollow follow = FindFirstObjectByType<CameraFollow>(FindObjectsInactive.Include);
        if (follow != null)
        {
            cachedGameplayFollow = follow;
            cachedGameplayCam = follow.GetComponent<Camera>();
            if (cachedGameplayCam == null) cachedGameplayCam = follow.GetComponentInChildren<Camera>();
            cachedGameplayCollision = follow.GetComponent<CameraCollision>();
            cachedGameplayBrain = follow.GetComponent<CinemachineBrain>();
            if (cachedGameplayBrain == null && cachedGameplayCam != null)
                cachedGameplayBrain = cachedGameplayCam.GetComponent<CinemachineBrain>();
        }
        if (cachedGameplayCam == null) cachedGameplayCam = Camera.main;
        if (cachedGameplayFollow == null && cachedGameplayCam != null)
            cachedGameplayFollow = cachedGameplayCam.GetComponent<CameraFollow>();
        if (cachedGameplayCollision == null && cachedGameplayCam != null)
            cachedGameplayCollision = cachedGameplayCam.GetComponent<CameraCollision>();
        if (cachedGameplayBrain == null && cachedGameplayCam != null)
            cachedGameplayBrain = cachedGameplayCam.GetComponent<CinemachineBrain>();

        if (introDirector != null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                PlayerController pc = pObj.GetComponent<PlayerController>();
                if (pc != null) pc.isControlBlocked = true;
            }

            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);
            if (objectiveUI != null) objectiveUI.gameObject.SetActive(true);

            // ФІКС ТРЕЙЛУ: Тепер зі старту він веде до Незнайомця, а не до Коня!
            if (tutorialTrail != null && strangerTransform != null)
            {
                tutorialTrail.gameObject.SetActive(false);
                tutorialTrail.gameObject.SetActive(true);
                tutorialTrail.enabled = true;
                tutorialTrail.SetTarget(strangerTransform);
            }
            Canvas.ForceUpdateCanvases();

            // (Видалено помилковий спам підказки про коня зі старту гри)
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

        if (storyIntro != null)
        {
            // The illustrated new-game cutscene runs first. The existing
            // Timeline intro (camera descent) + its LocationTitleReveal overlay
            // and the gameplay handoff only begin once the cutscene finishes.
            // The player is already control-blocked from Awake, so nothing moves
            // while the slides play.
            StartCoroutine(DeferIntroUntilCutscene());
        }
        else
        {
            if (introDirector != null)
            {
                SetCinematicMode(true);
                if (cachedGameplayBrain != null) cachedGameplayBrain.enabled = true;
                introDirector.Play();
            }

            StartCoroutine(LevelStartRoutine());
        }
    }

    // Waits out the illustrated opening cutscene, then runs the normal Timeline
    // intro + camera handoff exactly as before. Inert if no cutscene is playing
    // (e.g. already seen this save) — it falls straight through.
    private IEnumerator DeferIntroUntilCutscene()
    {
        // Let every component's Start() run so StoryIntroPlayer.IsPlaying settles.
        yield return null;
        while (StoryIntroPlayer.IsPlaying) yield return null;

        if (introDirector != null)
        {
            SetCinematicMode(true);
            if (cachedGameplayBrain != null) cachedGameplayBrain.enabled = true;
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
            PlayerController pc = pObj != null ? pObj.GetComponent<PlayerController>() : null;
            if (pc != null) pc.isControlBlocked = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

            yield return null;
            while (introDirector.state == PlayState.Playing) yield return null;

            if (cachedGameplayBrain != null) cachedGameplayBrain.enabled = false;

            CameraFollow cf = cachedGameplayFollow;
            if (cf != null && cachedGameplayCam != null)
            {
                Vector3 currentRot = cachedGameplayCam.transform.eulerAngles;
                float pitchX = currentRot.x;
                if (pitchX > 180f) pitchX -= 360f;
                cf.SyncRotation(currentRot.y, pitchX);
                cf.BeginHandoffBlend(0.7f);
            }

            SetCinematicMode(false);

            if (cf != null)
            {
                while (cf.IsHandoffBlending) yield return null;
            }

            if (pc != null) pc.isControlBlocked = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

            // ФІКС UI: Прибрано Canvas.ForceUpdateCanvases() та маніпуляції з альфою, 
            // щоб не переривати рідну анімацію плашки!
            if (objectiveUI != null) objectiveUI.gameObject.SetActive(true);

            if (tutorialTrail != null && strangerTransform != null)
            {
                tutorialTrail.gameObject.SetActive(false);
                tutorialTrail.gameObject.SetActive(true);
                tutorialTrail.enabled = true;
                tutorialTrail.SetTarget(strangerTransform);
            }

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
        // Deliberately uses the cached gameplay CameraFollow / CameraCollision
        // instead of Camera.main.GetComponent<>(): when the pause scene camera
        // is active, Camera.main points at THAT camera, so isCinematicMode
        // never gets cleared on the real gameplay camera and it stays frozen
        // after unpause.
        if (cachedGameplayFollow != null) cachedGameplayFollow.isCinematicMode = isCinematic;
        if (cachedGameplayCollision != null) cachedGameplayCollision.isCinematicMode = isCinematic;
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

        if (cachedGameplayBrain != null) cachedGameplayBrain.enabled = false;
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
        float startY = playerTransform != null ? playerTransform.position.y + 50f : pos.y + 50f;
        Vector3 origin = new Vector3(pos.x, startY, pos.z);

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 150f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = -9999f;
        bool found = false;

        foreach (var hit in hits)
        {
            Collider col = hit.collider;
            if (col.isTrigger) continue;

            string n = col.name.ToLower();
            // ФІКС: Більше ніяких дерев! Приймаємо тільки справжню землю.
            if (n.Contains("terrain") || n.Contains("floor") || n.Contains("ground"))
            {
                if (hit.point.y > bestY)
                {
                    bestY = hit.point.y;
                    found = true;
                }
            }
        }

        // Якщо раптом під ворогами немає Terrain, безпечно ставимо їх на висоту гравця
        if (!found) return playerTransform != null ? playerTransform.position.y : pos.y;

        return bestY;
    }

    private IEnumerator TriggerAmbushWave1Routine()
    {
        isAmbushTriggered = true;

        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
            if (playerTransform == null) yield break;
        }

        PlayerController pController = playerTransform.GetComponent<PlayerController>();
        if (pController != null) pController.isControlBlocked = true;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        Vector3 spawnPos = GetTerrainPos(playerTransform.position - playerTransform.forward * spawnDistanceBehind);
        skeletonsWave1.transform.position = spawnPos;
        skeletonsWave1.transform.LookAt(new Vector3(playerTransform.position.x, spawnPos.y, playerTransform.position.z));

        // АБСОЛЮТНИЙ ЗАХИСТ ВІД ВИБУХІВ: Вимикаємо КОЛАЙДЕРИ та фізику ДО активації об'єкта
        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>(true))
            if (ai != null) ai.isCinematicFrozen = true;
        foreach (var agent in skeletonsWave1.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            if (agent != null) agent.enabled = false;
        foreach (var rb in skeletonsWave1.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) rb.isKinematic = true;
        foreach (var col in skeletonsWave1.GetComponentsInChildren<Collider>(true))
            if (col != null && !col.isTrigger) col.enabled = false;

        skeletonsWave1.SetActive(true);

        LayoutEnemyFormation(skeletonsWave1.transform, playerTransform.position);
        Physics.SyncTransforms();

        Coroutine cameraFly = StartCoroutine(DroneCameraFlyAndTrack(spawnPos, 3.5f));

        TriggerGroupRise(skeletonsWave1.transform, 2.5f);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Watch out! They're crawling from the dirt!", 2f, 8));
        yield return cameraFly;

        UnfreezeEnemiesSecurely(skeletonsWave1);

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

            float slotGround = FindGroundY(slot);
            slot.y = Mathf.Abs(slotGround - playerGroundY) < 8f ? slotGround : playerGroundY;

            // ФІКС ПОЛЬОТІВ: Повністю видалено NavMesh.SamplePosition. 
            // Вороги залишаться рівно на координатах землі, які ми вирахували!
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
        if (objectiveUI != null) objectiveUI.gameObject.SetActive(false);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Good job! Wait... do you hear that?", 1.5f));

        Vector3 hordePos = GetTerrainPos(playerTransform.position + playerTransform.right * spawnDistanceBehind);
        skeletonsHordeWave2.transform.position = hordePos;
        skeletonsHordeWave2.transform.LookAt(new Vector3(playerTransform.position.x, hordePos.y, playerTransform.position.z));

        // АБСОЛЮТНИЙ ЗАХИСТ ВІД ВИБУХІВ: Вимикаємо КОЛАЙДЕРИ та фізику ДО активації об'єкта
        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>(true))
            if (ai != null) ai.isCinematicFrozen = true;
        foreach (var agent in skeletonsHordeWave2.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            if (agent != null) agent.enabled = false;
        foreach (var rb in skeletonsHordeWave2.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) rb.isKinematic = true;
        foreach (var col in skeletonsHordeWave2.GetComponentsInChildren<Collider>(true))
            if (col != null && !col.isTrigger) col.enabled = false;

        skeletonsHordeWave2.SetActive(true);

        LayoutEnemyFormation(skeletonsHordeWave2.transform, playerTransform.position);
        Physics.SyncTransforms();

        TriggerGroupRise(skeletonsHordeWave2.transform, 2.5f);
        yield return StartCoroutine(DroneCameraFlyAndTrack(hordePos, 3f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!", 2f, 9));

        if (evacuationHorse != null)
        {
            evacuationHorse.SetActive(true);
            evacuationHorse.transform.position = GetTerrainPos(evacuationHorse.transform.position);
            foreach (Renderer r in evacuationHorse.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;
            Physics.SyncTransforms();

            yield return StartCoroutine(DroneCameraFlyAndTrack(evacuationHorse.transform.position, 2.5f));
        }

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: RUN TO THE HORSE, NOW!!", 2f, 10));

        UnfreezeEnemiesSecurely(skeletonsHordeWave2);

        yield return new WaitForSeconds(0.4f);

        if (pController != null) pController.isControlBlocked = false;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);
        if (objectiveUI != null && !objectiveUI.gameObject.activeSelf) objectiveUI.gameObject.SetActive(true);

        if (tutorialTrail != null && evacuationHorse != null)
            tutorialTrail.SetTarget(evacuationHorse.transform);

        StartCoroutine(ShowTutorialHint("[TIP] You can't kill them! Hold SHIFT to sprint and reach the Extraction Point!", 6f));
    }

    private IEnumerator DroneCameraFlyAndTrack(Vector3 targetPosition, float flyDuration)
    {
        Camera mainCam = cachedGameplayCam != null ? cachedGameplayCam : Camera.main;
        if (mainCam == null) yield break;

        SetCinematicMode(true);

        Vector3 startPos = mainCam.transform.position;
        Quaternion startRot = mainCam.transform.rotation;

        Vector3 toTarget = new Vector3(targetPosition.x - startPos.x, 0, targetPosition.z - startPos.z);
        Vector3 approachDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward;
        Vector3 endPos = targetPosition - approachDir * 8f + Vector3.up * 5f;

        // Перевіряємо землю ТІЛЬКИ для кінцевої точки, щоб камера не впала під карту
        float endGround = SampleGroundY(endPos);
        endPos.y = Mathf.Min(endPos.y, endGround + 6f);
        endPos.y = Mathf.Max(endPos.y, endGround + 3f);

        float dist = Vector3.Distance(startPos, endPos);
        float arcHeight = Mathf.Clamp(dist * 0.15f, 1f, 5f);
        Vector3 midPos = Vector3.Lerp(startPos, endPos, 0.5f) + Vector3.up * arcHeight;

        float elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / flyDuration);

            // Крива Безьє (старт -> центр -> кінець)
            Vector3 m1 = Vector3.Lerp(startPos, midPos, t);
            Vector3 m2 = Vector3.Lerp(midPos, endPos, t);
            Vector3 camPos = Vector3.Lerp(m1, m2, t);

            // ВИДАЛЕНО: жорстке вирівнювання (Clamp) по землі під час польоту.
            // Тепер камера просто плавно пролетить по ідеальній дузі без стрибків!

            mainCam.transform.position = camPos;

            Vector3 lookDir = targetPosition - camPos;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion cinematicRot = Quaternion.LookRotation(lookDir);
                mainCam.transform.rotation = Quaternion.Slerp(startRot, cinematicRot, t);
            }

            yield return null;
        }

        CameraFollow cf = cachedGameplayFollow;
        if (cf != null && mainCam != null)
        {
            Vector3 currentRot = mainCam.transform.eulerAngles;
            float pitchX = currentRot.x;
            if (pitchX > 180f) pitchX -= 360f;
            cf.SyncRotation(currentRot.y, pitchX);
            cf.BeginHandoffBlend(0.5f);
        }
        SetCinematicMode(false);
    }

    private float SampleGroundY(Vector3 worldPos)
    {
        // Більше ніяких старих багованих перевірок! 
        // Просто змушуємо камеру використовувати той самий ідеальний лазерний пошук, що й вороги.
        return FindGroundY(worldPos);
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
        // ФІКС ЗІШТОВХУВАННЯ: Вимикаємо CharacterController на час анімації, 
        // щоб він не вистрілював скелета з-під землі.
        CharacterController cc = enemy.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

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

        if (cc != null) cc.enabled = true;
    }

    private IEnumerator ShowSubtitleTypewriter(string text, float duration, int dialogueId = 0)
    {
        if (subtitleText == null) yield break;

        text = LocalizationManager.Tr(text);

        // Duck the score during the line so the reader isn't competing with
        // battle music. Released once the subtitle clears below.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.DuckMusic(0.35f, 0.25f, -1f, 0f);
            if (dialogueId > 0) AudioManager.Instance.PlayDialogue(dialogueId);
        }

        // Typewriter via an <alpha=#00> tag: the FULL line is always laid out
        // (so the layout never shifts / jumps as it types — the reveal happens
        // "in place"), but the not-yet-typed tail is transparent. This uses
        // plain `.text =` + ForceMeshUpdate, which renders reliably here, unlike
        // maxVisibleCharacters which left later lines un-rendered on this TMP.
        int token = SubtitleGuard.Claim();
        subtitleText.maxVisibleCharacters = 99999;
        for (int i = 0; i <= text.Length; i++)
        {
            if (!SubtitleGuard.Owns(token)) yield break;
            subtitleText.text = text.Substring(0, i) + "<alpha=#00>" + text.Substring(i);
            subtitleText.ForceMeshUpdate();
            yield return new WaitForSecondsRealtime(typingSpeed);
        }

        yield return new WaitForSecondsRealtime(duration);
        if (!SubtitleGuard.Owns(token)) yield break;

        subtitleText.text = string.Empty;
        subtitleText.ForceMeshUpdate();
        if (AudioManager.Instance != null) AudioManager.Instance.UnduckMusic(0.5f);
    }

    private IEnumerator ShowTutorialHint(string text, float duration)
    {
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr(text));
            yield return new WaitForSecondsRealtime(duration);
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
        else SceneLoader.LoadScene("Lvl_1");
    }

    private void UnfreezeEnemiesSecurely(GameObject group)
    {
        // 1. Вмикаємо звичайні колайдери
        foreach (var col in group.GetComponentsInChildren<Collider>(true))
        {
            if (col != null && !col.isTrigger) col.enabled = true;
        }

        foreach (EnemyAI ai in group.GetComponentsInChildren<EnemyAI>())
        {
            if (ai != null)
            {
                // 2. Більше жодних маніпуляцій з NavMeshAgent чи Rigidbody! 
                // Ми просто даємо скрипту EnemyAI зелене світло на рух, 
                // і він почне бігти до гравця за власною математикою.
                ai.ForceStop();
                ai.isCinematicFrozen = false;
            }
        }
    }
}