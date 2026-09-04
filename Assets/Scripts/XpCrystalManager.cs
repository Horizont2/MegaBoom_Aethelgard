using System.Collections.Generic;
using UnityEngine;

// Ticks every XP crystal from ONE Update.
//
// Each crystal used to run its own Update to hover, spin and test the pickup
// radius. The work per crystal is trivial, but a horde run leaves hundreds of
// them on the ground at once and the cost is then the managed-to-native Update
// dispatch itself, paid once per crystal per frame, plus a player and controller
// null-check each. One loop pays that once.
//
// Created automatically the first time a crystal registers — nothing to place in
// the scene.
[DefaultExecutionOrder(-50)]
public class XpCrystalManager : MonoBehaviour
{
    public static XpCrystalManager Instance { get; private set; }

    [Tooltip("Crystals further than this from the camera skip their hover and spin. They are a few pixels at that range, and the pickup test still runs, so nothing is missed.")]
    public float visualDistance = 60f;

    private readonly List<XpCrystal> _crystals = new List<XpCrystal>(256);
    private Transform _player;
    private PlayerController _controller;
    private Transform _cam;

    public static XpCrystalManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("XpCrystalManager");
        Instance = go.AddComponent<XpCrystalManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Register(XpCrystal c) { if (c != null && !_crystals.Contains(c)) _crystals.Add(c); }
    public void Unregister(XpCrystal c) { _crystals.Remove(c); }

    private void Update()
    {
        int count = _crystals.Count;
        if (count == 0) return;

        // Resolve the player ONCE for the whole batch rather than per crystal.
        if (_player == null || _controller == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _player = p.transform;
            _controller = p.GetComponent<PlayerController>();
            if (_controller == null) return;
        }
        if (_cam == null) _cam = CameraCache.MainTransform;

        Vector3 playerPos = _player.position;
        float pickupSqr = _controller.pickupRadius * _controller.pickupRadius;
        Vector3 camPos = _cam != null ? _cam.position : playerPos;
        float visualSqr = visualDistance * visualDistance;
        float dt = Time.deltaTime;
        float time = Time.time;

        // Iterate backwards so a crystal unregistering itself mid-tick (it was
        // collected) can't shuffle entries we have not visited yet.
        for (int i = count - 1; i >= 0; i--)
        {
            var c = _crystals[i];
            if (c == null) { _crystals.RemoveAt(i); continue; }
            c.Tick(dt, time, playerPos, pickupSqr, camPos, visualSqr);
        }
    }
}
