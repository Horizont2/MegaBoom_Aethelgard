using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class NoticeBoardManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject boardCanvas;
    public GameObject interactionRune;
    public Transform paperLayoutGroup;
    public GameObject missionPaperPrefab;
    public TextMeshProUGUI emptyBoardMessage;
    public Button embarkButton;

    [Header("Scene Transition")]
    public string worldSceneName = "WorldScene";

    [Header("Mission Database")]
    public MissionData[] baseMissions;

    [Header("Restock System")]
    public int maxMissionsOnBoard = 3;
    public float restockTimeMinutes = 5f;

    private List<GameObject> activePapers = new List<GameObject>();

    // What the marker over the board needs to know: is there anything here to
    // take right now? Papers are destroyed when the board is emptied and added
    // when it restocks, so the list itself is the honest answer — no second
    // piece of state to fall out of sync with it.
    public static NoticeBoardManager Instance { get; private set; }

    // ==== A CLOSED BOARD IS NOT AN EMPTY BOARD ====
    //
    // This asked each paper for activeInHierarchy, which folds in every parent
    // — and the papers live under boardCanvas, which Start deactivates on the
    // line BEFORE it generates them. So from the moment the scene loaded until
    // the player opened the board, every paper reported inactive and this said
    // the board had nothing on it. The marker above the board is driven from
    // here, which is why it only ever appeared once you were already standing
    // at the board with it open — the one moment you do not need telling.
    //
    // CountAcceptablePapers asks the question properly: the paper's own
    // activeSelf, and an accept button that has not been used yet.
    public bool HasMissionsToTake => CountAcceptablePapers() > 0;

    // Has the board restocked since the player last looked at it? A notice
    // board's mark is a promise about NEW work, so it has to come back when new
    // work arrives rather than being spent forever on the first visit.
    public static bool HasUnseenRestock
    {
        get
        {
            string last = PlayerPrefs.GetString("LastMissionRestockTime", "");
            if (string.IsNullOrEmpty(last)) return true;   // never stocked = never seen
            return PlayerPrefs.GetString("MissionBoard_SeenRestock", "") != last;
        }
    }

    private static void MarkRestockSeen()
    {
        PlayerPrefs.SetString("MissionBoard_SeenRestock",
                              PlayerPrefs.GetString("LastMissionRestockTime", ""));
        PlayerPrefs.Save();
    }
    private bool isPlayerNear = false;
    public bool isBoardOpen = false;

    // Static flag that CameraFollow and GlobalHUD's ESC handler check so
    // camera mouse-look freezes while the board is open, and ESC closes the
    // board instead of triggering the pause menu.
    public static bool IsAnyBoardOpen { get; private set; }

    // Saved cursor state so we can restore whatever mode was active before
    // OpenBoard() forced Cursor.visible = true.
    private CursorLockMode savedCursorLock;
    private bool savedCursorVisible;
    private bool cursorStateSaved;

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        if (embarkButton != null) embarkButton.onClick.AddListener(EmbarkOnJourney);
        boardCanvas.SetActive(false);

        // "����" �������� �� ��� ����� ����� (��� ������ UI), 
        // ��� ������ ����, �� � �� ����� ������
        CheckAndGenerateMissions();

        // ��������� ���� ����
        UpdateRuneVisibility();
    }

    private void Update()
    {
        if (isPlayerNear && Input.GetKeyDown(KeyCode.E))
        {
            if (!isBoardOpen) OpenBoard();
            else CloseBoard();
        }
        // ESC closes the board — must run before GlobalHUD's own Escape
        // handler fires the pause menu. GlobalHUD swallows ESC when
        // IsAnyBoardOpen is true.
        if (isBoardOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseBoard();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_OPEN_BOARD"));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
            if (isBoardOpen) CloseBoard();
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    public void OpenBoard()
    {
        isBoardOpen = true;
        IsAnyBoardOpen = true;
        boardCanvas.SetActive(true);

        if (PlayerPrefs.GetInt("HasInteractedWithBoard", 0) == 0)
        {
            PlayerPrefs.SetInt("HasInteractedWithBoard", 1);
            PlayerPrefs.Save();
        }

        if (interactionRune != null) interactionRune.SetActive(false);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        CheckAndGenerateMissions();

        // After the restock, not before: opening the board means the player has
        // seen whatever is on it NOW, and CheckAndGenerateMissions is where a
        // new batch gets its timestamp.
        MarkRestockSeen();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();

        // Snapshot cursor state before forcing it visible so Close can restore
        // whatever mode gameplay was using.
        if (!cursorStateSaved)
        {
            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            cursorStateSaved = true;
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void CloseBoard()
    {
        isBoardOpen = false;
        IsAnyBoardOpen = false;
        boardCanvas.SetActive(false);

        UpdateRuneVisibility();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        // Always lock cursor back to gameplay mode on close.
        cursorStateSaved = false;
        StartCoroutine(LockCursorRoutine());
    }

    private System.Collections.IEnumerator LockCursorRoutine()
    {
        yield return new WaitForEndOfFrame();
        // Don't steal the cursor from a modal that opened during the
        // end-of-frame wait (settings, confirm dialog, etc.). Modals
        // legitimately own the cursor state while they're up.
        if (ConfirmDialog.IsOpen) yield break;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    // ����� �����: ���� �������� ����
    public void UpdateRuneVisibility()
    {
        if (interactionRune == null) return;

        bool isFirstTime = PlayerPrefs.GetInt("HasInteractedWithBoard", 0) == 0;
        // Papers are NOT removed when accepted — MissionPaperUI only disables the
        // accept button — so childCount stays above zero forever and the rune
        // hung over the board with nothing left to take. What matters is whether
        // anything is still ACCEPTABLE.
        bool hasMissions = CountAcceptablePapers() > 0;

        // ���� ������� Ҳ���� ����:
        // 1. �� ����� ��� (������� �� �� �������� � ������)
        // ��� 2. �� ����� � ��
        // � ��� ����� ����� ����� �������.
        interactionRune.SetActive((isFirstTime || hasMissions) && !isBoardOpen);
    }

    private int CountAcceptablePapers()
    {
        if (paperLayoutGroup == null) return 0;
        int n = 0;
        foreach (Transform child in paperLayoutGroup)
        {
            if (child == null || !child.gameObject.activeSelf) continue;
            var paper = child.GetComponent<MissionPaperUI>();
            // No component: treat it as a real, takeable paper rather than
            // silently ignoring it.
            if (paper == null) { n++; continue; }
            if (paper.acceptButton == null || paper.acceptButton.interactable) n++;
        }
        return n;
    }

    private void CheckAndGenerateMissions()
    {
        bool isFirstTime = PlayerPrefs.GetInt("HasInteractedWithBoard", 0) == 0;
        string lastRestockStr = PlayerPrefs.GetString("LastMissionRestockTime", "");

        // ���� �� ����� ��� � �����Ӫ�� ������ � ����������� ��������� ��
        bool needsRestock = string.IsNullOrEmpty(lastRestockStr) || isFirstTime;

        if (!needsRestock)
        {
            // UTC binary timestamp (culture/DST-invariant) instead of the old
            // DateTime.Now.ToString()/TryParse, which could fail to parse across
            // a locale change. A parse failure just forces a restock (safe).
            if (long.TryParse(lastRestockStr, out long lastBin))
            {
                DateTime lastRestock = DateTime.FromBinary(lastBin);
                if ((DateTime.UtcNow - lastRestock).TotalMinutes >= restockTimeMinutes)
                    needsRestock = true;
            }
            else
            {
                needsRestock = true;
            }
        }

        int currentActiveMissions = 0;
        if (MissionManager.Instance != null)
        {
            currentActiveMissions = MissionManager.Instance.GetActiveMissionCount();
        }

        // This label alternates between two different state messages. Exclude
        // it from the AutoLocalize walkers so the 0.5s scene repeater can't
        // capture whichever message showed first as a loc key and keep forcing
        // it back over the other one every half-second (the mission-status text
        // "changing every second").
        if (emptyBoardMessage != null && emptyBoardMessage.GetComponent<NoAutoLocalize>() == null)
            emptyBoardMessage.gameObject.AddComponent<NoAutoLocalize>();

        if (currentActiveMissions >= MissionManager.MaxActiveMissions)
        {
            needsRestock = false;

            foreach (Transform child in paperLayoutGroup) Destroy(child.gameObject);
            activePapers.Clear();

            if (emptyBoardMessage != null)
            {
                emptyBoardMessage.text = LocalizationManager.Tr("You already have 3 active missions.\nComplete them first!");
            }
        }
        else
        {
            if (emptyBoardMessage != null)
            {
                emptyBoardMessage.text = LocalizationManager.Tr("No new missions available right now.\nCheck back later.");
            }
        }

        if (needsRestock)
        {
            GenerateNewMissions(MissionManager.MaxActiveMissions - currentActiveMissions);
            PlayerPrefs.SetString("LastMissionRestockTime", DateTime.UtcNow.ToBinary().ToString());
            PlayerPrefs.Save();
        }

        UpdateEmptyMessage();
    }

    private void GenerateNewMissions(int maxAllowed)
    {
        foreach (Transform child in paperLayoutGroup) Destroy(child.gameObject);
        activePapers.Clear();

        int maxToSpawn = Mathf.Min(maxMissionsOnBoard, maxAllowed);
        if (maxToSpawn <= 0) return;

        int missionsToSpawn = UnityEngine.Random.Range(1, maxToSpawn + 1);

        // Scale mission rewards/goals with actual progression. This used to
        // read the Meta* perk levels, but those were always 0 (no purchase
        // path — the dead system was removed), so missions never scaled.
        // Conquered-region count is a clean 0..24 progress proxy: reward grows
        // faster than the goal so later missions stay worth doing.
        int progress = Mathf.Clamp(PlayerPrefs.GetInt("TotalConqueredRegions", 0), 0, 24);

        float rewardMultiplier = 1f + (progress * 0.06f);
        float goalMultiplier = 1f + (progress * 0.02f);

        List<MissionData> availableMissions = new List<MissionData>(baseMissions);

        for (int i = 0; i < missionsToSpawn; i++)
        {
            if (availableMissions.Count == 0) break;

            int randomIndex = UnityEngine.Random.Range(0, availableMissions.Count);
            MissionData baseMission = availableMissions[randomIndex];
            availableMissions.RemoveAt(randomIndex);

            MissionData scaledMission = ScriptableObject.Instantiate(baseMission);

            int rawTarget = Mathf.RoundToInt(scaledMission.targetAmount * goalMultiplier);
            float payMultiplier = rewardMultiplier;

            if (scaledMission.missionType == MissionType.BuildStructures)
            {
                // ==== NOT EVERY GOAL IS A COUNT OF FIFTY-SOMETHING ====
                //
                // This ran every target through RoundToNearestFive and then
                // clamped it to a floor of 5. That is right for kills, crystals
                // and seconds. It is ruinous for buildings, because
                // RoundToNearestFive(1) and RoundToNearestFive(2) are both ZERO,
                // and zero is then clamped UP to five.
                //
                // So all three authored build missions — Camp Expansion at 1,
                // Engineering Mastery at 2, Master Architect at 3 — arrived on
                // the board asking for the same five upgrades, while keeping
                // their own rewards. Camp Expansion paid 170 for five upgrades
                // and Master Architect paid 635 for the identical job.
                //
                // Build goals are small integers by design. They stay that way.
                int want = Mathf.Clamp(rawTarget, 1, 6);

                // And the camp can only be built so far. Thirty upgrades exist
                // in a save; past that a build mission is one that can never be
                // completed, and it would sit in an active slot forever and stop
                // the board restocking. Offer what the camp can still serve, or
                // nothing.
                int left = CampBuilding.RemainingUpgrades;
                if (left <= 0)
                {
                    i--;            // try a different mission in this slot
                    continue;
                }
                scaledMission.targetAmount = Mathf.Min(want, left);

                // Build costs climb steeply with level — a first upgrade is
                // under a hundred resources, a fifth is near eight hundred —
                // while rewardMultiplier tracks conquered regions, which says
                // nothing about how expensive the next upgrade is. A camp that
                // is nearly finished is asking for far more than a new one, so
                // the pay follows the camp as well.
                payMultiplier *= 1f + CampBuilding.UpgradesCompleted * 0.1f;
            }
            else
            {
                scaledMission.targetAmount = Mathf.Clamp(RoundToNearestFive(rawTarget), 5, 400);
            }

            scaledMission.woodReward = RoundToNearestFive(scaledMission.woodReward * payMultiplier);
            scaledMission.stoneReward = RoundToNearestFive(scaledMission.stoneReward * payMultiplier);
            scaledMission.foodReward = RoundToNearestFive(scaledMission.foodReward * payMultiplier);
            scaledMission.diamondReward = RoundToNearestFive(scaledMission.diamondReward * payMultiplier);

            GameObject paperObj = Instantiate(missionPaperPrefab, paperLayoutGroup);
            MissionPaperUI paperUI = paperObj.GetComponent<MissionPaperUI>();

            paperUI.SetupPaper(scaledMission, 1f);

            // NOTE: the accept SFX is played inside MissionPaperUI.AcceptMission —
            // playing it here too double-fired the sound on every accept. This
            // listener only refreshes the empty-board message.
            paperUI.acceptButton.onClick.AddListener(UpdateEmptyMessage);

            activePapers.Add(paperObj);
        }
    }

    private int RoundToNearestFive(int value)
    {
        if (value <= 0) return 0;
        return Mathf.RoundToInt(value / 5f) * 5;
    }

    private int RoundToNearestFive(float value)
    {
        if (value <= 0) return 0;
        return Mathf.RoundToInt(value / 5f) * 5;
    }

    public void UpdateEmptyMessage()
    {
        StartCoroutine(CheckEmptyRoutine());
    }

    private System.Collections.IEnumerator CheckEmptyRoutine()
    {
        yield return new WaitForEndOfFrame();
        int paperCount = CountAcceptablePapers();
        if (emptyBoardMessage != null) emptyBoardMessage.gameObject.SetActive(paperCount == 0);

        // ��������� ���� ������, ���� ������� ������ ���
        if (!isBoardOpen) UpdateRuneVisibility();
    }

    private void EmbarkOnJourney()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.currentRegion = null;
            GameManager.Instance.isRegionMission = false;
        }

        // Also clear the lingering static region pointer. It's set when a region
        // is attacked and survives a lost run (it's a static, not tied to the
        // GameManager), so WorldGenerator would otherwise fall back to it and
        // regenerate the region location even though we asked for a normal run.
        MissionInitializer.PendingMissionRegion = null;

        PlayerPrefs.SetInt("IsRegionMission", 0);
        PlayerPrefs.SetInt("IsRunActive", 1);
        PlayerPrefs.SetInt("IsContinuing", 0);
        PlayerPrefs.Save();

        CloseBoard();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(worldSceneName);
        else SceneLoader.LoadScene(worldSceneName);
    }
}