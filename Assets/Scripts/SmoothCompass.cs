using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class SmoothCompass : MonoBehaviour
{
    [Header("Compass UI")]
    public RectTransform compassPanel; // The mask/container
    public TextMeshProUGUI compassTextPrefab; // The template for numbers

    [Header("Radar Icons")]
    public RectTransform markersParent;
    public GameObject markerIconPrefab;

    [Header("Settings")]
    public float pixelsPerDegree = 12f;
    public float compassViewAngle = 60f;

    private List<RectTransform> compassMarks = new List<RectTransform>();
    public static List<Transform> activeMarkers = new List<Transform>();
    private List<GameObject> spawnedIcons = new List<GameObject>();

    private Transform mainCamera;

    private void Start()
    {
        // Safety check to prevent the infinite loop crash
        if (compassTextPrefab != null && compassTextPrefab.gameObject == this.gameObject)
        {
            Debug.LogError("🚨 COMPASS ERROR: Script is on the Prefab! Move script to CompassPanel.");
            return;
        }

        mainCamera = Camera.main.transform;

        if (compassTextPrefab == null || compassPanel == null)
        {
            Debug.LogError("🚨 COMPASS ERROR: Missing UI references in Inspector!");
            return;
        }

        // Create markers (N, 15, 30, 45, E...)
        for (int i = 0; i < 360; i += 15)
        {
            // IMPORTANT: 'false' ensures the UI element scales correctly relative to the parent
            TextMeshProUGUI newMark = Instantiate(compassTextPrefab, compassPanel, false);
            newMark.text = GetCompassMark(i);
            newMark.alignment = TextAlignmentOptions.Center;

            RectTransform rt = newMark.GetComponent<RectTransform>();
            rt.localScale = Vector3.one; // Force scale to 1 to avoid invisible text
            rt.anchoredPosition = Vector3.zero;

            compassMarks.Add(rt);
        }

        // Hide the original template
        compassTextPrefab.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (mainCamera == null || compassMarks.Count == 0) return;

        float camAngle = mainCamera.eulerAngles.y;

        for (int i = 0; i < compassMarks.Count; i++)
        {
            float markAngle = i * 15f;
            float angleDiff = Mathf.DeltaAngle(camAngle, markAngle);

            // Calculate exact pixel position
            float xPos = angleDiff * pixelsPerDegree;
            compassMarks[i].anchoredPosition = new Vector2(xPos, 0f);

            // Hide marks that are outside the view to save performance
            compassMarks[i].gameObject.SetActive(Mathf.Abs(angleDiff) <= compassViewAngle);
        }

        UpdateMarkers(camAngle);
    }

    private string GetCompassMark(int angle)
    {
        if (angle == 0) return "<color=#FF5555>N</color>";
        if (angle == 90) return "<color=#55FF55>E</color>";
        if (angle == 180) return "<color=#5555FF>S</color>";
        if (angle == 270) return "<color=#FFFF55>W</color>";
        return angle.ToString();
    }

    private void UpdateMarkers(float camAngle)
    {
        if (markersParent == null || markerIconPrefab == null) return;

        activeMarkers.RemoveAll(item => item == null);

        int iconsNeeded = activeMarkers.Count - spawnedIcons.Count;
        for (int i = 0; i < iconsNeeded; i++)
        {
            GameObject icon = Instantiate(markerIconPrefab, markersParent, false);
            icon.GetComponent<RectTransform>().localScale = Vector3.one;
            spawnedIcons.Add(icon);
        }

        int iconsToRemove = spawnedIcons.Count - activeMarkers.Count;
        for (int i = 0; i < iconsToRemove; i++)
        {
            Destroy(spawnedIcons[spawnedIcons.Count - 1]);
            spawnedIcons.RemoveAt(spawnedIcons.Count - 1);
        }

        for (int i = 0; i < activeMarkers.Count; i++)
        {
            Vector3 dirToTarget = activeMarkers[i].position - mainCamera.position;
            // Skip Quaternion.LookRotation+eulerAngles (expensive matrix math)
            // for a single yaw angle that Atan2 gives us directly.
            float targetAngle = Mathf.Atan2(dirToTarget.x, dirToTarget.z) * Mathf.Rad2Deg;
            float angleDiff = Mathf.DeltaAngle(camAngle, targetAngle);

            if (Mathf.Abs(angleDiff) <= compassViewAngle)
            {
                spawnedIcons[i].SetActive(true);
                RectTransform iconRect = spawnedIcons[i].GetComponent<RectTransform>();
                float xPos = angleDiff * pixelsPerDegree;
                iconRect.anchoredPosition = new Vector2(xPos, -20f); // Offset slightly below text
            }
            else
            {
                spawnedIcons[i].SetActive(false);
            }
        }
    }
}