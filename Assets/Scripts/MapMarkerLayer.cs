using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws every revealed MapEventMarker onto the minimap.
//
// It borrows the minimap's geometry from the tracker that is already wired to it
// rather than asking for its own references. That is deliberate: this has to
// work in a scene nobody prepared for it, and a component that needs three
// inspector fields filled in is a component that is silently doing nothing in
// half the scenes it lives in — which is exactly how the exploration markers
// would have gone missing the same way the reliquaries did.
//
// Icons are pooled. Markers appear and vanish as the player moves and as chests
// are looted, and creating a UI object per marker per frame would show up in a
// profile long before it showed up as a bug.
[DisallowMultipleComponent]
public class MapMarkerLayer : MonoBehaviour
{
    private RectTransform _map;
    private Camera _cam;
    private Transform _player;
    private MapEventIcons _set;
    private float _radius;

    private readonly List<Image> _pool = new List<Image>(16);
    private float _rebind;

    // ==== RE-INSTALLED PER SCENE, NOT ONCE PER SESSION ====
    //
    // RuntimeInitializeOnLoadMethod fires exactly ONCE, in whatever scene the
    // session starts in — the boot logo. The object it made is
    // DontDestroyOnLoad, so in principle it survives; in practice this project
    // has now lost four separate features to that assumption, because anything
    // that clears persistent objects between scenes takes it with no second
    // chance and no log line.
    //
    // Subscribing to sceneLoaded as well costs nothing and removes the whole
    // class of failure. Same pattern PlayerBlock uses.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
        Install();
    }

    private static void OnScene(UnityEngine.SceneManagement.Scene s,
                                UnityEngine.SceneManagement.LoadSceneMode m) => Install();

    private static void Install()
    {
        if (FindFirstObjectByType<MapMarkerLayer>() != null) return;
        var go = new GameObject("[MapMarkers]");
        DontDestroyOnLoad(go);
        go.AddComponent<MapMarkerLayer>();
        Debug.Log("[MapIcons] Marker layer installed.");
    }

    private void LateUpdate()
    {
        if (!Bind()) { HideAll(); return; }
        if (MapEventMarker.All.Count == 0)
        {
            // Not a fault: the camp has no events in it. Only worth a word in a
            // region, where an empty list means nothing registered a marker.
            HideAll();
            return;
        }

        float unitsInView = _cam.orthographic
            ? _cam.orthographicSize * 2f
            : 2f * _cam.transform.position.y * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (unitsInView <= 0.01f) { HideAll(); return; }

        float pixelsPerMetre = _mapWidth / unitsInView;
        int used = 0;

        for (int i = 0; i < MapEventMarker.All.Count; i++)
        {
            var m = MapEventMarker.All[i];
            if (m == null || (m.done && m.hideWhenDone)) continue;

            var entry = _set.For(m.kind);
            if (entry == null || entry.revealRadius <= 0f) continue;
            // No sprite means an Image with a null source, which Unity draws as
            // a solid white rectangle — worse than showing nothing, and it looks
            // like a broken UI rather than a missing assignment.
            if (entry.icon == null) { _missingIcon |= 1 << (int)m.kind; continue; }

            float reveal = m.RevealRadius(_set);
            Vector3 rel = m.transform.position - _player.position;
            float dist = new Vector2(rel.x, rel.z).magnitude;

            // Fades in across the last few metres of the reveal band. A marker
            // that pops on at an exact distance reads as a glitch the first few
            // times you see it happen.
            float alpha;
            if (dist <= reveal - entry.fadeBand) alpha = 1f;
            else if (dist <= reveal) alpha = Mathf.InverseLerp(reveal, reveal - entry.fadeBand, dist);
            else alpha = 0f;

            if (alpha > 0.02f) m.seen = true;
            else if (m.seen && entry.rememberOnceSeen) alpha = 0.55f;   // remembered, but dimmer than live
            else continue;

            var icon = Rent(used++);
            icon.sprite = entry.icon;
            icon.enabled = true;
            var c = entry.tint; c.a = alpha;
            icon.color = c;

            var rt = icon.rectTransform;
            rt.sizeDelta = new Vector2(entry.size, entry.size);

            Vector2 uiPos = new Vector2(rel.x, rel.z) * pixelsPerMetre;
            // Clamped to the rim, so something outside the minimap's view still
            // tells you which way to walk instead of disappearing.
            if (uiPos.magnitude > _radius) uiPos = uiPos.normalized * _radius;
            rt.anchoredPosition = uiPos;
        }

        for (int i = used; i < _pool.Count; i++) _pool[i].enabled = false;

        Census(used);
    }

    // ==== SAYS WHAT IT IS ACTUALLY DOING, ONCE EVERY FEW SECONDS ====
    //
    // Bind() already explains why nothing is drawn when it cannot find the
    // minimap. The state it could NOT explain is the one that kept happening:
    // bound successfully, markers registered, and still an empty map — because
    // every marker was out of range, or its kind had no sprite, or the only
    // kinds with sprites were kinds nothing in the game ever registers (which
    // was exactly the case for Altar).
    //
    // From the player's seat all of those look identical to "the feature does
    // not work", and each is a ten-second fix once named.
    private float _census;
    private int _missingIcon;

    private void Census(int drawn)
    {
        _census -= Time.unscaledDeltaTime;
        if (_census > 0f) return;
        _census = 5f;

        if (drawn > 0) { _missingIcon = 0; return; }

        if (_missingIcon != 0)
        {
            var names = new List<string>();
            foreach (MapEventIcons.Kind k in System.Enum.GetValues(typeof(MapEventIcons.Kind)))
                if ((_missingIcon & (1 << (int)k)) != 0) names.Add(k.ToString());
            Debug.LogWarning("[MapIcons] Markers exist but have no sprite assigned for: " + string.Join(", ", names) +
                             ". Assign them in Tools > World > Map Event Icons.");
            _missingIcon = 0;
            return;
        }

        // Nothing drawn and nothing missing a sprite: everything registered is
        // simply too far away. Report what exists and how far off it is, so the
        // answer is "walk that way" or "raise the radius" rather than a guess.
        float nearest = float.PositiveInfinity;
        string nearestKind = "none";
        for (int i = 0; i < MapEventMarker.All.Count; i++)
        {
            var m = MapEventMarker.All[i];
            if (m == null) continue;
            Vector3 rel = m.transform.position - _player.position;
            float d = new Vector2(rel.x, rel.z).magnitude;
            if (d < nearest) { nearest = d; nearestKind = m.kind.ToString(); }
        }
        if (MapEventMarker.All.Count > 0)
            Debug.Log($"[MapIcons] {MapEventMarker.All.Count} marker(s) registered, none in range. " +
                      $"Nearest is a {nearestKind} at {nearest:F0}m; its reveal radius decides when it appears.");
    }

    // Re-finds the minimap when the scene changes. Throttled, because the failure
    // mode is a scene with no minimap at all and searching for one every frame in
    // the camp would be pure waste.
    // SAYS WHY IT FAILED, ONCE.
    //
    // Silence was the whole problem with the last three features in this
    // project: markers not appearing looked identical whether the icon set was
    // missing, the minimap could not be found, the player had no tag, or the
    // radius maths produced zero. Each of those is a two-second fix and an
    // afternoon of guessing. The report is throttled to one line per distinct
    // reason so a failure in the camp does not fill the console.
    private string _lastComplaint;

    private bool Fail(string why)
    {
        if (_lastComplaint != why)
        {
            _lastComplaint = why;
            Debug.LogWarning("[MapIcons] No event markers are being drawn: " + why);
        }
        return false;
    }

    private bool Bind()
    {
        if (_map != null && _cam != null && _player != null && _set != null) return true;

        _rebind -= Time.unscaledDeltaTime;
        if (_rebind > 0f) return false;
        _rebind = 1f;

        _set = MapEventIcons.Load();
        if (_set == null)
            return Fail("no MapEventIcons asset in a Resources folder. Create it with Tools > World > Map Event Icons.");

        // The tracker is the preferred source — it already knows the minimap's
        // rect and camera. INCLUDING INACTIVE ones, because it lives on the HUD
        // and a HUD element that happens to be switched off should not take the
        // whole marker layer down with it.
        var host = FindFirstObjectByType<MinimapIconTracker>(FindObjectsInactive.Include);
        _map = host != null ? host.minimapRect : null;

        // No tracker, or one that was never wired: find the minimap directly
        // rather than giving up. This layer only needs a rect and a camera, and
        // both are findable on their own.
        if (_map == null)
        {
            foreach (var rt in FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rt == null) continue;
                string n = rt.name;
                if (n == "MinimapBase" || (n.IndexOf("minimap", System.StringComparison.OrdinalIgnoreCase) >= 0
                                           && n.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0))
                { _map = rt; break; }
            }
        }
        if (_map == null)
            return Fail("no minimap rect found — neither a MinimapIconTracker with minimapRect wired, nor an " +
                        "object named MinimapBase. There is nothing to draw markers onto.");

        _cam = host != null && host.minimapCamera != null
             ? host.minimapCamera
             : FindFirstObjectByType<MinimapCamera>(FindObjectsInactive.Include)?.GetComponent<Camera>();
        if (_cam == null)
            return Fail("no minimap camera found, so world metres cannot be converted to map pixels.");

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p == null) return Fail("no object tagged Player, so there is nothing to measure distance from.");
        _player = p.transform;

        // Off the RESOLVED rect, not sizeDelta. A minimap anchored by stretch
        // has a sizeDelta of zero regardless of how big it looks, which made
        // every marker pile up in the centre with a negative clamp radius — the
        // markers were being drawn perfectly, on top of each other, invisibly.
        float width = _map.rect.width;
        if (width < 1f) width = _map.sizeDelta.x;
        if (width < 1f)
            return Fail("the minimap rect resolves to zero width, so there is nowhere to place a marker.");
        _mapWidth = width;
        _radius = (width / 2f) - 10f;

        _lastComplaint = null;
        Debug.Log($"[MapIcons] Bound to the minimap ({width:F0}px). Markers live: {MapEventMarker.All.Count}.");

        // The pool lives under the minimap, so it inherits its mask and moves
        // with it. Icons from a previous bind are DESTROYED rather than merely
        // forgotten: the HUD canvas is DontDestroyOnLoad, so the minimap object
        // survives a scene change and anything left parented to it would sit
        // there enabled, showing the last region's markers under this one's.
        foreach (var old in _pool) if (old != null) Destroy(old.gameObject);
        _pool.Clear();
        return true;
    }

    private float _mapWidth = 1f;

    private Image Rent(int index)
    {
        while (_pool.Count <= index)
        {
            var go = new GameObject($"MapMarker_{_pool.Count}", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_map, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            _pool.Add(img);
        }
        return _pool[index];
    }

    private void HideAll()
    {
        for (int i = 0; i < _pool.Count; i++) if (_pool[i] != null) _pool[i].enabled = false;
    }
}
