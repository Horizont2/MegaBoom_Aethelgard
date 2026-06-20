using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MapInteractiveViewer : MonoBehaviour, IDragHandler, IScrollHandler
{
    private RectTransform mapRect;
    private RectTransform parentViewport;

    [Header("Zoom Settings")]
    public float zoomSpeed = 0.5f;
    public float minZoom = 1.0f;
    public float maxZoom = 4f;
    public float smoothZoomSpeed = 10f;
    private float targetZoom = 1.2f;

    [Header("Drag Settings")]
    public float smoothDragSpeed = 15f;
    private Vector2 targetPosition;

    [Header("Parallax Effect")]
    public RectTransform parallaxLayer;
    public float parallaxStrength = 0.12f;

    private void Awake()
    {
        mapRect = GetComponent<RectTransform>();
        parentViewport = transform.parent.GetComponent<RectTransform>();
        targetPosition = mapRect.anchoredPosition;

        // Drag/scroll handlers need a Graphic with raycastTarget on the same
        // GameObject — otherwise the pointer can fall through gaps between
        // region buttons and dragging fails to start. Add a transparent
        // Image as a safety net if no Graphic is wired.
        Graphic existing = GetComponent<Graphic>();
        if (existing == null)
        {
            Image img = gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
        }
        else if (!existing.raycastTarget)
        {
            existing.raycastTarget = true;
        }
    }

    private void OnEnable()
    {
        MapTableInteract.OnMapFullyOpened += TriggerAutoFocus;
    }

    private void OnDisable()
    {
        MapTableInteract.OnMapFullyOpened -= TriggerAutoFocus;
    }

    private void TriggerAutoFocus()
    {
        StopAllCoroutines();
        StartCoroutine(DelayedAutoFocus());
    }

    private IEnumerator DelayedAutoFocus()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        AutoFocusOnLatestRegion(false);
    }

    private void Update()
    {
        // 1. ����̲���� ̲Ͳ������� ��� (�� ���������� ��� ���� ������ �� �����)
        float viewWidth = parentViewport.rect.width;
        float viewHeight = parentViewport.rect.height;
        float minZoomX = viewWidth / mapRect.rect.width;
        float minZoomY = viewHeight / mapRect.rect.height;
        float dynamicMinZoom = Mathf.Max(minZoom, Mathf.Max(minZoomX, minZoomY));

        targetZoom = Mathf.Clamp(targetZoom, dynamicMinZoom, maxZoom);

        // 2. ��������� ֲ����ί ����ֲ� (Target Position)
        // ����������� ��� ��� ֲ������� ����, ��� ��� ��������� ���� ������ ����������� �� ������
        float targetMapWidth = mapRect.rect.width * targetZoom;
        float targetMapHeight = mapRect.rect.height * targetZoom;
        float maxTargetX = Mathf.Max(0, (targetMapWidth - viewWidth) / 2f);
        float maxTargetY = Mathf.Max(0, (targetMapHeight - viewHeight) / 2f);

        targetPosition.x = Mathf.Clamp(targetPosition.x, -maxTargetX, maxTargetX);
        targetPosition.y = Mathf.Clamp(targetPosition.y, -maxTargetY, maxTargetY);

        // 3. ������� ��� �� ���
        float currentZoom = Mathf.Lerp(mapRect.localScale.x, targetZoom, Time.unscaledDeltaTime * smoothZoomSpeed);
        mapRect.localScale = new Vector3(currentZoom, currentZoom, 1f);

        mapRect.anchoredPosition = Vector2.Lerp(mapRect.anchoredPosition, targetPosition, Time.unscaledDeltaTime * smoothDragSpeed);

        // 4. ������� ��������� �������ί ����ֲ� (Current Position)
        // ��� ����� �� ��� ����������� ������ ��� ���� �� "���������" �� ������ � �� ���������� �����
        float currentMapWidth = mapRect.rect.width * currentZoom;
        float currentMapHeight = mapRect.rect.height * currentZoom;
        float maxCurrentX = Mathf.Max(0, (currentMapWidth - viewWidth) / 2f);
        float maxCurrentY = Mathf.Max(0, (currentMapHeight - viewHeight) / 2f);

        Vector2 clampedPos = mapRect.anchoredPosition;
        clampedPos.x = Mathf.Clamp(clampedPos.x, -maxCurrentX, maxCurrentX);
        clampedPos.y = Mathf.Clamp(clampedPos.y, -maxCurrentY, maxCurrentY);
        mapRect.anchoredPosition = clampedPos;

        // 5. ��������
        if (parallaxLayer != null)
        {
            parallaxLayer.anchoredPosition = mapRect.anchoredPosition * parallaxStrength;
            parallaxLayer.localScale = Vector3.one * (1f + (currentZoom - 1f) * (parallaxStrength / 2f));
        }
    }

    public void OnScroll(PointerEventData eventData)
    {
        float scrollDelta = eventData.scrollDelta.y;
        targetZoom += scrollDelta * zoomSpeed;
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentViewport, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentViewport, eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 prevLocalPoint);

        targetPosition += (localPoint - prevLocalPoint);
    }

    public void FocusOnNode(RectTransform nodeRect, bool instant = true)
    {
        targetZoom = Mathf.Clamp(1.7f, minZoom, maxZoom);

        Vector2 nodeLocalPos = mapRect.InverseTransformPoint(nodeRect.position);
        targetPosition = -nodeLocalPos * targetZoom;

        if (instant)
        {
            mapRect.localScale = new Vector3(targetZoom, targetZoom, 1f);
            mapRect.anchoredPosition = targetPosition;
            // ��������� ���� Update() ��� ����������� �������� ������� ������ �� �������� ����������
        }
    }

    public void AutoFocusOnLatestRegion(bool instant = true)
    {
        RegionUI[] allRegions = FindObjectsByType<RegionUI>(FindObjectsSortMode.None);
        if (allRegions.Length == 0) return;

        RegionUI targetNode = null;

        var availableRegions = allRegions
            .Where(r => r.myRegionData != null && r.myRegionData.currentState == RegionState.Available)
            .OrderByDescending(r => r.myRegionData.regionID)
            .ToList();

        if (availableRegions.Count > 0)
        {
            targetNode = availableRegions.FirstOrDefault(r => r.myRegionData.isNewlyUnlocked) ?? availableRegions[0];
        }
        else
        {
            targetNode = allRegions
                .Where(r => r.myRegionData != null && r.myRegionData.currentState == RegionState.Conquered)
                .OrderByDescending(r => r.myRegionData.regionID)
                .FirstOrDefault();
        }

        if (targetNode != null)
        {
            FocusOnNode(targetNode.GetComponent<RectTransform>(), instant);
        }
    }
}