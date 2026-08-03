#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Collections.Generic;
using System.Text;

// Fails the build if a scene that shouldn't ship is enabled in Build
// Settings. Currently guards against dev / prototype scenes leaking into
// a Steam ship build — anything whose file name contains "Test",
// "Prototype", "Sandbox", or the location scaffolding (Location_1..N)
// that hasn't been folded into the region set.
//
// Also warns (but does not fail) if less than one scene is enabled,
// which almost always means someone unchecked the entry scene by
// mistake.
public class BuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private static readonly string[] BANNED_SUBSTRINGS =
    {
        "Test",
        "test_",
        "Prototype",
        "Sandbox",
        "Location_1",
        "Location_2",
        "Location_3",
        "CharactersTest",
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        var enabled = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) enabled.Add(s.path);

        if (enabled.Count == 0)
            throw new BuildFailedException("[BuildValidator] No scenes enabled in Build Settings — nothing to ship.");

        var bad = new List<string>();
        foreach (var scenePath in enabled)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            foreach (var banned in BANNED_SUBSTRINGS)
            {
                if (name.Contains(banned))
                {
                    bad.Add($"{scenePath}  (matches '{banned}')");
                    break;
                }
            }
        }

        if (bad.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[BuildValidator] Refusing to build — the following dev/test scenes are enabled in Build Settings:");
            foreach (var s in bad) sb.AppendLine("  • " + s);
            sb.AppendLine();
            sb.AppendLine("Uncheck them under File → Build Settings → Scenes In Build, then re-run the build.");
            sb.AppendLine("If you actually need one shipped, rename it or update BuildValidator.BANNED_SUBSTRINGS.");
            throw new BuildFailedException(sb.ToString());
        }

        UnityEngine.Debug.Log($"[BuildValidator] OK — {enabled.Count} scene(s) cleared.");
    }
}
#endif
