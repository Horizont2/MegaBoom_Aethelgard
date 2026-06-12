using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FMODUnity;
using FMOD.Studio;

public static class AudioID
{
    public const string UI_Click = "UI_Click";
    public const string UI_Hover = "UI_Hover";
    public const string UI_QuestAccept = "UI_QuestAccept";
    public const string UI_QuestComplete = "UI_QuestComplete";
    public const string UI_Error = "UI_Error";
    public const string UI_LevelUp = "UI_LevelUp";
    public const string UI_Purchase = "UI_Purchase";

    public const string Player_Dash = "Player_Dash";
    public const string Player_Swing = "Player_Swing";
    public const string Player_HitEnemy = "Player_HitEnemy";
    public const string Player_HitResource = "Player_HitRes";
    public const string Player_Hurt = "Player_Hurt";
    public const string Player_Throw = "Player_Throw";
    public const string Player_Heal = "Player_Heal";
    public const string Player_Footstep = "Player_Footstep";
    public const string Explosion = "Explosion";

    public const string Enemy_Agro = "Enemy_Agro";
    public const string Enemy_Telegraph = "Enemy_Telegraph";
    public const string Enemy_Attack = "Enemy_Attack";
    public const string Enemy_Hurt = "Enemy_Hurt";
    public const string Enemy_Die = "Enemy_Die";
    public const string Enemy_Footstep = "Enemy_Footstep";

    public const string Camp_CollectItem = "Camp_CollectItem";
    public const string Camp_CollectGem = "Camp_CollectGem";
    public const string Camp_BuildStart = "Camp_BuildStart";
    public const string Camp_BuildDone = "Camp_BuildDone";
    public const string NPC_Work = "NPC_Work";
    public const string Env_Thunder = "Env_Thunder";
    public const string Env_ChestOpen = "Env_ChestOpen";

    public const string Animal_CatMeow = "Animal_CatMeow";
    public const string Animal_Chicken = "Animal_Chicken";

    public const string Music_Camp = "Music_Camp";
    public const string Music_Battle = "Music_Battle";
}

[System.Serializable]
public class SoundGroup
{
    public EventReference fmodEvent;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("=== UI SOUNDS ===")]
    public SoundGroup uiClick, uiHover, uiQuestAccept, uiQuestComplete, uiError, uiLevelUp, uiPurchase;

    [Header("=== PLAYER SOUNDS ===")]
    public SoundGroup playerDash, playerSwing, playerHitEnemy, playerHitResource, playerHurt, playerThrow, playerHeal, playerFootstep, explosion;

    [Header("=== ENEMY SOUNDS ===")]
    public SoundGroup enemyAgro, enemyTelegraph, enemyAttack, enemyHurt, enemyDie, enemyFootstep;

    [Header("=== ENVIRONMENT & CAMP ===")]
    public SoundGroup campCollectItem, campCollectGem, campBuildStart, campBuildDone, npcWork, envThunder, envChestOpen;

    [Header("=== ANIMALS ===")]
    public SoundGroup animalCatMeow, animalChicken;

    [Header("=== MUSIC ===")]
    public SoundGroup musicCamp, musicBattle;

    private Dictionary<string, SoundGroup> sfxDictionary;
    private EventInstance currentMusicInstance;
    private string currentMusicName;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeDictionaries();
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "CampScene") PlayMusic(AudioID.Music_Camp);
        else if (scene.name == "WorldScene") PlayMusic(AudioID.Music_Battle);
    }

    private void InitializeDictionaries()
    {
        sfxDictionary = new Dictionary<string, SoundGroup>();

        // UI
        sfxDictionary.Add(AudioID.UI_Click, uiClick); sfxDictionary.Add(AudioID.UI_Hover, uiHover); sfxDictionary.Add(AudioID.UI_QuestAccept, uiQuestAccept);
        sfxDictionary.Add(AudioID.UI_QuestComplete, uiQuestComplete); sfxDictionary.Add(AudioID.UI_Error, uiError); sfxDictionary.Add(AudioID.UI_LevelUp, uiLevelUp);
        sfxDictionary.Add(AudioID.UI_Purchase, uiPurchase);

        // Player
        sfxDictionary.Add(AudioID.Player_Dash, playerDash); sfxDictionary.Add(AudioID.Player_Swing, playerSwing); sfxDictionary.Add(AudioID.Player_HitEnemy, playerHitEnemy);
        sfxDictionary.Add(AudioID.Player_HitResource, playerHitResource); sfxDictionary.Add(AudioID.Player_Hurt, playerHurt); sfxDictionary.Add(AudioID.Player_Throw, playerThrow);
        sfxDictionary.Add(AudioID.Player_Heal, playerHeal); sfxDictionary.Add(AudioID.Player_Footstep, playerFootstep); sfxDictionary.Add(AudioID.Explosion, explosion);

        // Enemy
        sfxDictionary.Add(AudioID.Enemy_Agro, enemyAgro); sfxDictionary.Add(AudioID.Enemy_Telegraph, enemyTelegraph); sfxDictionary.Add(AudioID.Enemy_Attack, enemyAttack);
        sfxDictionary.Add(AudioID.Enemy_Hurt, enemyHurt); sfxDictionary.Add(AudioID.Enemy_Die, enemyDie); sfxDictionary.Add(AudioID.Enemy_Footstep, enemyFootstep);

        // Env & Camp
        sfxDictionary.Add(AudioID.Camp_CollectItem, campCollectItem); sfxDictionary.Add(AudioID.Camp_CollectGem, campCollectGem);
        sfxDictionary.Add(AudioID.Camp_BuildStart, campBuildStart); sfxDictionary.Add(AudioID.Camp_BuildDone, campBuildDone);
        sfxDictionary.Add(AudioID.NPC_Work, npcWork); sfxDictionary.Add(AudioID.Env_Thunder, envThunder); sfxDictionary.Add(AudioID.Env_ChestOpen, envChestOpen);

        // Animals
        sfxDictionary.Add(AudioID.Animal_CatMeow, animalCatMeow); sfxDictionary.Add(AudioID.Animal_Chicken, animalChicken);

        // Music
        sfxDictionary.Add(AudioID.Music_Camp, musicCamp); sfxDictionary.Add(AudioID.Music_Battle, musicBattle);
    }

    // Odpalanie efektów dŸwiêkowych 2D (interfejs)
    public void PlayUI(string soundName)
    {
        PlaySFX(soundName);
    }

    // Odpalanie efektów dŸwiêkowych w œwiecie gry
    public void PlaySFX(string soundName)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            // FMOD odpala dŸwiêk jednorazowo w locie! 
            // Losowoœæ pitchu/g³oœnoœci ustawiasz bezpoœrednio w programie FMOD Studio!
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
    }

    // Zarz¹dzanie muzyk¹ w tle
    public void PlayMusic(string soundName)
    {
        if (currentMusicName == soundName) return;

        // Jeœli leci jakaœ muzyka, zatrzymujemy j¹ z uwzglêdnieniem wygaszania (Fade Out zdefiniowanego w FMOD Studio)
        if (currentMusicInstance.isValid())
        {
            currentMusicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            currentMusicInstance.release();
        }

        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            currentMusicInstance = RuntimeManager.CreateInstance(group.fmodEvent);
            currentMusicInstance.start();
            currentMusicName = soundName;
        }
    }

    // Puste funkcje zachowane dla kompatybilnoœci kodu programisty (¿eby gra siê kompilowa³a)
    public void SetMasterVolume(float vol) { }
    public void SetMusicVolume(float vol) { }
    public void SetSFXVolume(float vol) { }
}