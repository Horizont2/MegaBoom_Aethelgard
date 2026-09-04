using UnityEngine;

// Birds burst out of the trees as the rider passes.
//
// The forest in Part 1 is beautiful and completely dead — nothing in it reacts to
// him, so it reads as scenery rather than a place. One flock breaking cover as he
// comes past does more for that than any amount of extra vegetation, and it costs
// a prefab and a trigger distance.
//
// Placed along the route by 'Dress Roadside'.
public class TrailerBirdFlush : MonoBehaviour
{
    [Tooltip("The flock prefab (the Zacxophone bird prefabs work directly).")]
    public GameObject flockPrefab;
    [Tooltip("Who startles them.")]
    public Transform target;
    [Tooltip("They break cover at this distance — AHEAD of him, so the flush is already happening when he arrives rather than behind his back.")]
    public float triggerRange = 26f;
    [Tooltip("Metres above the spawn point they appear.")]
    public float height = 4f;
    [Tooltip("Seconds before the flock cleans itself up.")]
    public float lifetime = 9f;
    public string cryId = "AMB/AMB_Crow";

    private bool _flushed;

    private void Start()
    {
        if (target == null)
        {
            var ride = Object.FindFirstObjectByType<TrailerHorseRide>();
            if (ride != null) target = ride.transform;
        }
    }

    private void Update()
    {
        if (_flushed || target == null || flockPrefab == null) return;

        Vector3 rel = transform.position - target.position; rel.y = 0f;
        // AHEAD of him only: a flock that erupts behind the camera is a sound
        // effect, not an image.
        if (Vector3.Dot(target.forward, rel) <= 0f) return;
        if (rel.magnitude > triggerRange) return;

        _flushed = true;
        var go = Instantiate(flockPrefab, transform.position + Vector3.up * height,
                             Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        Destroy(go, lifetime);

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(cryId))
            AudioManager.Instance.PlaySFX3D(cryId, transform.position);
    }
}
