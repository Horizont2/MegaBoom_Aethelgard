using System.Collections;
using UnityEngine;

// Simple component that plays a looping 3D fire-crackle SFX at the
// campfire's position. Attach to the Campfire GameObject. If the FMOD
// event for AudioID.Ambient_CampFire isn't wired in AudioManager the
// call falls through with a warning — no crash, just silent fire.
public class CampfireAudio : MonoBehaviour
{
    [Tooltip("Optional override — leave empty to use Ambient_CampFire.")]
    public string customEventKey = null;

    private int loopHandle = -1;
    private Coroutine startRoutine;

    private void OnEnable()
    {
        // Start via a coroutine that WAITS for the AudioManager singleton.
        // The old code bailed the instant AudioManager.Instance was null —
        // which is exactly the state during scene load, when this OnEnable
        // usually runs — so the fire stayed silent forever with no retry.
        startRoutine = StartCoroutine(StartWhenReady());
    }

    private IEnumerator StartWhenReady()
    {
        // Wait until the audio system is alive AND has finished registering
        // its FMOD events, then start the loop. Guard the loop count so a
        // scene without an AudioManager doesn't spin forever.
        float timeout = 0f;
        while (AudioManager.Instance == null && timeout < 10f)
        {
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }
        if (AudioManager.Instance == null) yield break;

        string key = string.IsNullOrEmpty(customEventKey) ? AudioID.Ambient_CampFire : customEventKey;
        // Follow the transform so the crackle stays anchored to the fire.
        loopHandle = AudioManager.Instance.PlayLoopingSFX3D(key, transform);
        startRoutine = null;
    }

    private void OnDisable()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
            startRoutine = null;
        }
        if (loopHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(loopHandle, 0.4f);
            loopHandle = -1;
        }
    }

    private void OnDestroy() => OnDisable();
}
