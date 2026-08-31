using UnityEngine;

[RequireComponent(typeof(Light))]
public class GlimmerSweep : MonoBehaviour
{
    [Header("Sweep Points")]
    public Transform startPoint; // ����� ������
    public Transform endPoint;   // ���� ������

    [Header("Settings")]
    public float speed = 0.5f;   // �������� ��������
    public float maxIntensity = 50f; // ����������� ��������� �� ������

    private Light glimmerLight;
    private float progress = 0f;

    private void Awake()
    {
        glimmerLight = GetComponent<Light>();
    }

    private void OnEnable()
    {
        // ������� ������� ������, ���� ����� ���������
        progress = 0f;
    }

    private void Update()
    {
        // Advance the sweep progress unconditionally. The old code early-returned
        // when startPoint/endPoint were unwired — so on any building whose glimmer
        // prefab lacked those references (the lumberjack, per report) the light
        // just sat frozen. Now the pulse ALWAYS animates; the positional sweep is
        // the only part that needs the points.
        progress += Time.deltaTime * Mathf.Max(0.01f, speed);
        if (progress > 1f) progress -= 1f;

        if (startPoint != null && endPoint != null)
            transform.position = Vector3.Lerp(startPoint.position, endPoint.position, progress);

        // Sin(progress * PI): 0 at the ends, 1 in the middle — a soft breathing glow.
        if (glimmerLight != null)
            glimmerLight.intensity = Mathf.Sin(progress * Mathf.PI) * maxIntensity;
    }
}