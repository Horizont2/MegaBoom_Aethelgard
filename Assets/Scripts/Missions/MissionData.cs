using UnityEngine;

public enum MissionType { KillEnemies, CollectCrystals, Survive, BuildStructures }

[CreateAssetMenu(fileName = "NewMission", menuName = "Megabonk/Mission Data")]
public class MissionData : ScriptableObject
{
    [Header("Mission Details")]
    public string missionName = "New Mission";
    [TextArea] public string missionDescription = "Mission description...";
    public MissionType missionType;

    [Header("Target Goal")]
    public int targetAmount = 50;

    [Header("Rewards (Hub Resources)")]
    public int woodReward = 20;
    public int stoneReward = 10; // ������ ������
    public int foodReward = 5;   // ������ ���
    public int diamondReward = 10;

    // Player-facing objective line — this is WHAT to do, not the
    // flavor. UI shows this in bold; the flavor `missionDescription`
    // becomes a secondary line under it.
    public static string BuildObjective(MissionType type, int targetAmount)
    {
        switch (type)
        {
            case MissionType.KillEnemies:
                return LocalizationManager.Tr("OBJECTIVE_KILL_ENEMIES", targetAmount);
            case MissionType.CollectCrystals:
                return LocalizationManager.Tr("OBJECTIVE_COLLECT_CRYSTALS", targetAmount);
            case MissionType.Survive:
                if (targetAmount >= 60)
                {
                    int minutes = targetAmount / 60;
                    int seconds = targetAmount % 60;
                    return seconds == 0
                        ? LocalizationManager.Tr("OBJECTIVE_SURVIVE_MINUTES", minutes)
                        : LocalizationManager.Tr("OBJECTIVE_SURVIVE_MIN_SEC", minutes, seconds);
                }
                return LocalizationManager.Tr("OBJECTIVE_SURVIVE_SECONDS", targetAmount);
            case MissionType.BuildStructures:
                return LocalizationManager.Tr("OBJECTIVE_BUILD_STRUCTURES", targetAmount);
            default:
                return LocalizationManager.Tr("MISSION_TARGET_LABEL", targetAmount);
        }
    }

    // Short version for the HUD widget — same words but no target.
    public static string BuildObjectiveShort(MissionType type)
    {
        switch (type)
        {
            case MissionType.KillEnemies:     return LocalizationManager.Tr("OBJECTIVE_SHORT_KILL");
            case MissionType.CollectCrystals: return LocalizationManager.Tr("OBJECTIVE_SHORT_COLLECT");
            case MissionType.Survive:         return LocalizationManager.Tr("OBJECTIVE_SHORT_SURVIVE");
            case MissionType.BuildStructures: return LocalizationManager.Tr("OBJECTIVE_SHORT_BUILD");
            default:                          return "";
        }
    }
}