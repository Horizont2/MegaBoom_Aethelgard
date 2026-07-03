using UnityEngine;

public class MinimapCamera : MonoBehaviour
{
    public Transform player;
    public float cameraHeight = 50f;
    [Tooltip("Чи обертати карту разом з гравцем (AAA стиль)")]
    public bool rotateWithPlayer = true;

    private void Start()
    {
        transform.parent = null;
    }

    private void LateUpdate()
    {
        if (player != null)
        {
            // Камера просто висить над гравцем
            transform.position = new Vector3(player.position.x, player.position.y + cameraHeight, player.position.z);

            // Завжди дивиться вниз (Північ зверху)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}