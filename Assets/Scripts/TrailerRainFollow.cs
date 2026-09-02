using UnityEngine;

// Keeps a rain (or snow) particle volume centred above the camera so the weather
// always fills frame during a moving trailer shot, while staying world-vertical
// so the rain falls straight down regardless of where the camera looks.
public class TrailerRainFollow : MonoBehaviour
{
    public Transform target;      // usually the Main Camera
    public float height = 12f;    // how far above the camera the rain volume sits
    public bool keepVertical = true;

    private void LateUpdate()
    {
        if (target == null)
        {
            var c = Camera.main;
            if (c != null) target = c.transform;
            if (target == null) return;
        }

        Vector3 p = target.position;
        p.y += height;
        transform.position = p;
        if (keepVertical) transform.rotation = Quaternion.identity;
    }
}
