using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance;

    [Header("UI Setup")]
    public GameObject missionUIPrefab;
    public Transform missionUIParent;

    public class ActiveMission
    {
        public MissionData data;
        public int currentProgress;
        public int targetAmount;
        public MissionUIElement uiElement;
        public bool isCompleted;
    }

    private List<ActiveMission> activeMissions = new List<ActiveMission>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        LoadMissions();

        // --- Բ��: ���� �� ��������� ��� ����� � ����� ������ � ����� ---
        if (SceneManager.GetActiveScene().name == "CampScene")
        {
            ClearCompletedMissionsUI();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "CampScene")
        {
            ClearCompletedMissionsUI();
        }
    }

    // ==== A GOAL THE WORLD CAN NO LONGER SERVE ====
    //
    // Runs in the camp, before the sweep below, and only for BuildStructures —
    // the one mission type whose supply is finite. A save has thirty upgrades in
    // it and not one more.
    //
    // Older saves are already carrying missions that ask for five upgrades,
    // because the board rounded every build target up to five regardless of what
    // the mission was authored for. If the camp cannot serve what is left of the
    // goal, that mission never completes, holds one of the three active slots
    // forever, and the board will not restock while three are held — so a single
    // bad mission takes the whole system down and there is no way to abandon it.
    //
    // Clamped down to what the camp can still do. If nothing at all remains, it
    // is counted as met: the player did the work the mission was pointing at,
    // and a permanent blocker is far worse than a reward paid a little early.
    private void ReconcileBuildMissions()
    {
        int left = CampBuilding.RemainingUpgrades;

        foreach (ActiveMission mission in activeMissions)
        {
            if (mission.isCompleted) continue;
            if (mission.data == null || mission.data.missionType != MissionType.BuildStructures) continue;

            int stillNeeded = mission.targetAmount - mission.currentProgress;
            if (stillNeeded <= left) continue;      // reachable, leave it alone

            mission.targetAmount = mission.currentProgress + Mathf.Max(0, left);

            if (mission.uiElement != null)
                mission.uiElement.UpdateProgress(mission.currentProgress, mission.targetAmount);

            if (mission.currentProgress >= mission.targetAmount) CompleteMission(mission);
        }
    }

    private void ClearCompletedMissionsUI()
    {
        ReconcileBuildMissions();

        for (int i = activeMissions.Count - 1; i >= 0; i--)
        {
            if (!(activeMissions[i].isCompleted || activeMissions[i].currentProgress >= activeMissions[i].targetAmount))
                continue;

            // Don't consume a completed mission until we can actually PAY it. If
            // the ResourceManager isn't up yet (a scene-init race), leaving it in
            // the list retries next camp load — the old code deleted + persisted
            // the deletion here, silently swallowing the reward.
            if (ResourceManager.Instance == null) continue;

            ResourceManager.Instance.AddStashResources(
                activeMissions[i].data.woodReward,
                activeMissions[i].data.stoneReward,
                activeMissions[i].data.foodReward
            );
            // AddDiamonds (not a raw `diamonds +=`) so the reward is SAVED
            // immediately and the toast/achievement fire. The raw increment
            // only lived in memory + the HUD and was lost if the player quit
            // before the next resource event triggered a stash save.
            ResourceManager.Instance.AddDiamonds(activeMissions[i].data.diamondReward);
            ResourceManager.Instance.UpdateUI();

            if (activeMissions[i].uiElement != null)
            {
                Destroy(activeMissions[i].uiElement.gameObject);
            }
            activeMissions.RemoveAt(i);
        }
        SaveMissions();
    }

    public void SaveMissions()
    {
        PlayerPrefs.SetInt("ActiveMissionCount", activeMissions.Count);

        for (int i = 0; i < activeMissions.Count; i++)
        {
            var m = activeMissions[i];
            PlayerPrefs.SetString("MissionName_" + i, m.data.missionName);
            PlayerPrefs.SetString("MissionDesc_" + i, m.data.missionDescription);
            PlayerPrefs.SetString("MissionType_" + i, m.data.missionType.ToString());
            PlayerPrefs.SetInt("MissionWood_" + i, m.data.woodReward);
            PlayerPrefs.SetInt("MissionStone_" + i, m.data.stoneReward);
            PlayerPrefs.SetInt("MissionFood_" + i, m.data.foodReward);
            PlayerPrefs.SetInt("MissionDiamond_" + i, m.data.diamondReward);
            PlayerPrefs.SetInt("MissionTarget_" + i, m.targetAmount);
            PlayerPrefs.SetInt("MissionProgress_" + i, m.currentProgress);
            PlayerPrefs.SetInt("MissionCompleted_" + i, m.isCompleted ? 1 : 0);
        }
        PlayerPrefs.Save();
    }

    private void LoadMissions()
    {
        int count = PlayerPrefs.GetInt("ActiveMissionCount", 0);

        for (int i = 0; i < count; i++)
        {
            MissionData loadedData = ScriptableObject.CreateInstance<MissionData>();
            loadedData.missionName = PlayerPrefs.GetString("MissionName_" + i);
            loadedData.missionDescription = PlayerPrefs.GetString("MissionDesc_" + i);

            string typeStr = PlayerPrefs.GetString("MissionType_" + i);
            if (System.Enum.TryParse(typeStr, out MissionType parsedType))
                loadedData.missionType = parsedType;

            loadedData.woodReward = PlayerPrefs.GetInt("MissionWood_" + i);
            loadedData.stoneReward = PlayerPrefs.GetInt("MissionStone_" + i);
            loadedData.foodReward = PlayerPrefs.GetInt("MissionFood_" + i);
            loadedData.diamondReward = PlayerPrefs.GetInt("MissionDiamond_" + i);

            int target = PlayerPrefs.GetInt("MissionTarget_" + i);
            int progress = PlayerPrefs.GetInt("MissionProgress_" + i);
            bool isDone = PlayerPrefs.GetInt("MissionCompleted_" + i, 0) == 1;

            ActiveMission newMission = new ActiveMission
            {
                data = loadedData,
                currentProgress = progress,
                targetAmount = target,
                isCompleted = isDone || progress >= target
            };

            activeMissions.Add(newMission);
            CreateUIForMission(newMission);
        }
    }

    // Max simultaneously-active missions. The Notice Board sizes its paper
    // count to (cap - active) so this is normally never hit; it's a guard so a
    // future flow / race can't overflow the 3-slot active-mission HUD.
    public const int MaxActiveMissions = 3;

    public void AddNewMission(MissionData data, int targetAmount)
    {
        if (activeMissions.Count >= MaxActiveMissions) return;

        ActiveMission newMission = new ActiveMission
        {
            data = data,
            currentProgress = 0,
            targetAmount = targetAmount,
            isCompleted = false
        };

        activeMissions.Add(newMission);
        CreateUIForMission(newMission);
        SaveMissions();
    }

    private void CreateUIForMission(ActiveMission mission)
    {
        if (missionUIPrefab == null || missionUIParent == null) return;

        // worldPositionStays: FALSE, and it matters.
        //
        // Instantiate(prefab, parent) defaults that flag to TRUE, which tells
        // Unity to keep the prefab's WORLD position and back-solve a local one
        // to match. For a 3D prop that is usually what you want. For UI it
        // throws the layout away: the plate's anchoredPosition is recomputed
        // from wherever the prefab root happened to sit, so the margin authored
        // into missionUIParent is discarded and the card lands flush against the
        // screen edge.
        //
        // With false the plate keeps the offsets it was authored with and simply
        // adopts the container — which is the whole reason the container exists.
        GameObject uiObj = Instantiate(missionUIPrefab, missionUIParent, false);
        mission.uiElement = uiObj.GetComponent<MissionUIElement>();
        // HUD widget shows the WHAT-TO-DO short label (e.g. "Defeat enemies"),
        // not the flavor description — the player already knows the
        // mission name from the title.
        string shortObjective = MissionData.BuildObjectiveShort(mission.data.missionType);
        mission.uiElement.Setup(LocalizationManager.Tr(mission.data.missionName), shortObjective, mission.currentProgress, mission.targetAmount);

        if (mission.isCompleted)
        {
            mission.uiElement.SetCompletedStateInstant();
        }
    }

    public void AddProgress(MissionType type, int amount = 1)
    {
        bool wasUpdated = false;
        foreach (ActiveMission mission in activeMissions)
        {
            if (!mission.isCompleted && mission.data.missionType == type)
            {
                mission.currentProgress += amount;
                if (mission.currentProgress > mission.targetAmount) mission.currentProgress = mission.targetAmount;

                if (mission.uiElement != null) mission.uiElement.UpdateProgress(mission.currentProgress, mission.targetAmount);

                if (mission.currentProgress >= mission.targetAmount) CompleteMission(mission);
                wasUpdated = true;
            }
        }

        if (wasUpdated) SaveMissions();
    }

    public int GetActiveMissionCount()
    {
        int count = 0;
        foreach (var mission in activeMissions)
        {
            if (!mission.isCompleted) count++;
        }
        return count;
    }

    private void CompleteMission(ActiveMission mission)
    {
        mission.isCompleted = true;
        if (mission.uiElement != null) mission.uiElement.CompleteMission();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);
        RunSession.AddMission();
        SaveMissions();
    }
}