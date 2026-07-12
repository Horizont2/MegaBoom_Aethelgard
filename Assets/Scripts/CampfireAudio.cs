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

    private void OnEnable()
    {
        if (AudioManager.Instance == null) return;
        string key = string.IsNullOrEmpty(customEventKey) ? AudioID.Ambient_CampFire : customEventKey;
        loopHandle = AudioManager.Instance.PlayLoopingSFX3D(key, transform);
    }

    private void OnDisable()
    {
        if (loopHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(loopHandle, 0.4f);
            loopHandle = -1;
        }
    }

    private void OnDestroy() => OnDisable();
}
