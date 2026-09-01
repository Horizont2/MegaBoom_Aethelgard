using UnityEngine;

// Non-functional annotation dropped on each lore-trailer virtual camera by
// Tools ▸ Lore Trailer ▸ Build Camera Rig. Selecting a camera in the Hierarchy
// shows, right in the Inspector, what the shot is and how the camera should
// behave — copied straight from the shot script so you don't have to keep the
// document open while you position cameras.
[DisallowMultipleComponent]
public class LoreTrailerShotNote : MonoBehaviour
{
    [TextArea(3, 8)] public string note;
}
