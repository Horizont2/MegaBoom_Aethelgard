using UnityEngine;
using UnityEngine.UI; // ����'������ ��� ������ � Image!

public class BuildingIndicator : MonoBehaviour
{
    [Header("Settings")]
    public float visibleDistance = 20f; // � ��� ������� ������ �'��������

    [Tooltip("�������� ����� ����� �������� �� ��'��� (1 = �������� �� ������, 0.5 = ����� ���� ���)")]
    [Range(0.1f, 1f)]
    public float lookThreshold = 0.7f;

    private Image iconImage;
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
        iconImage = GetComponent<Image>();

        // ������ ������ ��������� �� �����
        if (iconImage != null)
        {
            Color c = iconImage.color;
            c.a = 0f;
            iconImage.color = c;
        }
    }

    void Update()
    {
        if (mainCam == null || iconImage == null) return;

        // Cache camera transform reads — every property access on the
        // Transform component goes through a native marshal boundary.
        Transform camT = mainCam.transform;
        Vector3 camPos = camT.position;
        Vector3 camFwd = camT.forward;

        // Squared-distance range check first — avoids the sqrt in
        // Vector3.Distance until we know we're actually close enough
        // to potentially render the icon. Applied per-indicator per-
        // frame, so it stacks with N buildings.
        Vector3 delta = transform.position - camPos;
        float sqrDist = delta.sqrMagnitude;
        float visSqr = visibleDistance * visibleDistance;

        float targetAlpha = 0f;
        if (sqrDist <= visSqr)
        {
            float dist = Mathf.Sqrt(sqrDist);
            // Dot product with the un-normalised delta / dist — one
            // division instead of a full Vector3.Normalize().
            float lookDot = Vector3.Dot(camFwd, delta / dist);
            if (lookDot >= lookThreshold)
            {
                float distAlpha = Mathf.Clamp01((visibleDistance - dist) / 5f);
                float lookAlpha = Mathf.Clamp01((lookDot - lookThreshold) / (1f - lookThreshold));
                targetAlpha = Mathf.Min(distAlpha, lookAlpha);
            }
        }

        // Smoothed alpha — only write to the Image when it actually
        // changes to skip a redundant Canvas dirty.
        Color c = iconImage.color;
        float newA = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 8f);
        if (Mathf.Abs(newA - c.a) > 0.005f)
        {
            c.a = newA;
            iconImage.color = c;
        }

        // Face-the-camera billboard. LookAt is fine here; the cost is
        // trivial next to the Canvas rebuild the color write triggers.
        // Using cached camT to avoid a second Component-property fetch.
        Quaternion camRot = camT.rotation;
        transform.LookAt(transform.position + camRot * Vector3.forward, camRot * Vector3.up);
    }
}