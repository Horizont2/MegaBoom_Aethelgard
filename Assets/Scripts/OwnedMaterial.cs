using UnityEngine;

// Ties a runtime-created Material to the lifetime of a GameObject.
//
// ==== WHY THIS KEEPS BEING NEEDED ====
//
// A Material built in script is an ENGINE-side object. The garbage collector
// never touches it, and — the part that catches everyone — destroying the
// GameObject it was assigned to does not free it either. Unity only auto-frees
// a material it instantiated itself, which is the one you get from reading
// `.material`; one you constructed and assigned outlives the renderer and stays
// resident until the process exits.
//
// So `new Material(...)` followed by `Destroy(go)` is a leak, and it is a leak
// that looks exactly like correct code. This project had it in a dozen places,
// several of them on per-kill or per-shot paths.
//
// Attach this and the material goes when the object does:
//
//     OwnedMaterial.Attach(go, mat);
//
// Use it for one-off effects. For anything spawned repeatedly with the SAME
// material, cache the material statically and share it instead — that is
// strictly better than creating and freeing one per instance.
[DisallowMultipleComponent]
public class OwnedMaterial : MonoBehaviour
{
    private Material[] _owned;
    private int _count;

    public static void Attach(GameObject host, params Material[] mats)
    {
        if (host == null || mats == null || mats.Length == 0) return;

        var owner = host.GetComponent<OwnedMaterial>();
        if (owner == null) owner = host.AddComponent<OwnedMaterial>();
        owner.Add(mats);
    }

    private void Add(Material[] mats)
    {
        if (_owned == null) _owned = new Material[mats.Length];
        foreach (var m in mats)
        {
            if (m == null) continue;
            if (_count == _owned.Length) System.Array.Resize(ref _owned, _count * 2);
            _owned[_count++] = m;
        }
    }

    private void OnDestroy()
    {
        if (_owned == null) return;
        for (int i = 0; i < _count; i++)
            if (_owned[i] != null) Destroy(_owned[i]);
        _owned = null;
        _count = 0;
    }
}
