using UnityEngine;

// One of the braziers around a cursed monolith.
//
// Deliberately almost inert. It holds a position, a lit/unlit look, and the one
// public verb that changes it — everything about WHO may light it, WHEN, and
// what that costs is decided by RitualMonolith.
//
// ==== WHY THE BRAZIER HAS NO Update ====
//
// The obvious build is a proximity test and a hold timer per brazier. That is
// four Updates each running a distance check and a key poll for one POI, and
// there can be several of these on a map. The monolith already has to know
// which brazier the player is at, because it draws the progress bar and decides
// whether the channel survives — so it runs one loop and this holds state.
[DisallowMultipleComponent]
public class RitualBrazier : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Shown while the brazier is cold. Usually the bare bowl.")]
    public GameObject unlitVisual;
    [Tooltip("Shown once it is lit — flame particles, the glow, the light. Switched on, never spawned, so there is no instantiate cost mid-fight.")]
    public GameObject litVisual;
    [Tooltip("Optional point light, lifted to full over the same beat the flame appears on.")]
    public Light flame;
    [Tooltip("Brightness the light reaches once lit.")]
    public float flameIntensity = 3.2f;
    [Tooltip("Seconds the flame takes to come up. Instant reads as a switch; this reads as catching.")]
    public float flameRise = 0.45f;

    [Header("Where the player stands")]
    [Tooltip("How close the player must be to channel this one. Keep it tight — the point is that they are pinned in one spot with their back open.")]
    public float interactRange = 2.6f;

    public bool IsLit { get; private set; }

    private float _lightT;

    private void Awake()
    {
        // Cold to begin with, whatever the prefab was saved in.
        ApplyLit(false, instant: true);
    }

    public void Light()
    {
        if (IsLit) return;
        IsLit = true;
        ApplyLit(true, instant: false);
    }

    public void Extinguish()
    {
        if (!IsLit) return;
        IsLit = false;
        ApplyLit(false, instant: true);
    }

    private void ApplyLit(bool on, bool instant)
    {
        if (unlitVisual != null) unlitVisual.SetActive(!on);
        if (litVisual != null) litVisual.SetActive(on);
        if (flame == null) return;

        _lightT = instant ? (on ? 1f : 0f) : 0f;
        flame.enabled = on;
        flame.intensity = on && instant ? flameIntensity : 0f;
        if (!on) return;
        // Ride it up on the ordinary clock. A brazier catching is not a beat
        // that needs to survive a pause.
        StopAllCoroutines();
        StartCoroutine(RiseRoutine());
    }

    private System.Collections.IEnumerator RiseRoutine()
    {
        while (_lightT < 1f)
        {
            _lightT += Time.deltaTime / Mathf.Max(0.01f, flameRise);
            if (flame != null) flame.intensity = Mathf.Lerp(0f, flameIntensity, Mathf.Clamp01(_lightT));
            yield return null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
