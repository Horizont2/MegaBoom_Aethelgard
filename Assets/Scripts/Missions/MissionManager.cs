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

        // --- ФІКС: Якщо ми запустили гру прямо зі сцени табору в едіторі ---
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

    private void ClearCompletedMissionsUI()
    {
        for (int i = activeMissions.Count - 1; i >= 0; i--)
        {
            if (activeMissions[i].isCompleted || activeMissions[i].currentProgress >= activeMissions[i].targetAmount)
            {
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.AddStashResources(
                        activeMissions[i].data.woodReward,
                        activeMissions[i].data.stoneReward,
                        activeMissions[i].data.foodReward
                    );
                    ResourceManager.Instance.diamonds += activeMissions[i].data.diamondReward;
                    ResourceManager.Instance.UpdateUI();
                }

                if (activeMissions[i].uiElement != null)
                {
                    Destroy(activeMissions[i].uiElement.gameObject);
                }
                activeMissions.RemoveAt(i);
            }
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

    public void AddNewMission(MissionData data, int targetAmount)
    {
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

        GameObject uiObj = Instantiate(missionUIPrefab, missionUIParent);
        mission.uiElement = uiObj.GetComponent<MissionUIElement>();
        mission.uiElement.Setup(mission.data.missionName, mission.data.missionDescription, mission.currentProgress, mission.targetAmount);

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
        SaveMissions();
    }
}