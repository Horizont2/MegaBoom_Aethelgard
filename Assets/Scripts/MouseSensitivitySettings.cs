using UnityEngine;

// Mouse sensitivity multiplier the player chooses in settings. Camera
// controllers (CameraFollow) read SensitivityMultiplier when applying
// their own per-script mouseSensitivity values.
public static class MouseSensitivitySettings
{
    public static event System.Action OnChanged;

    // 0.25 .. 3.0 with 1.0 the default. UI slider should expose 25..300.
    public static float Multiplier
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat("Settings_MouseSensitivity", 1f), 0.25f, 3f);
        set
        {
            PlayerPrefs.SetFloat("Settings_MouseSensitivity", Mathf.Clamp(value, 0.25f, 3f));
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }
}
