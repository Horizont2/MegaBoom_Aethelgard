using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Breaks a SKINNED character (the skeletons) into physical chunks the instant it
// dies: bakes the current posed mesh, splits its triangles into groups by their
// dominant bone (so it comes apart along skull / ribcage / limbs), spawns each
// group as a rigidbody chunk and blows them outward from the death point. The
// pieces tumble, land on the terrain and then fade away.
public static class SkeletonShatter
{
    // Returns true if it shattered at least one skinned mesh.
    public static bool Shatter(GameObject root, Vector3 explosionCenter,
                               float force = 5f, float chunkLife = 3.5f, int maxChunks = 14)
    {
        if (root == null) return false;

        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        if (smrs == null || smrs.Length == 0) return false;

        var container = new GameObject("ShatterChunks");
        container.transform.position = root.transform.position;

        bool any = false;
        foreach (var smr in smrs)
        {
            if (smr == null || smr.sharedMesh == null || !smr.enabled) continue;
            if (BuildChunks(smr, container.transform, explosionCenter, force, maxChunks)) any = true;
            smr.enabled = false;   // hide the intact mesh — the chunks replace it
        }

        if (!any) { Object.Destroy(container); return false; }

        var cleanup = container.AddComponent<ShatterCleanup>();
        cleanup.life = chunkLife;
        return true;
    }

    private static bool BuildChunks(SkinnedMeshRenderer smr, Transform parent,
                                    Vector3 center, float force, int maxChunks)
    {
        // Bake the CURRENT pose into a static mesh (renderer-local space).
        Mesh baked = new Mesh();
        smr.BakeMesh(baked, false);

        Vector3[] verts = baked.vertices;
        Vector3[] normals = baked.normals;
        Vector2[] uvs = baked.uv;
        int[] tris = baked.triangles;
        BoneWeight[] weights = smr.sharedMesh.boneWeights;   // same vertex order as baked
        int boneCount = Mathf.Max(1, smr.bones != null ? smr.bones.Length : 1);
        if (tris.Length < 3) return false;

        // Bucket bones so we cap the number of chunks (many bones -> a few limbs).
        int bucketDiv = Mathf.Max(1, Mathf.CeilToInt(boneCount / (float)maxChunks));

        // Group triangles by the dominant bone of their first vertex.
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int v0 = tris[t];
            int bone = (weights != null && v0 < weights.Length) ? weights[v0].boneIndex0 : 0;
            int bucket = bone / bucketDiv;
            if (!groups.TryGetValue(bucket, out var list)) { list = new List<int>(64); groups[bucket] = list; }
            list.Add(tris[t]); list.Add(tris[t + 1]); list.Add(tris[t + 2]);
        }

        Transform rt = smr.transform;
        Material mat = smr.sharedMaterial;
        int layer = rt.gameObject.layer;
        bool madeAny = false;

        foreach (var kv in groups)
        {
            var triList = kv.Value;
            if (triList.Count < 3) continue;

            // Remap the used vertices into a compact submesh.
            var map = new Dictionary<int, int>(triList.Count);
            var nv = new List<Vector3>(); var nn = new List<Vector3>();
            var nu = new List<Vector2>(); var nt = new List<int>(triList.Count);
            foreach (int vi in triList)
            {
                if (!map.TryGetValue(vi, out int ni))
                {
                    ni = nv.Count; map[vi] = ni;
                    nv.Add(verts[vi]);
                    nn.Add(vi < normals.Length ? normals[vi] : Vector3.up);
                    nu.Add(vi < uvs.Length ? uvs[vi] : Vector2.zero);
                }
                nt.Add(ni);
            }
            if (nv.Count < 3) continue;

            var m = new Mesh();
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0);
            m.RecalculateBounds();

            var go = new GameObject("Chunk");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(rt.position, rt.rotation);
            go.transform.localScale = rt.lossyScale;
            go.layer = layer;

            go.AddComponent<MeshFilter>().mesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var box = go.AddComponent<BoxCollider>();
            box.center = m.bounds.center;
            box.size = Vector3.Max(m.bounds.size, Vector3.one * 0.05f);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // Gentle nudge outward — pieces separate and drop rather than launch.
            rb.AddExplosionForce(force, center, 3.5f, 0.35f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * force * 0.25f, ForceMode.Impulse);

            madeAny = true;
        }

        return madeAny;
    }
}

// Fades and cleans up the shatter chunks after they've had time to scatter/land.
public class ShatterCleanup : MonoBehaviour
{
    public float life = 3.5f;

    private void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(life);

        // Sink + shrink the pieces, then remove them.
        var chunks = GetComponentsInChildren<Transform>(true);
        float t = 0f; const float dur = 0.8f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - (t / dur);
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == null || chunks[i] == transform) continue;
                chunks[i].localScale *= 0.985f;
                chunks[i].position += Vector3.down * 0.4f * Time.deltaTime;
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}
