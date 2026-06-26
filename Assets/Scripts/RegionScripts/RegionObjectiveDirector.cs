using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class RegionObjectiveDirector : MonoBehaviour
{
    [Header("Sky Beacon")]
    [Tooltip("Висота світлового стовпа над тотемом (м)")]
    public float beaconHeight = 120f;
    [Tooltip("Діаметр світлового стовпа (м)")]
    public float beaconDiameter = 0.5f;
    [Tooltip("Колір стовпа на активному тотемі")]
    public Color activeBeaconColor = new Color(1f, 0.25f, 0.15f, 0.85f);
    [Tooltip("Опціональний кастомний матеріал. Якщо null — створюється на льоту з URP/Unlit")]
    public Material beaconMaterialTemplate;
    [Tooltip("Сила пульсації стовпа (0 = без пульсу)")]
    [Range(0f, 1f)] public float pulseStrength = 0.35f;
    public float pulseSpeed = 1.6f;
    [Tooltip("Вертикальний підйом основи стовпа над тотемом (м)")]
    public float beaconBaseOffset = 4f;

    [Header("Compass Integration")]
    public bool addCompassMarkers = true;

    [Header("Distance Label (Worldspace TMP)")]
    [Tooltip("Показувати worldspace плашку дистанції над найближчим тотемом")]
    public bool showDistanceLabel = true;
    [Tooltip("Розмір шрифту 3D TMP (5-12 — норм)")]
    public float labelFontSize = 7f;
    [Tooltip("Висота плашки над тотемом (м)")]
    public float labelHeight = 6f;
    [Tooltip("Текст-префікс перед дистанцією")]
    public string labelPrefix = "PURIFY TOTEM";
    [Tooltip("TMP_FontAsset. Якщо null — пробує LiberationSans SDF (за замовчуванням у TMP Essentials)")]
    public TMP_FontAsset labelFontAsset;

    [Header("Search Timing")]
    public float searchTimeout = 30f;
    public float visibilityScanInterval = 0.4f;

    private class BeaconHandle
    {
        public RegionTotem totem;
        public GameObject beacon;
        public Material material;
        public Color baseColor;
    }

    private readonly List<BeaconHandle> beacons = new List<BeaconHandle>();
    private float scanTimer = 0f;
    private bool initialized = false;
    private Transform player;

    // Distance label (single instance, follows the closest active totem)
    private GameObject distanceLabel;
    private TextMeshPro distanceText;
    private Transform mainCamTransform;

    private void Start()
    {
        StartCoroutine(InitRoutine());
    }

    private IEnumerator InitRoutine()
    {
        // Let MissionInitializer.Start() flip the flag first
        yield return null;

        if (!WorldEncounterDirector.IsActiveRegionMission())
        {
            enabled = false;
            yield break;
        }

        float deadline = Time.unscaledTime + searchTimeout;
        RegionTotem[] found = null;
        while (Time.unscaledTime < deadline)
        {
            found = FindObjectsByType<RegionTotem>(FindObjectsSortMode.None);
            if (found != null && found.Length > 0) break;
            yield return new WaitForSeconds(0.5f);
        }

        if (found == null || found.Length == 0)
        {
            Debug.LogWarning("[RegionObjectiveDirector] No RegionTotem found in scene.");
            yield break;
        }

        for (int i = 0; i < found.Length; i++)
        {
            CreateBeacon(found[i]);
            if (addCompassMarkers && found[i].GetComponent<CompassMarkerItem>() == null)
                found[i].gameObject.AddComponent<CompassMarkerItem>();
        }

        if (showDistanceLabel) CreateDistanceLabel();

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;
        mainCamTransform = CameraCache.MainTransform;

        initialized = true;
    }

    private void CreateBeacon(RegionTotem totem)
    {
        if (totem == null) return;

        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "TotemSkyBeacon";

        Collider col = beacon.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Don't parent — totem may carry weird rotation/scale that would
        // tip the pillar over or stretch it sideways. Place in world space.
        beacon.transform.SetParent(null);
        // Use the totem's renderer bounds centre when available — pivot
        // can sit at the base or one corner of the mesh, so
        // `totem.transform.position + up` puts the beacon over the wrong
        // spot and reads as "the beam is crooked over the totem".
        // Bounds.center always picks the visual centre regardless of how
        // the prefab was authored.
        Vector3 totemAnchor = totem.transform.position;
        Renderer totemRend = totem.GetComponentInChildren<Renderer>();
        if (totemRend != null)
        {
            Bounds b = totemRend.bounds;
            totemAnchor = new Vector3(b.center.x, b.max.y, b.center.z);
        }
        beacon.transform.position = totemAnchor + Vector3.up * (beaconHeight * 0.5f + beaconBaseOffset);
        beacon.transform.rotation = Quaternion.identity;
        // Unity's cylinder primitive is 1m diameter and 2m tall at scale (1,1,1),
        // so to get world (diameter, height) we use scale (diameter, height/2, diameter).
        beacon.transform.localScale = new Vector3(beaconDiameter, beaconHeight * 0.5f, beaconDiameter);

        Material mat = BuildBeaconMaterial();
        Renderer rend = beacon.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        beacons.Add(new BeaconHandle
        {
            totem = totem,
            beacon = beacon,
            material = mat,
            baseColor = activeBeaconColor
        });
    }

    private Material BuildBeaconMaterial()
    {
        Material mat;
        if (beaconMaterialTemplate != null)
        {
            mat = new Material(beaconMaterialTemplate);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", activeBeaconColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", activeBeaconColor);

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (mat.HasProperty("_EmissionColor"))
        {
            mat.SetColor("_EmissionColor", activeBeaconColor * 5f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        return mat;
    }

    private void CreateDistanceLabel()
    {
        distanceLabel = new GameObject("ObjectiveDistanceLabel");
        distanceText = distanceLabel.AddComponent<TextMeshPro>();

        // Try wired-up font first, then fall back to TMP's default LiberationSans
        if (labelFontAsset != null) distanceText.font = labelFontAsset;
        else
        {
            TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fallback != null) distanceText.font = fallback;
        }

        distanceText.fontSize = labelFontSize;
        distanceText.alignment = TextAlignmentOptions.Center;
        distanceText.color = activeBeaconColor;
        distanceText.text = labelPrefix;
        distanceText.fontStyle = FontStyles.Bold;
        distanceText.outlineWidth = 0.2f;
        distanceText.outlineColor = Color.black;

        distanceLabel.SetActive(false);
    }

    private static readonly int s_emissionColorID = Shader.PropertyToID("_EmissionColor");

    private void Update()
    {
        if (!initialized || beacons.Count == 0) return;

        scanTimer -= Time.deltaTime;
        bool runScan = scanTimer <= 0f;
        if (runScan) scanTimer = visibilityScanInterval;

        // Pulse only matters visually if it changes — skip the entire emission
        // write when pulseStrength is zero.
        bool pulseEnabled = pulseStrength > 0f;
        float pulse = pulseEnabled ? 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength : 1f;

        BeaconHandle closestActive = null;
        float closestDistSqr = float.MaxValue;
        Vector3 playerPos = player != null ? player.position : transform.position;

        for (int i = 0; i < beacons.Count; i++)
        {
            BeaconHandle h = beacons[i];
            if (h.totem == null || h.beacon == null) continue;

            bool shouldShow = ShouldBeaconBeVisible(h.totem);

            if (runScan && h.beacon.activeSelf != shouldShow)
                h.beacon.SetActive(shouldShow);

            if (h.beacon.activeSelf && pulseEnabled && h.material != null)
                h.material.SetColor(s_emissionColorID, h.baseColor * (5f * pulse));

            if (shouldShow && player != null)
            {
                float dsq = (h.totem.transform.position - playerPos).sqrMagnitude;
                if (dsq < closestDistSqr)
                {
                    closestDistSqr = dsq;
                    closestActive = h;
                }
            }
        }

        UpdateDistanceLabel(closestActive, closestDistSqr);
    }

    private void UpdateDistanceLabel(BeaconHandle closestActive, float closestDistSqr)
    {
        if (distanceLabel == null) return;

        if (closestActive == null || player == null)
        {
            if (distanceLabel.activeSelf) distanceLabel.SetActive(false);
            return;
        }

        if (!distanceLabel.activeSelf) distanceLabel.SetActive(true);

        Vector3 totemPos = closestActive.totem.transform.position;
        distanceLabel.transform.position = totemPos + Vector3.up * labelHeight;

        if (mainCamTransform == null) mainCamTransform = CameraCache.MainTransform;
        if (mainCamTransform != null)
        {
            // Billboard — face away from camera so text reads upright from the player's view.
            Vector3 fwd = distanceLabel.transform.position - mainCamTransform.position;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.01f)
                distanceLabel.transform.rotation = Quaternion.LookRotation(fwd.normalized);
        }

        if (distanceText != null)
        {
            float dist = Mathf.Sqrt(closestDistSqr);
            distanceText.text = $"{labelPrefix}\n<size={labelFontSize * 1.3f}>{Mathf.CeilToInt(dist)} m</size>";
        }
    }

    private static bool ShouldBeaconBeVisible(RegionTotem totem)
    {
        if (totem.isPurified) return false;
        if (totem.isActivated) return false;
        if (totem.interactHintVFX != null) return totem.interactHintVFX.activeSelf;
        return true;
    }
}
