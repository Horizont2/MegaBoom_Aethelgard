using UnityEngine;

// An enemy watchtower that sweeps the ground with a searchlight and raises the
// region alarm when the player is caught in it.
//
// The beam is the design, not decoration. A detection radius the player cannot
// see is just an invisible trap; a slowly rotating pool of light on the ground
// states the danger zone plainly and in advance, so crossing it is a decision.
// The player can read the sweep, time it, go round it, or climb up and put the
// tower out entirely.
//
// Three timings carry the whole feel:
//   sweepSpeed   -- slow enough to be read and waited out.
//   lockOnTime   -- caught in the beam is not instant failure. The light turns
//                   and the tone rises first, which is the window to get out.
//   alarm        -- handed to RegionAlertDirector, which owns the response and
//                   makes sure it expires.
//
// Everything visual is built in code so the component can be dropped onto any
// tower mesh in the project without authoring a prefab per tower.
[DisallowMultipleComponent]
public class WatchtowerAlarm : MonoBehaviour
{
    [Header("Beam Geometry")]
    [Tooltip("Where the light emits from. Leave empty to use the top of this object's bounds.")]
    public Transform beamOrigin;
    [Tooltip("How far from the tower the lit pool sits.")]
    public float sweepRadius = 22f;
    [Tooltip("Radius of the lit pool on the ground — this IS the detection area.")]
    public float poolRadius = 7f;
    [Tooltip("Degrees per second the beam rotates around the tower.")]
    public float sweepSpeed = 24f;
    [Tooltip("Randomises the starting angle so a row of towers doesn't sweep in lockstep.")]
    public bool randomiseStartAngle = true;

    [Header("Detection")]
    [Tooltip("Seconds the player must stay in the pool before the alarm sounds.")]
    public float lockOnTime = 1.2f;
    [Tooltip("How fast the lock-on decays once the player leaves the light.")]
    public float lockOnDecayRate = 1.5f;
    [Tooltip("Seconds after an alarm before this tower can raise another.")]
    public float rearmDelay = 20f;

    [Header("Look")]
    public Color calmColor = new Color(0.95f, 0.88f, 0.65f);
    public Color alarmColor = new Color(1f, 0.25f, 0.15f);
    public float lightIntensity = 40f;
    [Tooltip("Segments in the ground ring. Higher is smoother, 40 is plenty.")]
    public int ringSegments = 44;

    private Light spot;
    private LineRenderer ring;
    private float angle;
    private float lockOn;
    private float nextAlarmAllowedTime;
    private Transform player;
    private float playerScanTimer;

    // Exposed so a destructible-tower script can silence this one.
    public bool disabledByDamage = false;

    private void Start()
    {
        if (randomiseStartAngle) angle = Random.Range(0f, 360f);
        if (beamOrigin == null) beamOrigin = ResolveTopOfTower();
        BuildSpot();
        BuildRing();
    }

    // Towers in this project are plain meshes with no marked emitter point, so
    // aim from the top of whatever geometry is here rather than requiring every
    // tower prefab to be edited by hand.
    private Transform ResolveTopOfTower()
    {
        var go = new GameObject("BeamOrigin");
        go.transform.SetParent(transform, false);

        float top = 6f;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            top = b.max.y - transform.position.y;
        }
        go.transform.localPosition = new Vector3(0f, Mathf.Max(3f, top - 0.6f), 0f);
        return go.transform;
    }

    private void BuildSpot()
    {
        var go = new GameObject("SearchBeam");
        go.transform.SetParent(beamOrigin, false);
        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = calmColor;
        spot.intensity = lightIntensity;
        spot.range = sweepRadius * 2.6f;
        // Widen the cone just enough that the lit pool on the ground is about
        // poolRadius across at the distance it lands.
        float height = Mathf.Max(1f, beamOrigin.position.y - transform.position.y);
        float slant = Mathf.Sqrt(sweepRadius * sweepRadius + height * height);
        spot.spotAngle = Mathf.Clamp(2f * Mathf.Atan2(poolRadius, slant) * Mathf.Rad2Deg, 8f, 90f);
        spot.shadows = LightShadows.None;   // a sweeping shadowed spot is expensive and reads no better
        spot.renderMode = LightRenderMode.ForceVertex;
    }

    private void BuildRing()
    {
        var go = new GameObject("SearchRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Mathf.Max(8, ringSegments);
        ring.startWidth = 0.22f;
        ring.endWidth = 0.22f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var ringMat = new Material(sh);
            ring.sharedMaterial = ringMat;
            OwnedMaterial.Attach(ring.gameObject, ringMat);
        }
        ring.startColor = calmColor;
        ring.endColor = calmColor;
    }

    private void Update()
    {
        if (disabledByDamage)
        {
            if (spot != null) spot.enabled = false;
            if (ring != null) ring.enabled = false;
            return;
        }

        angle += sweepSpeed * Time.deltaTime;
        if (angle >= 360f) angle -= 360f;

        float rad = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * sweepRadius;
        Vector3 poolCenter = transform.position + offset;
        poolCenter.y = GroundY(poolCenter);

        AimBeam(poolCenter);
        UpdateDetection(poolCenter);
        DrawRing(poolCenter);
    }

    private void AimBeam(Vector3 poolCenter)
    {
        if (spot == null) return;
        Vector3 dir = poolCenter - beamOrigin.position;
        if (dir.sqrMagnitude > 0.001f)
            spot.transform.rotation = Quaternion.LookRotation(dir.normalized);

        float heat = Mathf.Clamp01(lockOn / Mathf.Max(0.01f, lockOnTime));
        spot.color = Color.Lerp(calmColor, alarmColor, heat);
        // Pulse harder the closer the tower is to sounding, so the warning is
        // audible-in-vision rather than a silent countdown.
        spot.intensity = lightIntensity * (1f + heat * (0.35f + 0.25f * Mathf.Sin(Time.time * 14f)));
    }

    private void UpdateDetection(Vector3 poolCenter)
    {
        playerScanTimer -= Time.deltaTime;
        if (playerScanTimer <= 0f)
        {
            playerScanTimer = 0.5f;
            var pc = FindFirstObjectByType<PlayerController>();
            player = pc != null && pc.currentHealth > 0f ? pc.transform : null;
        }

        bool lit = false;
        if (player != null)
        {
            Vector3 flat = player.position - poolCenter;
            flat.y = 0f;
            if (flat.sqrMagnitude <= poolRadius * poolRadius)
            {
                // The beam has to actually reach them — standing in a doorway
                // inside the circle should not count.
                Vector3 to = (player.position + Vector3.up) - beamOrigin.position;
                float d = to.magnitude;
                lit = !Physics.Raycast(beamOrigin.position, to / d, d - 0.5f,
                                       SightBlockerMask(), QueryTriggerInteraction.Ignore);
            }
        }

        if (lit) lockOn += Time.deltaTime;
        else lockOn = Mathf.Max(0f, lockOn - Time.deltaTime * lockOnDecayRate);

        if (lockOn < lockOnTime) return;
        if (Time.time < nextAlarmAllowedTime) return;

        lockOn = 0f;
        nextAlarmAllowedTime = Time.time + rearmDelay;

        if (RegionAlertDirector.Instance != null && player != null)
            RegionAlertDirector.Instance.RaiseAlert(player.position, transform);
    }

    private void DrawRing(Vector3 poolCenter)
    {
        if (ring == null) return;

        float heat = Mathf.Clamp01(lockOn / Mathf.Max(0.01f, lockOnTime));
        Color c = Color.Lerp(calmColor, alarmColor, heat);
        ring.startColor = c;
        ring.endColor = c;

        int segments = ring.positionCount;
        float step = 360f / segments;
        float a = 0f;
        for (int i = 0; i < segments; i++)
        {
            float r = a * Mathf.Deg2Rad;
            Vector3 p = new Vector3(poolCenter.x + Mathf.Cos(r) * poolRadius,
                                    0f,
                                    poolCenter.z + Mathf.Sin(r) * poolRadius);
            // Follow the ground, but clamp to a band around the pool centre so a
            // slope or a ledge cannot tear the ring into a hanging sheet.
            p.y = Mathf.Clamp(GroundY(p), poolCenter.y - 3f, poolCenter.y + 2f) + 0.12f;
            ring.SetPosition(i, p);
            a += step;
        }
    }

    private static int s_sightMask = -1;
    private static int SightBlockerMask()
    {
        if (s_sightMask != -1) return s_sightMask;
        int mask = 0;
        string[] names = { "Default", "Obstacles", "Nature" };
        foreach (var n in names)
        {
            int l = LayerMask.NameToLayer(n);
            if (l >= 0) mask |= 1 << l;
        }
        s_sightMask = mask;
        return s_sightMask;
    }

    private static float GroundY(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        Terrain[] all = Terrain.activeTerrains;
        if (all != null)
        {
            foreach (var t in all)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position;
                Vector3 s = t.terrainData.size;
                if (pos.x >= o.x && pos.x <= o.x + s.x && pos.z >= o.z && pos.z <= o.z + s.z)
                    return t.SampleHeight(pos) + o.y;
            }
        }
        return pos.y;
    }
}
