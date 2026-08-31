using UnityEngine;
using UnityEngine.SceneManagement;

// Self-bootstrapping safety net that keeps magenta (missing-shader) PARTICLE
// systems off the screen — the summoner's remains kept showing purple particles
// that made trailer recording impossible. It re-homes any broken particle
// material onto a valid particle shader (or disables it as a last resort) on
// scene load and on a slow interval, so newly-spawned enemies get caught too.
// Cost is trivial: it only touches renderers that are actually broken, and once
// repaired they're never magenta again.
public class MagentaParticleSweeper : MonoBehaviour
{
    private const float Interval = 1.5f;
    private float nextSweep;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<MagentaParticleSweeper>() != null) return;
        var go = new GameObject("[MagentaParticleSweeper]");
        DontDestroyOnLoad(go);
        go.AddComponent<MagentaParticleSweeper>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ShaderRepair.SweepParticles();
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene s, LoadSceneMode m) => ShaderRepair.SweepParticles();

    private void Update()
    {
        if (Time.unscaledTime < nextSweep) return;
        nextSweep = Time.unscaledTime + Interval;
        ShaderRepair.SweepParticles();
    }
}
