using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TotemMinimapTracker : MonoBehaviour
{
    [Header("References")]
    public RectTransform minimapRect;
    public GameObject totemIconPrefab;
    public Camera minimapCamera;

    [Header("Settings")]
    public float iconMargin = 12f;

    private Transform player;
    private float minimapRadius;

    private Dictionary<RegionTotem, RectTransform> totemIcons = new Dictionary<RegionTotem, RectTransform>();

    // ФІКС ОПТИМІЗАЦІЇ: Кешуємо тотеми
    private RegionTotem[] cachedTotems;
    private float searchTimer = 0f;

    private void Start()
    {
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;

        if (minimapCamera == null)
        {
            MinimapCamera camScript = FindFirstObjectByType<MinimapCamera>();
            if (camScript != null) minimapCamera = camScript.GetComponent<Camera>();
        }
    }

    private void LateUpdate()
    {
        if (player == null || minimapCamera == null || minimapRect == null || totemIconPrefab == null) return;

        // ОПТИМІЗАЦІЯ: Шукаємо тотеми раз на секунду, а не 100 разів на секунду
        if (cachedTotems == null || cachedTotems.Length == 0)
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0f)
            {
                cachedTotems = FindObjectsByType<RegionTotem>(FindObjectsSortMode.None);
                searchTimer = 1f;
            }
            if (cachedTotems == null || cachedTotems.Length == 0) return;
        }

        // ФІКС МАПИ: Використовуємо rect.width замість sizeDelta, щоб ігнорувати якір Маски
        float mapWidth = minimapRect.rect.width;
        if (mapWidth == 0) mapWidth = 200f; // Запобіжник
        minimapRadius = (mapWidth / 2f) - iconMargin;

        float unitsInView;
        if (minimapCamera.orthographic)
        {
            unitsInView = minimapCamera.orthographicSize * 2f;
        }
        else
        {
            unitsInView = 2f * minimapCamera.transform.position.y * Mathf.Tan(minimapCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        float pixelsPerMeter = mapWidth / unitsInView;

        foreach (var totem in cachedTotems)
        {
            if (totem == null) continue;

            bool shouldShow = !totem.isActivated && !totem.isPurified;

            if (!shouldShow)
            {
                if (totemIcons.ContainsKey(totem))
                {
                    if (totemIcons[totem] != null) Destroy(totemIcons[totem].gameObject);
                    totemIcons.Remove(totem);
                }
                continue;
            }

            if (!totemIcons.ContainsKey(totem))
            {
                GameObject newIcon = Instantiate(totemIconPrefab, minimapRect);
                RectTransform rt = newIcon.GetComponent<RectTransform>();

                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;

                totemIcons[totem] = rt;
            }

            RectTransform iconRect = totemIcons[totem];
            Vector3 relPos = totem.transform.position - player.position;
            Vector2 uiPos = new Vector2(relPos.x, relPos.z) * pixelsPerMeter;

            if (uiPos.magnitude > minimapRadius)
            {
                uiPos = uiPos.normalized * minimapRadius;
            }

            iconRect.anchoredPosition = uiPos;
        }
    }
}