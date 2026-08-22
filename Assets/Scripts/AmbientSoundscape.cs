using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// Periodically plays one-shot ambient SFX (wind, distant howls, crow caws,
// thunder rumble) layered over the music. Self-installs into combat/world
// scenes via RuntimeInitializeOnLoadMethod so no scene wiring is needed.
//
// Picks SFX based on scene context — howls/thunder only in combat scenes,
// leaf rustle/crow more often in calmer regions. Intervals jitter so the
// listener never hears the same beat twice.
public class AmbientSoundscape : MonoBehaviour
{
    private struct AmbientEntry
    {
        public string id;
        public float minDelay;
        public float maxDelay;
        public float weight;
    }

    private static AmbientSoundscape s_instance;

    private float nextPlayTime;
    private string lastPlayed;
    private bool isCombatScene;

    // NOTE: Ambient_LeafRustle (bush/leaf rustle) removed from both
    // lists per design — it was flagged as noise.
    private readonly List<AmbientEntry> calmEntries = new List<AmbientEntry>
    {
        new AmbientEntry { id = AudioID.Ambient_Wind,        minDelay = 14f, maxDelay = 26f, weight = 3f },
        new AmbientEntry { id = AudioID.Ambient_Crow,        minDelay = 18f, maxDelay = 40f, weight = 2f },
        new AmbientEntry { id = AudioID.Ambient_LeafRustle,  minDelay = 12f, maxDelay = 28f, weight = 2.5f },
    };

    private readonly List<AmbientEntry> combatEntries = new List<AmbientEntry>
    {
        new AmbientEntry { id = AudioID.Ambient_Wind,            minDelay = 10f, maxDelay = 20f, weight = 2f },
        new AmbientEntry { id = AudioID.Ambient_Howl,            minDelay = 22f, maxDelay = 48f, weight = 2.5f },
        new AmbientEntry { id = AudioID.Ambient_Crow,            minDelay = 16f, maxDelay = 32f, weight = 1.5f },
        new AmbientEntry { id = AudioID.Ambient_DistantThunder,  minDelay = 30f, maxDelay = 70f, weight = 1.5f },
        new AmbientEntry { id = AudioID.Ambient_LeafRustle,      minDelay = 14f, maxDelay = 30f, weight = 2f },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[AmbientSoundscape]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<AmbientSoundscape>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyScene(scene);
    }

    private void ApplyScene(Scene scene)
    {
        string n = scene.name;
        // CampScene is the only "calm" scene; everything else is treated as combat
        // (Lvl_1 tutorial, GameScene/WorldScene region runs).
        isCombatScene = n != "CampScene";
        ScheduleNext(Random.Range(6f, 12f));
    }

    private void ScheduleNext(float fromNow)
    {
        nextPlayTime = Time.unscaledTime + fromNow;
    }

    private void Update()
    {
        if (AudioManager.Instance == null) return;
        if (Time.unscaledTime < nextPlayTime) return;

        // Suppress crow / thunder / howl beats while the game world is
        // frozen for a dramatic beat (death cinematic, pause menu, boss
        // executes). The ambient bed keeps playing via FMOD, but we no
        // longer schedule new one-shots on top of it.
        if (Time.timeScale <= 0.05f)
        {
            ScheduleNext(2f);
            return;
        }
        if (PlayerController.LocalInstance != null && PlayerController.LocalInstance.IsDead)
        {
            ScheduleNext(4f);
            return;
        }

        List<AmbientEntry> pool = isCombatScene ? combatEntries : calmEntries;

        // Weighted pick; avoid replaying the immediately previous clip so the
        // soundscape doesn't ever feel repetitive.
        float totalWeight = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].id == lastPlayed) continue;
            totalWeight += pool[i].weight;
        }
        if (totalWeight <= 0f) totalWeight = 1f;

        float roll = Random.Range(0f, totalWeight);
        AmbientEntry picked = pool[0];
        float accum = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].id == lastPlayed) continue;
            accum += pool[i].weight;
            if (roll <= accum) { picked = pool[i]; break; }
        }

        AudioManager.Instance.PlaySFX(picked.id);
        lastPlayed = picked.id;

        ScheduleNext(Random.Range(picked.minDelay, picked.maxDelay));
    }
}
