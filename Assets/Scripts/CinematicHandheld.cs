using UnityEngine;

// Additive "handheld camera operator" noise for cutscenes. The single
// biggest tell of an amateur cutscene is a mathematically-still camera —
// real cinematography always has micro-drift from the operator's hands.
//
// Design: cinematic coroutines write the camera transform ABSOLUTELY every
// frame (Lerp chains). This component layers a tiny Perlin offset on top in
// LateUpdate without fighting them:
//   - If external code rewrote the transform this frame, the current value
//     IS the new base (their write never contained our offset).
//   - If nobody wrote (a hold/freeze frame), we restore our own predicted
//     base so offsets never accumulate into drift.
// Runs on UNSCALED time so the drift stays alive through slow-mo and
// freeze frames — that residual life inside a timeScale=0 impact frame is
// exactly what sells it.
//
// Usage from any cinematic:
//   var hh = CinematicHandheld.Begin(Camera.main, 0.04f, 0.35f, 0.5f);
//   ... run cinematic ...
//   CinematicHandheld.End(Camera.main);   // always call in finally
public class CinematicHandheld : MonoBehaviour
{
    [Tooltip("Positional drift amplitude in metres (0.03-0.06 feels like a tripod-mounted operator; 0.1+ reads as documentary handheld).")]
    public float positionAmplitude = 0.04f;
    [Tooltip("Rotational drift amplitude in degrees.")]
    public float rotationAmplitude = 0.35f;
    [Tooltip("Base noise frequency in Hz. 0.3-0.6 is a natural breathing rate.")]
    public float frequency = 0.5f;

    private Vector3 lastBasePos;
    private Quaternion lastBaseRot = Quaternion.identity;
    private Vector3 lastOffsetPos;
    private Quaternion lastOffsetRot = Quaternion.identity;
    private float seed;
    private bool primed;

    public static CinematicHandheld Begin(Camera cam, float posAmp = 0.04f, float rotAmp = 0.35f, float freq = 0.5f)
    {
        if (cam == null) return null;
        var existing = cam.GetComponent<CinematicHandheld>();
        if (existing == null) existing = cam.gameObject.AddComponent<CinematicHandheld>();
        existing.positionAmplitude = posAmp;
        existing.rotationAmplitude = rotAmp;
        existing.frequency = freq;
        existing.enabled = true;
        return existing;
    }

    public static void End(Camera cam)
    {
        if (cam == null) return;
        var existing = cam.GetComponent<CinematicHandheld>();
        if (existing != null) Destroy(existing);
    }

    private void OnEnable()
    {
        seed = Random.Range(0f, 1000f);
        primed = false;
    }

    private void OnDisable()
    {
        // Remove the residual offset so ending the effect doesn't leave the
        // camera nudged off wherever the cinematic parked it.
        if (primed)
        {
            transform.position = lastBasePos;
            transform.rotation = lastBaseRot;
            primed = false;
        }
    }

    private void LateUpdate()
    {
        Transform t = transform;
        Vector3 curPos = t.position;
        Quaternion curRot = t.rotation;

        // Base detection — see class comment.
        Vector3 basePos;
        Quaternion baseRot;
        bool untouched = primed
            && (curPos - (lastBasePos + lastOffsetPos)).sqrMagnitude < 1e-6f
            && Quaternion.Angle(curRot, lastBaseRot * lastOffsetRot) < 0.05f;
        if (untouched) { basePos = lastBasePos; baseRot = lastBaseRot; }
        else { basePos = curPos; baseRot = curRot; }

        // Two-octave centred Perlin per channel — the second octave adds the
        // irregular "muscle correction" jitter on top of the slow sway.
        float time = Time.unscaledTime * frequency;
        float nx = Centred(seed + 0f, time) + 0.5f * Centred(seed + 11f, time * 2.7f);
        float ny = Centred(seed + 37f, time) + 0.5f * Centred(seed + 51f, time * 2.3f);
        float rx = Centred(seed + 73f, time * 0.9f);
        float ry = Centred(seed + 97f, time * 1.1f);
        float rz = Centred(seed + 131f, time * 0.7f);

        // Positional drift stays in the camera's local XY plane (an operator
        // sways sideways/vertically, not along the lens axis).
        Vector3 offset = baseRot * new Vector3(nx, ny, 0f) * positionAmplitude;
        Quaternion rotOffset = Quaternion.Euler(
            rx * rotationAmplitude,
            ry * rotationAmplitude,
            rz * rotationAmplitude * 0.5f); // roll at half strength — full roll reads drunk

        t.position = basePos + offset;
        t.rotation = baseRot * rotOffset;

        lastBasePos = basePos;
        lastBaseRot = baseRot;
        lastOffsetPos = offset;
        lastOffsetRot = rotOffset;
        primed = true;
    }

    private static float Centred(float seed, float t) => Mathf.PerlinNoise(seed, t) - 0.5f;
}
