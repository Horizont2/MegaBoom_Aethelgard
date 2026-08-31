using UnityEngine;

// Runtime rescue for prefabs whose material shader isn't in the build — a
// BiRP/Standard shader dragged into a URP project, or a shader stripped from
// the build — which Unity replaces with the pink Hidden/InternalErrorShader
// (the classic "everything is magenta" bug). Scans an object's renderers and
// re-homes any error-shader material onto URP/Lit, salvaging the old base
// color / texture where possible.
public static class ShaderRepair
{
    private static Shader s_lit;
    private static Shader Lit => s_lit != null
        ? s_lit
        : (s_lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

    public static void Fix(GameObject root)
    {
        if (root == null || Lit == null) return;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        for (int ri = 0; ri < rends.Length; ri++)
        {
            var r = rends[ri];
            if (r == null) continue;
            var mats = r.materials;   // instanced copies — safe to reassign
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m != null && m.shader != null && m.shader.name != "Hidden/InternalErrorShader")
                    continue;

                // Salvage whatever color/texture the broken material still carries.
                Color c = (m != null && m.HasProperty("_Color")) ? m.color : new Color(0.78f, 0.75f, 0.68f);
                Texture tex = (m != null && m.HasProperty("_MainTex")) ? m.mainTexture : null;

                var nm = new Material(Lit);
                if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", c); else nm.color = c;
                if (tex != null && nm.HasProperty("_BaseMap")) nm.SetTexture("_BaseMap", tex);
                mats[i] = nm;
                changed = true;
            }
            if (changed) r.materials = mats;
        }
    }
}
