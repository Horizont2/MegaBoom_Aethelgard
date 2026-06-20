using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RegionObjectiveDirector : MonoBehaviour
{
    [Header("Sky Beacon")]
    [Tooltip("Висота світлового стовпа над тотемом (м)")]
    public float beaconHeight = 90f;
    [Tooltip("Діаметр світлового стовпа (м)")]
    public float beaconRadius = 0.7f;
    [Tooltip("Колір стовпа на цілі, на яку зараз треба йти")]
    public Color activeBeaconColor = new Color(1f, 0.25f, 0.15f, 0.85f);
    [Tooltip("Опціональний кастомний матеріал. Якщо null — створюється на льоту з URP/Unlit")]
    public Material beaconMaterialTemplate;
    [Tooltip("Сила пульсації стовпа (0 = без пульсу)")]
    [Range(0f, 1f)] public float pulseStrength = 0.35f;
    public float pulseSpeed = 1.6f;

    [Header("Compass Integration")]
    [Tooltip("Додати CompassMarkerItem до тотемів, щоб вони з'являлись на компасі гравця")]
    public bool addCompassMarkers = true;

    [Header("Search Timing")]
    [Tooltip("Скільки секунд чекати на появу тотемів у сцені")]
    public float searchTimeout = 30f;
    [Tooltip("Інтервал оновлення видимості маяків")]
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

    private void Start()
    {
        StartCoroutine(InitRoutine());
    }

    private IEnumerator InitRoutine()
    {
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
            {
                found[i].gameObject.AddComponent<CompassMarkerItem>();
            }
        }

        initialized = true;
    }

    private void CreateBeacon(RegionTotem totem)
    {
        if (totem == null) return;

        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "TotemSkyBeacon";

        Collider col = beacon.GetComponent<Collider>();
        if (col != null) Destroy(col);

        beacon.transform.SetParent(totem.transform, false);
        beacon.transform.localPosition = Vector3.up * (beaconHeight * 0.5f + 3f);
        beacon.transform.localRotation = Quaternion.identity;
        // Cylinder primitive is 2m tall by default => scale Y by half-height
        beacon.transform.localScale = new Vector3(beaconRadius * 2f, beaconHeight * 0.5f, beaconRadius * 2f);

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

        // URP transparent surface
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

    private void Update()
    {
        if (!initialized || beacons.Count == 0) return;

        scanTimer -= Time.deltaTime;
        bool runScan = scanTimer <= 0f;
        if (runScan) scanTimer = visibilityScanInterval;

        float pulse = pulseStrength > 0f ? 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength : 1f;

        for (int i = 0; i < beacons.Count; i++)
        {
            BeaconHandle h = beacons[i];
            if (h.totem == null || h.beacon == null) continue;

            if (runScan)
            {
                bool shouldShow = ShouldBeaconBeVisible(h.totem);
                if (h.beacon.activeSelf != shouldShow) h.beacon.SetActive(shouldShow);
            }

            if (h.beacon.activeSelf && pulseStrength > 0f && h.material != null && h.material.HasProperty("_EmissionColor"))
            {
                h.material.SetColor("_EmissionColor", h.baseColor * (5f * pulse));
            }
        }
    }

    private static bool ShouldBeaconBeVisible(RegionTotem totem)
    {
        if (totem.isPurified) return false;
        if (totem.isActivated) return false;
        // Mirror RegionManager logic: while interactHintVFX is wired up, follow its visibility.
        // Otherwise fall back to a sensible default (visible whenever totem is unpurified+inactive).
        if (totem.interactHintVFX != null) return totem.interactHintVFX.activeSelf;
        return true;
    }
}
