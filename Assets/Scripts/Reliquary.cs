using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A place worth walking to, that you can see, and that you have to earn.
//
// ==== WHY NOT A BEAM ====
//
// The first version put a shaft of light over a chest. It works, and it is what
// every game does, and that is the problem: a coloured laser is a HUD element
// wearing a costume. It says "the designer put a reward here" rather than
// "somebody built something here", and once a map has a few the world reads as a
// menu. A reliquary is a STRUCTURE. Banners on tall poles are the landmark,
// because they are the one prop that reads across broken terrain — high,
// coloured, and a vertical line the eye instantly separates from trees.
//
// ==== WHY IT CANNOT BE GRABBED AND FLED FROM ====
//
// A reward you walk up to and collect is not a decision, it is a pickup, and a
// map of pickups is a chore list. Two locks, layered so the grades feel like
// genuinely different encounters rather than the same one at three lengths:
//
//   GUARDIANS. Dormant until you are close, then they wake. The chest is visibly
//   sealed while any of them stand, so the player reads "kill these first"
//   without being told. Even the roadside find has some — a chest with no lock
//   at all is just a pickup with a lid.
//
//   THE VIGIL. At a shrine and a barrow, killing the guards only earns you the
//   right to START. Lighting the seal commits you to holding the ground while
//   waves arrive, and the chest opens if you are still standing there when the
//   clock runs out. The question is never "can I reach it", it is "can I hold
//   this for twenty-six seconds".
//
// Two rules about the vigil matter more than the numbers. It is NOT a held key —
// standing still with [E] down is the one thing this game never lets you do, so
// a hold-to-open bar only ever measured how fast the player got knocked off it.
// And leaving PAUSES rather than resets: a reset punishes one mistake with the
// whole attempt and teaches players not to start.
//
// The loot lands at the END, never at the start, or the player could open it and
// run — the exact thing this exists to prevent.
//
// ==== WHAT EACH GRADE IS ALLOWED TO GIVE ====
//
// Supplies and crystals from all three; ARMOUR ONLY FROM A BARROW. Armour is
// shop stock, and the shop is most of the progression, so the moment the common
// chests could drop it the economy was being fed from encounters that cost
// nothing. See ArmourLootTable — the odds have exactly one owner.
[DisallowMultipleComponent]
public class Reliquary : MonoBehaviour
{
    public enum Grade
    {
        Wayside,   // roadside find: two guards, open on the spot, supplies only
        Shrine,    // four guards, then a fourteen-second vigil. Supplies, no armour
        Barrow,    // a warband, a twenty-six-second siege, and the only armour in the world
    }

    [Header("Grade")]
    public Grade grade = Grade.Wayside;

    // ==== WHY THIS COMPONENT ASSEMBLES ITSELF ====
    //
    // The first version could only be built by a director that hunted for a clear
    // patch of terrain and assembled the whole site out of loose prefabs at
    // runtime. That put every part of the feature behind one search that could
    // fail — and did, silently, for several rounds: if no site passed the
    // clearance test, nothing existed and nothing said why.
    //
    // Now the site is a PREFAB. Drop Reliquary_Shrine into a location built by
    // hand, add that location to the generator's POI list, and the chest brings
    // its own guardians, seal and vigil with it. Nothing has to find anywhere.
    // The director still exists and still scatters them across open ground, but it
    // is now one way to place a prefab rather than the only path that works.
    [Header("Self-assembly")]
    [Tooltip("Post guardians around the chest on Start. Leave on: the guardians ARE the lock, and a site without them is a free pickup.")]
    public bool spawnGuardians = true;
    [Tooltip("How far out the guardians stand. Widen it for a big hand-built location so they are not standing in the walls.")]
    public float guardRadius = 4f;
    [Tooltip("Override the guardian count for this site. -1 keeps the count the grade implies (2 wayside / 4 shrine / 7 barrow).")]
    public int guardianCountOverride = -1;
    [Tooltip("Also scatter banners, stones and bones around the chest. OFF for a prefab dropped into a hand-built location — the location already has its own dressing — and ON for a site the director places on bare ground.")]
    public bool buildDecor = false;

    [Header("Presentation")]
    [Tooltip("Light coming off the chest, coloured by grade: green wayside, blue shrine, gold barrow. It goes out when the chest is opened — a beacon over an emptied chest walks the player back to nothing.")]
    public bool showBeacon = true;
    [Tooltip("How hard the chest rattles as the vigil runs, rising as the seal nears breaking. During a siege the player is watching the ground they are defending, not the HUD, so the progress has to be legible on the object itself.")]
    public float channelShake = 0.035f;

    [Header("Payout")]
    [Tooltip("Scales supplies only. Armour odds live in ArmourLootTable so the economy has one owner.")]
    [Range(0.5f, 3f)] public float richness = 1f;

    // ==== THE VIGIL ====
    //
    // This used to be a hold-[E] channel: stand still, keep a key down, watch a
    // bar. It had the right shape on paper and none of it in play — holding a key
    // is not a decision, and standing still is the one thing a game about being
    // swarmed never lets you do, so in practice the bar just measured how long it
    // took the player to get knocked off it.
    //
    // A vigil instead. One press lights the seal, and from that moment the site
    // is a fight on a clock: waves arrive, and the seal only breaks if the player
    // is still standing on the ground when the clock runs out. No key to hold, so
    // every second of it is spent playing the game the rest of the game is about.
    //
    // Two rules make it a place rather than a timer. LEAVING PAUSES IT — you
    // cannot light the seal and kite the wave across the valley while it ticks;
    // the reward is for holding THIS ground. And the clock does not reset when
    // you slip out, it holds and resumes, because a reset punishes one mistake
    // with the whole attempt and teaches players never to try.
    [Header("The vigil")]
    [Tooltip("Seconds the player must hold the ground once the seal is lit. Zero means the chest simply opens — right for a roadside find, wrong for anything guarded.")]
    public float waysideVigil = 0f;
    public float shrineVigil = 14f;
    public float barrowVigil = 26f;
    [Tooltip("How far from the chest counts as holding the ground. Generous on purpose: this is a fight, and a leash tight enough to stop you repositioning is a leash that stops you fighting.")]
    public float vigilRadius = 11f;
    [Tooltip("Seconds between attacker waves during a vigil.")]
    public float waveInterval = 7f;

    private LootChest _chest;
    private Light _lantern;
    private readonly List<EnemyAI> _guardians = new List<EnemyAI>(4);
    private readonly List<GameObject> _sealVfx = new List<GameObject>(4);
    private Transform _player;
    private float _progress;
    private bool _spent;
    private float _guardCheck;
    private ChestBeacon _beacon;
    private Vector3 _chestRest;
    private bool _vigilLit;
    private float _held;        // seconds of vigil banked
    private float _nextWave;
    private int _waveIndex;
    private int _spawnedByWaves;
    private float _awayFor;
    private MapEventMarker _marker;

    private static bool IsPlayerDead()
    {
        var pc = FindFirstObjectByType<PlayerController>();
        return pc != null && pc.IsDead;
    }

    public bool Sealed => LivingGuardians() > 0;
    private float VigilTime => grade switch
    {
        Grade.Barrow => barrowVigil,
        Grade.Shrine => shrineVigil,
        _ => waysideVigil,
    };
    private Color Accent => grade switch
    {
        Grade.Barrow => new Color(1.00f, 0.45f, 0.15f),
        Grade.Shrine => new Color(0.85f, 0.45f, 1.00f),
        _ => new Color(0.95f, 0.88f, 0.65f),
    };

    // The beacon's colour is the ONE thing the player can read before committing
    // to the fight, so it carries the only fact worth knowing from a distance:
    // how good this is. Green for a roadside find, blue for a guarded shrine,
    // gold for the barrow — the one that actually holds armour.
    private Color BeaconColour => grade switch
    {
        Grade.Barrow => new Color(1.00f, 0.82f, 0.32f),
        Grade.Shrine => new Color(0.34f, 0.62f, 1.00f),
        _ => new Color(0.42f, 1.00f, 0.52f),
    };

    // Everything the prefab needs to become a live site, without a director.
    private void Start()
    {
        if (_chest == null) Bind(GetComponentInChildren<LootChest>(true));
        if (_chest == null)
        {
            Debug.LogWarning($"[Reliquary] '{name}' has no LootChest under it — there is nothing here to open. " +
                             "Use the prefabs from Tools > Exploration > Build Reliquary Prefabs.", this);
            enabled = false;
            return;
        }

        var set = ReliquarySet.Load();
        if (buildDecor && set != null) Raise(set);
        if (spawnGuardians && _guardians.Count == 0) PostGuardians(set, guardRadius);

        // A map presence, added here rather than baked into the prefab so the
        // grade and the marker can never disagree — the grade is the whole
        // promise the marker is making.
        var marker = gameObject.GetComponent<MapEventMarker>();
        if (marker == null) marker = gameObject.AddComponent<MapEventMarker>();
        marker.kind = grade switch
        {
            Grade.Barrow => MapEventIcons.Kind.ChestBarrow,
            Grade.Shrine => MapEventIcons.Kind.ChestShrine,
            _ => MapEventIcons.Kind.ChestWayside,
        };
        _marker = marker;

        _chestRest = _chest.transform.localPosition;
        if (showBeacon && set != null)
            _beacon = ChestBeacon.Attach(_chest.transform, BeaconColour,
                                         grade == Grade.Barrow ? 2.7f : grade == Grade.Shrine ? 2.3f : 2.0f,
                                         set.beamMaterial);
    }

    public void Bind(LootChest chest)
    {
        if (_chest == chest) return;
        _chest = chest;
        if (_chest == null) return;
        _chest.Opened += OnOpened;
        // The reward lands when the lid is UP, not when the player commits — see
        // LootChest.LidOpened.
        _chest.LidOpened += OnLidOpened;
        // The reliquary owns the interaction. Leaving the chest's own press-to-
        // open live alongside the channel is how a player skips the fight.
        //
        // A flag rather than disabling the component: a disabled MonoBehaviour
        // cannot start a coroutine, so ForceOpen did nothing and the chest
        // opened empty.
        _chest.suppressOwnInteraction = true;
    }

    private void OnDestroy()
    {
        if (_chest == null) return;
        _chest.Opened -= OnOpened;
        _chest.LidOpened -= OnLidOpened;
    }

    // ---- the site ------------------------------------------------------------

    public void Raise(ReliquarySet set)
    {
        if (set == null) return;

        bool barrow = grade == Grade.Barrow;
        int banners = barrow ? 4 : grade == Grade.Shrine ? 2 : 1;
        float ring = barrow ? 4.8f : grade == Grade.Shrine ? 3.4f : 2.4f;

        // Banners first and furthest out: they are the part that has to be seen
        // from across the valley, so nothing may occlude them.
        for (int i = 0; i < banners; i++)
        {
            var prefab = set.PickBanner();
            if (prefab == null) break;
            float a = (i / (float)banners) * Mathf.PI * 2f + Mathf.PI * 0.25f;
            // Tall on purpose: the banners ARE the landmark, so they are the one
            // prop allowed to be bigger than a person.
            Strip(PlaceProp(prefab, Offset(a, ring), -a * Mathf.Rad2Deg, 3.4f, transform));
        }

        // Rune stones make it a shrine rather than a picnic. Odd count and
        // uneven spacing so it reads as raised by hand, not placed by a loop.
        int stones = barrow ? 7 : grade == Grade.Shrine ? 5 : 2;
        for (int i = 0; i < stones; i++)
        {
            var prefab = set.PickRuneStone();
            if (prefab == null) break;
            float a = (i / (float)stones) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
            var go = PlaceProp(prefab, Offset(a, ring * 0.62f), Random.Range(0f, 360f),
                               Random.Range(1.3f, 1.9f), transform);
            Strip(go, keepColliders: true);   // stones are cover; let them block
        }

        if (barrow)
        {
            if (set.archPrefab != null)
                Strip(PlaceProp(set.archPrefab, Offset(0f, ring * 1.25f), 180f, 4.2f, transform), keepColliders: true);

            for (int i = 0; i < 6; i++)
            {
                var bone = set.PickRemains();
                if (bone == null) break;
                // Yaw only. A skull given a random pitch and roll floats at an
                // angle instead of lying where somebody dropped it.
                var go = PlaceProp(bone, Offset(Random.Range(0f, Mathf.PI * 2f), Random.Range(1.4f, ring)),
                                   Random.Range(0f, 360f), Random.Range(0.28f, 0.45f), transform);
                Strip(go);
            }
        }

        if (set.lanternPrefab != null && grade != Grade.Wayside)
        {
            var lamp = PlaceProp(set.lanternPrefab, Offset(Mathf.PI, ring * 0.5f),
                                 Random.Range(0f, 360f), 2.1f, transform);
            Strip(lamp);
            var go = new GameObject("Lantern");
            go.transform.SetParent(transform, false);
            go.transform.position = (lamp != null ? lamp.transform.position : Offset(Mathf.PI, ring * 0.5f))
                                  + Vector3.up * 1.6f;
            _lantern = go.AddComponent<Light>();
            _lantern.type = LightType.Point;
            _lantern.color = Accent;
            _lantern.range = barrow ? 12f : 8f;
            _lantern.intensity = barrow ? 3.2f : 2.1f;
            // A dozen of these each casting shadows would cost more than the
            // whole feature is worth.
            _lantern.shadows = LightShadows.None;
        }

        // Guardians are NOT posted here. Decor and guards are separate decisions:
        // a prefab dropped into a hand-built ruin wants the guards and none of the
        // dressing, and Start owns that call.
        guardRadius = ring;
    }

    // Dormant until approached. Built out of the ordinary enemy AI rather than a
    // bespoke state machine: startPassive already means "stand here until
    // something comes close", which is exactly a sleeping guard, and it means
    // they fight like every other enemy once woken instead of like a special case.
    private void PostGuardians(ReliquarySet set, float ring)
    {
        // Enough of them to be a fight.
        //
        // It was 4 / 1 / 0, and 0 for a wayside meant the commonest chest in the
        // game had no lock on it at all — walk up, press a key, take the supplies.
        // One guardian on a shrine was barely more. The guards are the thing that
        // makes a chest a place rather than a pickup, so there are now enough of
        // them at every grade for the approach itself to be the encounter, before
        // the vigil even starts.
        int count = guardianCountOverride >= 0
                  ? guardianCountOverride
                  : grade switch { Grade.Barrow => 7, Grade.Shrine => 4, _ => 2 };
        if (count == 0) return;
        if (set == null || set.guardianPrefabs == null || set.guardianPrefabs.Length == 0)
        {
            // A sealed chest with nothing to unseal it is a chest nobody can ever
            // open, so this is loud rather than a shrug.
            Debug.LogWarning($"[Reliquary] '{name}' wants {count} guardians but the ReliquarySet has no guardian " +
                             "prefabs. Run Tools > Exploration > Build Reliquary Set, or the seal can never break.", this);
            return;
        }

        if (ring <= 0.1f) ring = 4f;

        for (int i = 0; i < count; i++)
        {
            var prefab = set.guardianPrefabs[Random.Range(0, set.guardianPrefabs.Length)];
            if (prefab == null) continue;

            float a = (i / (float)count) * Mathf.PI * 2f + Mathf.PI * 0.5f;
            Vector3 p = Offset(a, ring);
            var go = Instantiate(prefab, p, Quaternion.identity, null);

            var ai = go.GetComponent<EnemyAI>();
            if (ai == null) continue;
            ai.startPassive = true;
            ai.roamWhilePassive = false;      // a guard stands; it does not mill about
            ai.faceAnchorWhenIdle = true;
            ai.anchorPoint = transform.position;   // facing the thing they guard
            ai.roamRadius = 0.2f;
            ai.aggroRange = 13f;
            // Guardians never give up and never leave. Their whole job is to be
            // the reason this chest is still shut, and one that wandered off
            // would leave a permanently sealed reliquary.
            ai.canDeAggro = false;
            ai.leashRange = 40f;
            ai.CapturePost();
            _guardians.Add(ai);

            _sealVfx.Add(MakeSealMote(i, count));
        }
    }

    // One mote per living guardian, orbiting the chest — the seal made visible.
    // Built in code rather than from a prefab because there is no reusable
    // corruption-anchor effect in the project (the totem's is a scene instance),
    // and a light plus a small emissive bead says "locked" perfectly well.
    private GameObject MakeSealMote(int index, int total)
    {
        var go = new GameObject($"Seal_{index}");
        go.transform.SetParent(transform, false);

        var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bead.transform.SetParent(go.transform, false);
        bead.transform.localScale = Vector3.one * 0.22f;
        Destroy(bead.GetComponent<Collider>());
        var r = bead.GetComponent<Renderer>();
        if (r != null)
        {
            var mat = new Material(r.sharedMaterial);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Accent);
            if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Accent * 3f); }
            r.sharedMaterial = mat;
            OwnedMaterial.Attach(bead, mat);
        }

        var lightGo = new GameObject("SealGlow");
        lightGo.transform.SetParent(go.transform, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Accent;
        l.range = 3.5f;
        l.intensity = 1.6f;
        l.shadows = LightShadows.None;

        go.AddComponent<SealMoteOrbit>().Configure(transform, index / (float)Mathf.Max(1, total));
        return go;
    }

    // Keeps a mote circling the chest. A separate tiny component so the orbit
    // keeps running even while the reliquary's own Update has early-returned.
    private class SealMoteOrbit : MonoBehaviour
    {
        private Transform _centre;
        private float _phase;

        public void Configure(Transform centre, float phase01)
        {
            _centre = centre;
            _phase = phase01 * Mathf.PI * 2f;
        }

        private void Update()
        {
            if (_centre == null) return;
            float a = _phase + Time.time * 0.9f;
            transform.position = _centre.position
                               + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.15f
                               + Vector3.up * (1.3f + Mathf.Sin(Time.time * 1.7f + _phase) * 0.12f);
        }
    }

    // Place a prop at a sane real-world size, upright, sitting on the ground.
    //
    // THIS IS WHY THE SITES LOOKED LIKE A PILE OF ASSETS. Every prefab in the set
    // has localScale 1, but they come from five different packs and their MESHES
    // are authored in wildly different units — the chest is several metres tall
    // in its own space and the "lantern" is a street lamp. Instantiating them as
    // they are gives a shrine where the chest dwarfs the player and the banners
    // are the size of buildings.
    //
    // So nothing is placed at its authored scale. Each prop is measured and
    // scaled to the size it should be IN THIS WORLD, then grounded by its own
    // bounds rather than by its pivot — pivots across packs sit at the base, the
    // centre or nowhere in particular, which is the other half of why things
    // floated and sank. Rotation is yaw only: a banner given a random pitch lies
    // down, and a lying banner is not a landmark.
    public static GameObject PlaceProp(GameObject prefab, Vector3 groundPos, float yawDegrees,
                                       float targetHeight, Transform parent)
    {
        if (prefab == null) return null;

        var go = Instantiate(prefab, groundPos, Quaternion.Euler(0f, yawDegrees, 0f), parent);

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return go;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        if (b.size.y > 0.001f && targetHeight > 0.01f)
        {
            float k = targetHeight / b.size.y;
            go.transform.localScale *= k;

            // Bounds move with the scale, so they have to be re-read before the
            // grounding step or the object is placed using its old footprint.
            rends = go.GetComponentsInChildren<Renderer>();
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        }

        // Sit the BOTTOM of the mesh on the ground, whatever the pivot says.
        float lift = groundPos.y - b.min.y;
        go.transform.position += Vector3.up * lift;
        return go;
    }

    private Vector3 Offset(float angle, float radius)
    {
        Vector3 world = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        world.y = GroundAt(world);
        return world;
    }

    private static void Strip(GameObject go, bool keepColliders = false)
    {
        if (go == null) return;
        if (!keepColliders)
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        else
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.isTrigger = false;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        VFXAutoFade.HideFromMinimap(go);
    }

    // ---- the channel ---------------------------------------------------------

    private void Update()
    {
        if (_spent || _chest == null) return;

        if (_player == null)
        {
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc == null) return;
            _player = pc.transform;
        }

        // Retire spent seal motes as their guardians fall, so the chest visibly
        // unlocks one kill at a time instead of all at once at the end.
        _guardCheck -= Time.deltaTime;
        if (_guardCheck <= 0f)
        {
            _guardCheck = 0.25f;
            int alive = LivingGuardians();
            for (int i = 0; i < _sealVfx.Count; i++)
                if (_sealVfx[i] != null && _sealVfx[i].activeSelf != i < alive)
                    _sealVfx[i].SetActive(i < alive);
        }

        float dist = Vector3.Distance(transform.position, _player.position);
        bool onGround = dist <= vigilRadius;

        // STILL GUARDED. The seal cannot even be lit while a guardian stands, so
        // the approach is its own fight and the player reads "kill these first"
        // off the motes without being told.
        if (Sealed)
        {
            if (dist <= _chest.interactRange + 6f)
                ShowBar(LocalizationManager.Tr("RELIQUARY_SEALED", LivingGuardians()), 0f);
            return;
        }

        float need = VigilTime;

        // NO VIGIL — a roadside find. Guards down, one press, open. The escalation
        // between grades is the whole design: this one has to stay quick or the
        // commonest chest in the game becomes a chore.
        if (need <= 0.01f)
        {
            if (dist <= _chest.interactRange)
            {
                ShowBar(LocalizationManager.Tr("RELIQUARY_PROMPT"), 0f);
                if (Input.GetKeyDown(_chest.interactKey)) Break();
            }
            return;
        }

        // NOT LIT YET. Lighting it is a decision the player makes with their eyes
        // open — the prompt says how long they are signing up for, because a
        // twenty-six second siege sprung on someone at 10% health is not a
        // challenge, it is an ambush by the UI.
        if (!_vigilLit)
        {
            if (dist <= _chest.interactRange)
            {
                ShowBar(LocalizationManager.Tr("RELIQUARY_LIGHT", Mathf.RoundToInt(need)), 0f);
                if (Input.GetKeyDown(_chest.interactKey)) LightVigil();
            }
            return;
        }

        // THE VIGIL IS RUNNING.
        if (onGround)
        {
            _held += Time.deltaTime;
            _awayFor = 0f;

            if (Time.time >= _nextWave)
            {
                _nextWave = Time.time + waveInterval;
                SendWave();
            }
        }
        else
        {
            // AWAY. Banked time bleeds rather than freezing.
            //
            // Freezing it outright looked kind but it is an exploit: light the
            // seal, hold four seconds, walk out — the waves stop the moment you
            // leave — heal up, walk back, hold another four. The siege becomes a
            // sequence of safe nibbles and the whole point of it evaporates.
            //
            // Bleeding is the honest middle. Slipping out for a second costs a
            // second and a half, which is a real price and not a wiped attempt;
            // treating the vigil as somewhere to leave and come back to costs
            // more than it saves.
            _held = Mathf.Max(0f, _held - Time.deltaTime * 1.5f);
            _awayFor += Time.deltaTime;

            // Gone for good — dead, or walked off and never came back. The seal
            // closes and the site resets, because a chest left permanently lit
            // and rattling in an empty field is worse than one nobody opened.
            if (_awayFor > 12f || (_player != null && IsPlayerDead()))
            {
                _vigilLit = false;
                _held = 0f;
                _awayFor = 0f;
                _waveIndex = 0;
                _chest.transform.localPosition = _chestRest;
                if (_beacon != null) _beacon.SetAgitated(false);
                return;
            }
        }

        _progress = Mathf.Clamp01(_held / need);

        // The chest strains harder the closer the seal is to breaking. Feedback on
        // the OBJECT, not only on a bar at the top of the screen — during a fight
        // the player is looking at the ground they are defending, not at the HUD.
        if (channelShake > 0.0001f)
        {
            float amp = channelShake * Mathf.Lerp(0.25f, 1.4f, _progress) * (onGround ? 1f : 0.25f);
            _chest.transform.localPosition = _chestRest + Random.insideUnitSphere * amp;
        }

        ShowBar(onGround
                ? LocalizationManager.Tr("RELIQUARY_VIGIL", Mathf.CeilToInt(need - _held))
                : LocalizationManager.Tr("RELIQUARY_VIGIL_LOST"),
                _progress);

        if (_progress >= 1f)
        {
            // Put it back before handing over: LootChest's own open sequence
            // shakes from where it thinks the chest lives, and it would inherit
            // whatever jitter offset this frame happened to end on.
            _chest.transform.localPosition = _chestRest;
            Break();
        }
    }

    private void Break()
    {
        if (_spent) return;
        _spent = true;
        _chest.ForceOpen();   // payout rides on LootChest.LidOpened
    }

    // Lighting the seal is what makes the site hostile. Everything nearby is told
    // where the player is standing, which is the honest version of "a wave
    // arrives": the region's own alert system brings the region's own enemies,
    // rather than a bespoke spawner conjuring a cast nobody recognises.
    private void LightVigil()
    {
        _vigilLit = true;
        _held = 0f;
        _nextWave = Time.time + 1.5f;   // the first one comes almost at once
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Env_ChestOpen, transform.position);
        if (_beacon != null) _beacon.SetAgitated(true);
    }

    // A wave is SPAWNED, not merely summoned.
    //
    // The obvious implementation is to raise the region alarm and let the nearby
    // patrols converge, and that is still done — it costs nothing and it drags
    // the region's own enemies into the fight, which is better than a private
    // cast appearing from nowhere. But it cannot be the whole of it: RaiseAlert
    // returns false during its own cooldown, while another alert is running, or
    // during a totem capture, and it summons whatever patrols HAPPEN to be
    // nearby. On a quiet corner of the map that is a twenty-six second vigil
    // against nothing at all, which is worse than no mechanic — the player has
    // learnt the siege is a formality and will never respect it again.
    //
    // So the site brings its own attackers, and the alarm is a bonus on top.
    private void SendWave()
    {
        var alerts = RegionAlertDirector.Instance;
        if (alerts != null) alerts.RaiseAlert(transform.position, _player);

        var set = ReliquarySet.Load();
        if (set == null || set.guardianPrefabs == null || set.guardianPrefabs.Length == 0) return;

        int cap = grade == Grade.Barrow ? 12 : grade == Grade.Shrine ? 6 : 0;
        if (cap <= 0 || _spawnedByWaves >= cap) return;

        // Grows wave by wave, so a siege builds instead of arriving flat — the
        // last ten seconds should be the hard part, not an even trickle.
        int want = Mathf.Min((grade == Grade.Barrow ? 3 : 2) + _waveIndex, cap - _spawnedByWaves);
        _waveIndex++;

        for (int i = 0; i < want; i++)
        {
            var prefab = set.guardianPrefabs[Random.Range(0, set.guardianPrefabs.Length)];
            if (prefab == null) continue;

            // Outside the ground being held, so they have to close and the player
            // gets the half-second of warning that makes it a fight rather than
            // an ambush that materialises on top of them.
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 p = Offset(a, vigilRadius + Random.Range(3f, 8f));

            var go = Instantiate(prefab, p, Quaternion.LookRotation(transform.position - p));
            var ai = go.GetComponent<EnemyAI>();
            if (ai == null) continue;
            // Awake and coming. These are not guards; they are the answer to the
            // player having lit the thing.
            ai.startPassive = false;
            ai.canDeAggro = false;
            ai.aggroRange = vigilRadius + 25f;
            ai.leashRange = 90f;
            _spawnedByWaves++;
        }
    }

    // The boss HP bar, borrowed. The player already reads that bar as "there is
    // a fight in progress and here is how far through it you are" — which is
    // exactly what breaking a seal is — so reusing it costs them nothing to
    // learn. GlobalHUD drops the real boss bar out of the way if one is up.
    private void ShowBar(string label, float progress)
    {
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowObjectiveBar(label, progress, Accent);
        _barShownAt = Time.unscaledTime;
    }

    private float _barShownAt = -1f;

    private void LateUpdate()
    {
        // Hide it a moment after the last Show. Every way out of a channel —
        // finishing, walking off, dying, the guardians being killed elsewhere —
        // would otherwise need its own teardown, and the one that got forgotten
        // would leave a bar stuck on screen for the rest of the run.
        if (_barShownAt < 0f) return;
        if (Time.unscaledTime - _barShownAt < 0.25f) return;
        _barShownAt = -1f;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HideObjectiveBar();
    }

    private int LivingGuardians()
    {
        int n = 0;
        for (int i = 0; i < _guardians.Count; i++)
            if (_guardians[i] != null && !_guardians[i].IsDead) n++;
        return n;
    }

    // ---- the payout ----------------------------------------------------------

    // The player committed. Nothing is paid here — see OnLidOpened.
    private void OnOpened()
    {
        if (_lantern != null) _lantern.enabled = false;
        // The light goes out the moment the chest commits, not when the loot
        // lands — it is the "come here" signal, and it has served its purpose.
        if (_beacon != null) _beacon.Extinguish();
        // The map stops pointing at it. A marker over an emptied chest sends the
        // player back across the valley to nothing, which is worse than never
        // having marked it.
        if (_marker != null) _marker.MarkDone();
        ReliquaryDirector.NoteOpened(this);
    }

    // The lid is up. Everything the reward consists of happens now, in one beat.
    private void OnLidOpened()
    {
        // ==== THE RAREST THING IN THE GAME MUST NEVER PAY NOTHING IN SILENCE ====
        //
        // A Barrow is a nine-second vigil against a wave. Walking away from one
        // with no armour, no icons and no explanation is the worst outcome the
        // reward system can produce, and there are three separate ways to reach
        // it: the 55% roll simply fails; WeaponIndex has not been built, so the
        // pool is empty; or every piece within the campaign's tier ceiling is
        // already owned. All three look identical from the player's seat.
        float armourOdds = ArmourLootTable.ArmourChance(grade);
        bool rolledForArmour = Random.value < armourOdds;
        bool gaveArmour = rolledForArmour && GrantArmour();

        if (grade == Grade.Barrow && !gaveArmour)
        {
            string why = rolledForArmour
                ? "the roll PASSED but the loot table returned nothing — the pool is empty, or everything inside " +
                  "the campaign's tier ceiling is already owned. Check Tools > Shop > Build Weapon Index."
                : "the roll failed.";
            Debug.Log($"[Reliquary] Barrow gave no armour — odds were {armourOdds:P0} and {why}");
        }

        // ==== HOW MUCH, AND WHY IT IS NOT ALWAYS THE SAME ====
        //
        // The first pass paid a flat 35-70 wood, 25-55 stone and 15-35 food, and
        // that was wrong twice over. It was too much — the backpack holds 100 / 50
        // / 30, so ONE roadside chest filled it and every chest after that in the
        // run was worth nothing. And it was the same every time, so after two
        // chests the player knew exactly what the third contained and opening it
        // stopped being a question.
        //
        // So the payout is a FRACTION OF WHAT YOU CAN CARRY, rolled against a
        // fortune table. A wayside find is normally a handful; occasionally it is
        // a real haul. A barrow is normally a real haul; rarely it is more than
        // you can carry home, which is a good problem and a memorable one.
        int fortune = RollFortune();
        float share = GradeShare() * FortuneScale(fortune)
                    // Distance still pays, but modestly — this used to more than
                    // double the payout on a far site, which is how a single
                    // chest ended a run's need to gather anything.
                    * Mathf.Lerp(1f, 1.35f, Mathf.InverseLerp(1f, 2.1f, richness));

        var caps = ResourceManager.Instance;
        int capWood = caps != null ? caps.GetRunMax("Wood") : 100;
        int capStone = caps != null ? caps.GetRunMax("Stone") : 50;
        int capFood = caps != null ? caps.GetRunMax("Food") : 30;

        // Not every chest holds everything. A crate of salted meat, an ore cache,
        // a woodpile — a chest with a CHARACTER is worth remembering, and three
        // even piles every time is worth nothing.
        bool anyWood = false, anyStone = false, anyFood = false;
        int kinds = grade == Grade.Barrow ? Random.Range(2, 4)
                  : grade == Grade.Shrine ? Random.Range(1, 4)
                  : Random.Range(1, 3);
        // Drawn with replacement, so asking for two kinds sometimes yields one —
        // which is the point: the spread itself varies, not only the amount.
        for (int i = 0; i < kinds; i++)
            switch (Random.Range(0, 3))
            {
                case 0: anyWood = true; break;
                case 1: anyStone = true; break;
                default: anyFood = true; break;
            }

        // Concentrated when there are fewer kinds, so a single-resource chest is
        // a proper pile rather than a third of one.
        int present = (anyWood ? 1 : 0) + (anyStone ? 1 : 0) + (anyFood ? 1 : 0);
        float focus = present <= 1 ? 1.6f : present == 2 ? 1.25f : 1f;

        int Amount(bool included, int cap) =>
            included ? Mathf.Max(1, Mathf.RoundToInt(cap * share * focus * Random.Range(0.85f, 1.15f))) : 0;

        int wood = Amount(anyWood, capWood);
        int stone = Amount(anyStone, capStone);
        int food = Amount(anyFood, capFood);

        // The XP and crystals the chest itself scatters ride the same roll, so a
        // rich chest is rich in every way at once instead of the two payouts
        // disagreeing about how good the find was. Set before SpawnLoot, which
        // runs on the very next line of LootChest's open sequence.
        if (_chest != null)
        {
            float k = FortuneScale(fortune);
            _chest.minLootItems = Mathf.Max(1, Mathf.RoundToInt(_chest.minLootItems * k));
            _chest.maxLootItems = Mathf.Max(_chest.minLootItems, Mathf.RoundToInt(_chest.maxLootItems * k));
        }

        // SUPPLIES COME OUT AS OBJECTS, not as a number that changes.
        //
        // Crediting the backpack directly is the cheap version and it reads as
        // nothing happening: the lid opens on an empty box while a counter ticks
        // somewhere at the edge of the screen. Throwing physical pickups out of
        // the chest costs a handful of prefabs and turns the payout into the thing
        // the player actually came for — a pile on the ground they walk through.
        //
        // Each pickup carries a share of the total, so the backpack ends up with
        // the same amount either way; the difference is entirely in the watching.
        // If a drop prefab is missing the amount is credited instead of being lost.
        var set = ReliquarySet.Load();
        Vector3 mouth = _chest != null ? _chest.LootOrigin : transform.position + Vector3.up;
        int creditWood = Scatter(set != null ? set.woodDrop : null, ResourceDrop.ResourceType.Wood, wood, mouth);
        int creditStone = Scatter(set != null ? set.stoneDrop : null, ResourceDrop.ResourceType.Stone, stone, mouth);
        int creditFood = Scatter(set != null ? set.foodDrop : null, ResourceDrop.ResourceType.Food, food, mouth);

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            if (creditWood + creditStone + creditFood > 0)
                rm.AddRunResources(creditWood, creditStone, creditFood);
            // No diamonds on top of a piece of armour — see ArmourLootTable.
            if (!gaveArmour)
            {
                int gems = Mathf.Max(1, Mathf.RoundToInt(Random.Range(8f, 18f) * GradeCoin() * FortuneScale(fortune)));

                // A Barrow that produced no armour pays a real purse instead and
                // SAYS SO. The consolation has to be visible or the encounter
                // reads as broken rather than unlucky.
                if (grade == Grade.Barrow)
                {
                    gems = Mathf.RoundToInt(gems * 2.5f);
                    var set2 = ReliquarySet.Load();
                    RewardReveal.Show(set2 != null ? set2.diamondIcon : null,
                        LocalizationManager.Tr("BARROW_NO_ARMOUR_TITLE"),
                        LocalizationManager.Tr("BARROW_NO_ARMOUR_BODY", gems),
                        new Color(0.72f, 0.85f, 1f), 3.2f);
                }
                rm.AddDiamonds(gems);
            }
            rm.UpdateUI();
        }

        RevealHaul(set, fortune, wood, stone, food);
    }

    // The haul gets the same centre-screen beat a piece of armour gets.
    //
    // Supplies arriving as a stack of small toasts at the edge of the screen is
    // how a reward becomes bookkeeping: the player is fighting, the numbers move
    // somewhere in their peripheral vision, and a hoard feels exactly like a
    // handful. Putting the biggest thing in the chest in the middle of the
    // screen, with the icon and the rays, makes the size of the find land — and
    // because the reveal queues, an armour drop still gets its own beat first.
    private void RevealHaul(ReliquarySet set, int fortune, int wood, int stone, int food)
    {
        if (wood + stone + food <= 0) return;

        // ==== ANNOUNCE WHAT THE PLAYER ACTUALLY GETS ====
        //
        // AddRunResources clamps every deposit to the backpack's capacity and
        // silently discards the rest. This was announcing the ROLLED numbers, so
        // a chest could promise sixty wood into a backpack with five slots left
        // and the player would watch the counter move by five. That is the
        // "зараховується не та сума" report, and it is not a payout bug — the
        // payout is correct and the message was lying about it.
        var rm = ResourceManager.Instance;
        wood = Deliverable(rm, "Wood", rm != null ? rm.runWood : 0, wood);
        stone = Deliverable(rm, "Stone", rm != null ? rm.runStone : 0, stone);
        food = Deliverable(rm, "Food", rm != null ? rm.runFood : 0, food);
        if (wood + stone + food <= 0)
        {
            // Everything the chest held was refused by a full backpack. Saying
            // so is far better than a reveal that reads as a reward.
            RewardReveal.Show(set != null ? set.woodIcon : null,
                LocalizationManager.Tr("HAUL_BACKPACK_FULL_TITLE"),
                LocalizationManager.Tr("HAUL_BACKPACK_FULL_BODY"),
                new Color(0.85f, 0.5f, 0.35f), 2.6f);
            return;
        }

        // One reveal for the whole chest, led by whatever there was most of
        // relative to what the player can carry — three separate reveals for one
        // chest would be three times as long and a third as impressive. The
        // OTHER resources are no longer dropped from the picture: they ride
        // along as a row of icons under the title. Showing a single icon for a
        // haul of three things was the "показується іконка тільки одного".
        float fW = wood / (float)Mathf.Max(1, rm != null ? rm.GetRunMax("Wood") : 100);
        float fS = stone / (float)Mathf.Max(1, rm != null ? rm.GetRunMax("Stone") : 50);
        float fF = food / (float)Mathf.Max(1, rm != null ? rm.GetRunMax("Food") : 30);

        Sprite icon = fW >= fS && fW >= fF ? (set != null ? set.woodIcon : null)
                    : fS >= fF ? (set != null ? set.stoneIcon : null)
                    : (set != null ? set.foodIcon : null);

        // SAY WHICH HALF FAILED.
        //
        // "The icons did not appear" has two completely different causes that
        // look identical from the player's seat: the reveal never played at all,
        // or it played with a null sprite and showed only text. One is a broken
        // call path, the other is an unbuilt ReliquarySet, and guessing between
        // them has already cost more than this line will ever cost to keep.
        if (icon == null)
            Debug.LogWarning("[Reliquary] Paying out a haul with NO resource icon — the reveal will show text " +
                             "only. ReliquarySet.woodIcon/stoneIcon/foodIcon are empty; run " +
                             "Tools > Exploration > Build Reliquary Set.", this);

        var parts = new List<string>(3);
        if (wood > 0) parts.Add($"{wood} {LocalizationManager.Tr("Wood")}");
        if (stone > 0) parts.Add($"{stone} {LocalizationManager.Tr("Stone")}");
        if (food > 0) parts.Add($"{food} {LocalizationManager.Tr("Food")}");

        // Longer than an armour reveal, and longer the more there is to read.
        // Three quantities take real time to parse, and a hoard the player never
        // managed to read is a hoard that may as well have been a handful.
        Debug.Log($"[Reliquary] {grade} haul: wood {wood}, stone {stone}, food {food} " +
                  $"(fortune {fortune}) — showing the reward reveal.");
        var row = new List<(Sprite, int)>(3);
        if (wood > 0 && set != null && set.woodIcon != null) row.Add((set.woodIcon, wood));
        if (stone > 0 && set != null && set.stoneIcon != null) row.Add((set.stoneIcon, stone));
        if (food > 0 && set != null && set.foodIcon != null) row.Add((set.foodIcon, food));

        RewardReveal.ShowHaul(icon,
            LocalizationManager.Tr(FortuneTitleKey(fortune)),
            row.Count > 1 ? null : string.Join("   ·   ", parts),
            FortuneColour(fortune),
            row,
            3.0f + parts.Count * 0.45f + (fortune >= 2 ? 0.6f : 0f));
    }

    // How much of `want` will actually land, given what the backpack already
    // holds and what it can hold at all.
    private static int Deliverable(ResourceManager rm, string type, int current, int want)
    {
        if (want <= 0) return 0;
        if (rm == null) return want;
        int room = Mathf.Max(0, rm.GetRunMax(type) - current);
        return Mathf.Min(want, room);
    }

    private static string FortuneTitleKey(int fortune) => fortune switch
    {
        3 => "HAUL_HOARD",
        2 => "HAUL_RICH",
        1 => "HAUL_FAIR",
        _ => "HAUL_MEAGRE",
    };

    // Warmer and brighter the better the find, so the colour says how good it was
    // before a single word is read.
    private static Color FortuneColour(int fortune) => fortune switch
    {
        3 => new Color(1.00f, 0.82f, 0.32f),
        2 => new Color(0.65f, 0.85f, 1.00f),
        1 => new Color(0.75f, 0.92f, 0.70f),
        _ => new Color(0.80f, 0.80f, 0.78f),
    };

    // 0 meagre, 1 fair, 2 rich, 3 hoard. Weighted by grade: a wayside find is
    // usually a handful and a barrow is usually worth the fight, but neither is
    // guaranteed, and that uncertainty is the only reason opening one is a moment.
    private int RollFortune()
    {
        int[] weights = grade switch
        {
            Grade.Barrow => new[] { 10, 35, 40, 15 },
            Grade.Shrine => new[] { 30, 42, 22, 6 },
            _            => new[] { 55, 33, 10, 2 },
        };
        int total = 0;
        foreach (int w in weights) total += w;
        int roll = Random.Range(0, total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return 0;
    }

    private static float FortuneScale(int fortune) => fortune switch
    {
        3 => 2.6f,   // hoard — rare enough to be talked about
        2 => 1.6f,
        1 => 1.0f,
        _ => 0.6f,
    };

    // Fraction of the BACKPACK a fair find is worth. Everything is expressed
    // against carrying capacity rather than in absolute numbers so a change to the
    // backpack size cannot silently make chests trivial or overwhelming.
    private float GradeShare() => grade switch
    {
        Grade.Barrow => 0.38f,
        Grade.Shrine => 0.24f,
        _            => 0.14f,
    };

    private float GradeCoin() => grade switch
    {
        Grade.Barrow => 2.0f,
        Grade.Shrine => 1.4f,
        _            => 1.0f,
    };

    // Throws `total` worth of one resource out of the chest as pickups, and hands
    // back whatever could not be thrown so the caller can credit it directly.
    private static int Scatter(GameObject prefab, ResourceDrop.ResourceType type, int total, Vector3 mouth)
    {
        if (total <= 0) return 0;
        if (prefab == null) return total;

        // Enough to look like a haul, few enough not to carpet the ground.
        int pieces = Mathf.Clamp(Mathf.CeilToInt(total / 12f), 3, 8);
        int per = Mathf.Max(1, total / pieces);
        int left = total;

        for (int i = 0; i < pieces && left > 0; i++)
        {
            int give = (i == pieces - 1) ? left : Mathf.Min(per, left);
            left -= give;

            var go = Instantiate(prefab, mouth + Random.insideUnitSphere * 0.15f, Random.rotation);
            var drop = go.GetComponent<ResourceDrop>();
            if (drop == null) drop = go.AddComponent<ResourceDrop>();
            drop.resourceType = type;
            drop.amount = give;
            // Up and out, so the burst arcs over the open lid rather than
            // squirting sideways through the chest walls.
            drop.popForce = Random.Range(4.5f, 7.5f);
        }
        return left;
    }

    private bool GrantArmour()
    {
        var prize = ArmourLootTable.Roll(grade);
        if (prize == null) return false;

        PlayerPrefs.SetInt("ArmorUnlocked_" + prize.armorID, 1);
        PlayerPrefs.Save();
        ArmourLootTable.NoteGranted();

        int tier = ArmourLootTable.TierOf(prize);
        RewardReveal.Show(prize.icon, ArmorNaming.Display(prize.armorName),
            LocalizationManager.Tr("REVEAL_ARMOUR_SUB",
                                   LocalizationManager.Tr(ArmourLootTable.TierNameKey(tier)),
                                   prize.category.ToString(), prize.basePower),
            ArmourLootTable.TierColour(tier));
        return true;
    }

    private static float GroundAt(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 120f, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        Terrain[] all = Terrain.activeTerrains;
        if (all != null)
        {
            foreach (var t in all)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position;
                Vector3 s = t.terrainData.size;
                if (pos.x >= o.x && pos.x <= o.x + s.x && pos.z >= o.z && pos.z <= o.z + s.z)
                    return t.SampleHeight(pos) + o.y;
            }
        }
        return pos.y;
    }
}
