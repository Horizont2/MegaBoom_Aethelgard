using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

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
    public AudioClip[] clips;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.5f, 2f)] public float pitch = 1f;
    public bool randomizePitch = false;
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

    [Header("=== SETTINGS ===")]
    public int sfxPoolSize = 15;
    public float musicFadeDuration = 1.5f;

    [HideInInspector] public float globalMusicVolume = 1f;
    [HideInInspector] public float globalSFXVolume = 1f;

    // Внутрішні компоненти (більше не треба налаштовувати в Інспекторі)
    private AudioSource musicSource;
    private AudioSource uiSource;
    private AudioSource[] sfxSources;

    private Dictionary<string, SoundGroup> sfxDictionary, uiDictionary, musicDictionary;
    private Coroutine musicFadeCoroutine;

    // Змінні для анти-спаму (захист від дублювання звуків)
    private float lastUIPlayTime;
    private string lastUIPlaySound;

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
        CreateAudioSources();
        LoadAudioSettings();
    }

    private void CreateAudioSources()
    {
        // Автоматично створюємо і ховаємо всі динаміки
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.hideFlags = HideFlags.HideInInspector;

        uiSource = gameObject.AddComponent<AudioSource>();
        uiSource.hideFlags = HideFlags.HideInInspector;

        sfxSources = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            sfxSources[i] = gameObject.AddComponent<AudioSource>();
            sfxSources[i].hideFlags = HideFlags.HideInInspector;
        }
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

    private void LoadAudioSettings()
    {
        // ЖОРСТКИЙ ЗАХИСТ: Mathf.Clamp не дасть гучності стати більшою за 1 (100%)
        globalMusicVolume = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_MusicVol", 1f), 0f, 1f);
        globalSFXVolume = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_SFXVol", 1f), 0f, 1f);

        float masterVol = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_MasterVol", 1f), 0f, 1f);
        AudioListener.volume = masterVol;
    }

    public void SetMasterVolume(float vol) { AudioListener.volume = vol; PlayerPrefs.SetFloat("Settings_MasterVol", vol); }
    public void SetMusicVolume(float vol) { globalMusicVolume = vol; PlayerPrefs.SetFloat("Settings_MusicVol", vol); UpdateMusicVolume(); }
    public void SetSFXVolume(float vol) { globalSFXVolume = vol; PlayerPrefs.SetFloat("Settings_SFXVol", vol); }

    private void UpdateMusicVolume()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            foreach (var kvp in musicDictionary)
            {
                if (kvp.Value.clips != null && kvp.Value.clips.Length > 0 && kvp.Value.clips[0] == musicSource.clip)
                {
                    musicSource.volume = kvp.Value.volume * globalMusicVolume;
                    break;
                }
            }
        }
    }

    private void InitializeDictionaries()
    {
        sfxDictionary = new Dictionary<string, SoundGroup>();
        uiDictionary = new Dictionary<string, SoundGroup>();
        musicDictionary = new Dictionary<string, SoundGroup>();

        // UI
        uiDictionary.Add(AudioID.UI_Click, uiClick); uiDictionary.Add(AudioID.UI_Hover, uiHover); uiDictionary.Add(AudioID.UI_QuestAccept, uiQuestAccept);
        uiDictionary.Add(AudioID.UI_QuestComplete, uiQuestComplete); uiDictionary.Add(AudioID.UI_Error, uiError); uiDictionary.Add(AudioID.UI_LevelUp, uiLevelUp);
        uiDictionary.Add(AudioID.UI_Purchase, uiPurchase);

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
        musicDictionary.Add(AudioID.Music_Camp, musicCamp); musicDictionary.Add(AudioID.Music_Battle, musicBattle);
    }

    public void PlaySFX(string soundName)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && group.clips.Length > 0)
        {
            AudioSource source = GetAvailableSFXSource();
            if (source != null)
            {
                source.clip = group.clips[Random.Range(0, group.clips.Length)];
                source.volume = group.volume * globalSFXVolume;
                source.pitch = group.randomizePitch ? group.pitch * Random.Range(0.85f, 1.15f) : group.pitch;
                source.Play();
            }
        }
    }

    public void PlayUI(string soundName)
    {
        // АНТИ-СПАМ: Забороняємо грати абсолютно той самий звук, якщо з минулого виклику пройшло менше 0.05 секунди
        if (soundName == lastUIPlaySound && Time.unscaledTime - lastUIPlayTime < 0.05f) return;

        lastUIPlaySound = soundName;
        lastUIPlayTime = Time.unscaledTime;

        if (uiDictionary.TryGetValue(soundName, out SoundGroup group) && group.clips.Length > 0 && uiSource != null)
        {
            float dynamicPitch = group.randomizePitch ? group.pitch * Random.Range(0.85f, 1.15f) : group.pitch * Random.Range(0.95f, 1.05f);
            uiSource.pitch = dynamicPitch;
            uiSource.PlayOneShot(group.clips[Random.Range(0, group.clips.Length)], group.volume * globalSFXVolume);
        }
    }

    public void PlayMusic(string soundName)
    {
        if (musicDictionary.TryGetValue(soundName, out SoundGroup group) && group.clips.Length > 0 && musicSource != null)
        {
            AudioClip clipToPlay = group.clips[0];
            if (musicSource.clip == clipToPlay && musicSource.isPlaying) return;
            if (musicFadeCoroutine != null) StopCoroutine(musicFadeCoroutine);
            musicFadeCoroutine = StartCoroutine(FadeMusicRoutine(clipToPlay, group.volume * globalMusicVolume, group.pitch));
        }
    }

    private IEnumerator FadeMusicRoutine(AudioClip newClip, float targetVolume, float pitch)
    {
        if (musicSource.isPlaying)
        {
            float startVol = musicSource.volume;
            float t = 0;
            while (t < musicFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(startVol, 0f, t / musicFadeDuration);
                yield return null;
            }
            musicSource.volume = 0f;
            musicSource.Stop();
        }

        musicSource.clip = newClip;
        musicSource.pitch = pitch;
        musicSource.Play();

        float fadeInTimer = 0;
        while (fadeInTimer < musicFadeDuration)
        {
            fadeInTimer += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVolume, fadeInTimer / musicFadeDuration);
            yield return null;
        }
        musicSource.volume = targetVolume;
    }

    private AudioSource GetAvailableSFXSource()
    {
        foreach (AudioSource source in sfxSources) if (!source.isPlaying) return source;
        return sfxSources[0];
    }
}