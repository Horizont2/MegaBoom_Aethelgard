using UnityEngine;
using UnityEngine.UI;

// Tracks the current quest-giver NPC (any NPC_Dialogue with its questMarker still
// active) and pins its icon to the minimap, edge-clamped when the target is off-screen.
// Mirrors MinimapIconTracker but for a quest NPC instead of the nearest extraction point.
public class MinimapQuestTracker : MonoBehaviour
{
    [Header("References")]
    public RectTransform minimapRect;
    public Image iconImage;
    public Camera minimapCamera;

    [Header("Settings")]
    public float iconMargin = 10f;
    public float refreshInterval = 1f;

    private Transform player;
    private RectTransform iconRect;
    private float minimapRadius;
    private NPC_Dialogue targetNPC;
    private float nextRefreshTime;

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        iconRect = GetComponent<RectTransform>();

        if (minimapRect != null)
            minimapRadius = (minimapRect.sizeDelta.x / 2f) - iconMargin;

        if (minimapCamera == null)
        {
            MinimapCamera mc = FindFirstObjectByType<MinimapCamera>();
            if (mc != null) minimapCamera = mc.GetComponent<Camera>();
        }

        RefreshTarget();
    }

    private void LateUpdate()
    {
        if (player == null || minimapCamera == null || iconImage == null) return;

        if (Time.time >= nextRefreshTime)
        {
            RefreshTarget();
            nextRefreshTime = Time.time + refreshInterval;
        }

        if (targetNPC == null || !targetNPC.HasActiveQuestMarker)
        {
            iconImage.enabled = false;
            return;
        }

        iconImage.enabled = true;

        Vector3 relPos = targetNPC.transform.position - player.position;

        float unitsInView;
        if (minimapCamera.orthographic)
        {
            unitsInView = minimapCamera.orthographicSize * 2f;
        }
        else
        {
            unitsInView = 2f * minimapCamera.transform.position.y * Mathf.Tan(minimapCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        float pixelsPerMeter = minimapRect.sizeDelta.x / unitsInView;
        Vector2 uiPos = new Vector2(relPos.x, relPos.z) * pixelsPerMeter;

        // Clamp to the minimap edge so the player always sees which direction to head.
        if (uiPos.magnitude > minimapRadius)
            uiPos = uiPos.normalized * minimapRadius;

        iconRect.anchoredPosition = uiPos;
    }

    private void RefreshTarget()
    {
        if (targetNPC != null && targetNPC.HasActiveQuestMarker) return;

        NPC_Dialogue[] npcs = FindObjectsByType<NPC_Dialogue>(FindObjectsSortMode.None);
        foreach (NPC_Dialogue npc in npcs)
        {
            if (npc != null && npc.HasActiveQuestMarker)
            {
                targetNPC = npc;
                return;
            }
        }
        targetNPC = null;
    }
}
