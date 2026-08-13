using UnityEngine;

// Safety net for cinematic time-scale effects (glory kill freeze frame,
// slow-mo aftermaths). Those set Time.timeScale to 0 / a fraction and
// rely on a coroutine's finally block to restore it. If that coroutine
// dies before the finally runs — the boss GameObject is destroyed
// mid-glory-kill, a scene reload, a StopCoroutine — the finally NEVER
// executes and time stays frozen forever (the reported "time stuck
// after killing a boss" bug).
//
// A cutscene Arm()s this guard with the window it expects to finish in.
// If, past that window, time is still throttled AND nothing legitimately
// wants it frozen (level-up menu, pause), the guard force-restores it.
// When the cutscene's own restore runs normally, the guard's check finds
// time already ~1 and no-ops.
public class CinematicTimeGuard : MonoBehaviour
{
    private static CinematicTimeGuard s_inst;
    private float deadline = -1f;

    public static void Arm(float realtimeDuration)
    {
        if (s_inst == null)
        {
            var go = new GameObject("[CinematicTimeGuard]");
            DontDestroyOnLoad(go);
            s_inst = go.AddComponent<CinematicTimeGuard>();
        }
        // Take the later of any existing deadline and this one, so
        // overlapping cutscenes don't shorten each other's window.
        float d = Time.realtimeSinceStartup + realtimeDuration;
        if (d > s_inst.deadline) s_inst.deadline = d;
        s_inst.enabled = true;
    }

    private void Update()
    {
        if (deadline < 0f) { enabled = false; return; }
        if (Time.realtimeSinceStartup < deadline) return;
        deadline = -1f;

        if (Time.timeScale < 0.95f && !AnyLegitFreeze())
        {
            Debug.LogWarning("[CinematicTimeGuard] Cinematic window elapsed but Time.timeScale is still throttled — a cutscene coroutine likely died mid-freeze. Restoring to 1.");
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
        enabled = false;
    }

    // Systems that legitimately hold time frozen — don't fight them.
    private static bool AnyLegitFreeze()
    {
        if (LevelUpManager.IsMenuOpen) return true;
        if (PauseSceneController.IsPauseActive) return true;
        return false;
    }
}
