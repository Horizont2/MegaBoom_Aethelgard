using UnityEngine;
using UnityEngine.UI;

public class MinimapIconTracker : MonoBehaviour
{
    [Header("References")]
    public RectTransform minimapRect; // ��� MinimapBase (220x220)
    public Image iconImage;
    public Camera minimapCamera; // ��������� ���� ������ �������

    [Header("Settings")]
    public float iconMargin = 10f;

    private Transform player;
    private RectTransform iconRect;
    private float minimapRadius;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").transform;
        iconRect = GetComponent<RectTransform>();

        if (minimapRect != null)
            minimapRadius = (minimapRect.sizeDelta.x / 2f) - iconMargin;

        if (minimapCamera == null)
            minimapCamera = FindFirstObjectByType<MinimapCamera>().GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (player == null || minimapCamera == null) return;

        GameObject nearestHorse = FindNearestExtractionPoint();
        if (nearestHorse == null)
        {
            iconImage.enabled = false;
            return;
        }

        iconImage.enabled = true;

        // 1. �������� ������� ������� ���� �� ������
        Vector3 relPos = nearestHorse.transform.position - player.position;

        // 2. ��ò� ������Ҳ: ���������� �������� ������� �� ����� ������
        float unitsInView;
        if (minimapCamera.orthographic)
        {
            unitsInView = minimapCamera.orthographicSize * 2f;
        }
        else
        {
            // ��� Perspective ������ �� ����� 40
            unitsInView = 2f * minimapCamera.transform.position.y * Mathf.Tan(minimapCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        // ������ ������ UI ������� �� 1 ������� ����
        float pixelsPerMeter = minimapRect.sizeDelta.x / unitsInView;

        // 3. ������ �������� �������
        Vector2 uiPos = new Vector2(relPos.x, relPos.z) * pixelsPerMeter;

        // 4. �������� �����, ���� ��'��� �� ������ ��������
        if (uiPos.magnitude > minimapRadius)
        {
            uiPos = uiPos.normalized * minimapRadius;
        }

        iconRect.anchoredPosition = uiPos;
    }

    // Cache the extraction-point list — refreshed every ~1s instead of
    // every frame. FindGameObjectsWithTag allocates a new array each call
    // and this ran in LateUpdate on every frame the minimap was up.
    private GameObject[] cachedPoints;
    private float pointsRefreshTimer = 0f;
    private const float POINTS_REFRESH_INTERVAL = 1.0f;

    private GameObject FindNearestExtractionPoint()
    {
        pointsRefreshTimer -= Time.deltaTime;
        if (cachedPoints == null || pointsRefreshTimer <= 0f)
        {
            cachedPoints = GameObject.FindGameObjectsWithTag("ExtractionPoint");
            pointsRefreshTimer = POINTS_REFRESH_INTERVAL;
        }

        GameObject nearest = null;
        float minSqr = float.PositiveInfinity;
        for (int i = 0; i < cachedPoints.Length; i++)
        {
            var p = cachedPoints[i];
            if (p == null) continue; // pool destroyed since last refresh
            float sqr = (player.position - p.transform.position).sqrMagnitude;
            if (sqr < minSqr) { minSqr = sqr; nearest = p; }
        }
        return nearest;
    }
}