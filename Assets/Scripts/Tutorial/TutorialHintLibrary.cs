using UnityEngine;

[CreateAssetMenu(fileName = "TutorialHintLibrary", menuName = "Tutorial/Hint Library")]
public class TutorialHintLibrary : ScriptableObject
{
    [Tooltip("Усі TutorialHintData, які знає гра. Перетягни сюди усі хінт-ассети.")]
    public TutorialHintData[] hints;

    public TutorialHintData FindByKey(string key)
    {
        if (hints == null || string.IsNullOrEmpty(key)) return null;
        for (int i = 0; i < hints.Length; i++)
        {
            TutorialHintData d = hints[i];
            if (d != null && d.key == key) return d;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (hints == null) return;
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < hints.Length; i++)
        {
            TutorialHintData d = hints[i];
            if (d == null) continue;
            if (string.IsNullOrEmpty(d.key))
            {
                Debug.LogWarning($"[TutorialHintLibrary] Entry #{i} ({d.name}) has empty key.", d);
                continue;
            }
            if (!seen.Add(d.key))
                Debug.LogWarning($"[TutorialHintLibrary] Duplicate key '{d.key}' on {d.name}.", d);
        }
    }
#endif
}
