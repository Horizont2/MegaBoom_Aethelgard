using UnityEngine;
using TMPro;
using System.Collections;
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
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Thank the heavens you're here! My cart is busted and this forest is cursed.", 2.5f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: I need wood to fix the wheels. Gather 12 pieces, or we're not getting out of here alive!", 3f));

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

        PlayerController pController = playerTransform.GetComponent<PlayerController>();
        if (pController != null) pController.isControlBlocked = true;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        Vector3 spawnPos = playerTransform.position - (playerTransform.forward * spawnDistanceBehind);
        spawnPos = GetTerrainPos(spawnPos);

        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null) ai.isCinematicFrozen = true;
        }

        skeletonsWave1.transform.position = spawnPos;
        skeletonsWave1.transform.LookAt(playerTransform);
        skeletonsWave1.SetActive(true);

        Coroutine cameraFly = StartCoroutine(DroneCameraFlyAndTrack(spawnPos, 3.5f));

        TriggerGroupRise(skeletonsWave1.transform, 2.5f);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Watch out! They're crawling from the dirt!", 2f));
        yield return cameraFly;

        foreach (EnemyAI ai in skeletonsWave1.GetComponentsInChildren<EnemyAI>())
        {
            if (ai != null) ai.ForceStop();
        }

        if (pController != null) pController.isControlBlocked = false;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        StartCoroutine(ShowTutorialHint("[TIP] Enemies are attacking! Use Left Mouse Button to fight back and watch your health.", 5f));
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
        PlayerController pController = playerTransform.GetComponent<PlayerController>();
        if (pController != null) pController.isControlBlocked = true;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Good job! Wait... do you hear that?", 1.5f));

        Vector3 hordePos = playerTransform.position + (playerTransform.right * spawnDistanceBehind);
        hordePos = GetTerrainPos(hordePos);

        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>(true))
        {
            if (ai != null)
            {
                ai.MakeInvincibleAndFurious();
                ai.isCinematicFrozen = true;
            }
        }

        skeletonsHordeWave2.transform.position = hordePos;
        skeletonsHordeWave2.transform.LookAt(playerTransform);
        skeletonsHordeWave2.SetActive(true);

        TriggerGroupRise(skeletonsHordeWave2.transform, 2.5f);
        yield return StartCoroutine(DroneCameraFlyAndTrack(hordePos, 3f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!", 2f));

        if (evacuationHorse != null)
        {
            evacuationHorse.SetActive(true);
            yield return StartCoroutine(DroneCameraFlyAndTrack(evacuationHorse.transform.position, 2.5f));
        }

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: RUN TO THE HORSE, NOW!!", 2f));

        foreach (EnemyAI ai in skeletonsHordeWave2.GetComponentsInChildren<EnemyAI>())
        {
            if (ai != null) ai.ForceStop();
        }

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

        Vector3 midPos = Vector3.Lerp(startPos, targetPosition + new Vector3(0, 8f, -10f), 0.5f);
        midPos += new Vector3(15f, 5f, 0f);

        Vector3 endPos = targetPosition + new Vector3(8f, 6f, -8f);

        float elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / flyDuration);

            Vector3 m1 = Vector3.Lerp(startPos, midPos, t);
            Vector3 m2 = Vector3.Lerp(midPos, endPos, t);
            Vector3 camPos = Vector3.Lerp(m1, m2, t);

            if (Terrain.activeTerrain != null)
            {
                float minHeight = Terrain.activeTerrain.SampleHeight(camPos) + Terrain.activeTerrain.transform.position.y + 1.5f;
                if (camPos.y < minHeight) camPos.y = minHeight;
            }

            mainCam.transform.position = camPos;

            Vector3 lookDir = targetPosition - mainCam.transform.position;
            if (lookDir != Vector3.zero)
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
        }
        SetCinematicMode(false);
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

    private IEnumerator ShowSubtitleTypewriter(string text, float duration)
    {
        if (subtitleText != null)
        {
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
        }
    }

    private IEnumerator ShowTutorialHint(string text, float duration)
    {
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(text);
            yield return new WaitForSeconds(duration);
            GlobalHUD.Instance.HidePrompt();
        }
    }

    private void UpdateObjectiveUI()
    {
        if (objectiveUI == null) return;
        switch (currentQuestStep)
        {
            case 0: objectiveUI.Setup("Main Quest", "Investigate the Outpost", 0, 1); break;
            case 1: objectiveUI.Setup("Stranger's Request", "Gather Wood", 0, requiredWood); break;
            case 2: objectiveUI.Setup("Ambush!", "Survive the Skeletons", 0, totalSkeletonsW1); break;
            case 3: objectiveUI.Setup("Escape!", "REACH THE HORSE BEFORE THEY KILL YOU!", 0, 1); break;
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
            subtitleText.text = "<color=#8B0000>YOU HAVE FALLEN...</color>";
        }

        yield return new WaitForSeconds(2.5f);
        if (subtitleText != null) subtitleText.text = "";

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Lvl_1");
        else UnityEngine.SceneManagement.SceneManager.LoadScene("Lvl_1");
    }
}