using UnityEngine;

// Enemy projectile (arrow / bolt) fired by EnemyAI ranged mode. Flies a real
// BALLISTIC ARC (gravity), tilting to follow its trajectory so it reads like a
// real arrow. Raycasts each frame so fast arrows don't tunnel through the
// player, deals damage on hit, and self-destructs on impact / world hit / after
// its lifetime. Passes through other enemies and triggers. No Collider needed.
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("Seconds before the arrow despawns if it hits nothing.")]
    public float lifetime = 6f;
    [Tooltip("Optional impact VFX spawned where the arrow lands.")]
    public GameObject hitVFXPrefab;
    public bool playHitSfx = true;
    [Tooltip("Arrows embed in the player (true); magic orbs / bolts burst on impact (false).")]
    public bool stickOnHit = true;

    // ==== AN ARROW YOU CANNOT SEE IS NOT A TELEGRAPH ====
    //
    // An arrow is a thin dark stick moving at eighteen metres a second against
    // meadow, wood and rock. The archer's wind-up is signalled properly — a
    // colour pulse and a ThreatUI marker — but the SHOT itself, the part the
    // player has to physically dodge, was effectively invisible until it landed.
    // Losing health to something you never saw is the least fair thing a game
    // can do.
    //
    // A thin bright streak off the head fixes that for almost nothing: a trail
    // is one strip of geometry and it is already how the mage orb reads.
    //
    // Deliberately NOT a light. Lights on projectiles are the expensive mistake
    // — the renderer is Forward+ and every moving light rebuilds tile lists —
    // and the orb's own lights had to be capped for exactly that reason.
    [Header("Flight streak")]
    [Tooltip("Draw a thin streak behind the head so the shot is readable in flight. Off restores the bare arrow.")]
    public bool flightTrail = true;
    [Tooltip("Colour at the head. The tail fades to fully transparent on its own.")]
    public Color trailColor = new Color(1f, 0.82f, 0.45f, 0.9f);
    [Tooltip("Seconds of streak. Short — a long one reads as a laser rather than as an arrow.")]
    public float trailTime = 0.16f;
    [Tooltip("Width at the head, in metres. Thin on purpose: this is a hint of motion, not a comet.")]
    public float trailWidth = 0.075f;

    private TrailRenderer _trail;

    // ==== THE TRAIL MATERIAL HAS TO BE SHARED ====
    //
    // A Material built per arrow would leak one per shot, and a TrailRenderer
    // assigned through `.material` instead of `.sharedMaterial` instantiates its
    // own copy on top of that. One, cached, for every arrow ever fired.
    //
    // Unlit, because a streak that takes lighting goes dark in exactly the
    // conditions it is most needed in. No texture is required: a trail is a
    // ribbon whose shape comes from its width curve and whose fade comes from
    // its colour gradient, so the missing-texture white square that bites
    // billboarded particles cannot happen here.
    private static Material s_trailMat;

    private static Material TrailMaterial()
    {
        if (s_trailMat != null) return s_trailMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Sprites/Default");
        s_trailMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "ArrowStreak" };
        if (s_trailMat.HasProperty("_Surface")) s_trailMat.SetFloat("_Surface", 1f);   // transparent
        if (s_trailMat.HasProperty("_Blend")) s_trailMat.SetFloat("_Blend", 1f);       // additive
        s_trailMat.renderQueue = 3000;
        return s_trailMat;
    }

    private void EnsureTrail()
    {
        if (!flightTrail) return;
        if (_trail == null) _trail = GetComponent<TrailRenderer>();
        if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();

        _trail.time = trailTime;
        _trail.startWidth = trailWidth;
        _trail.endWidth = 0f;               // tapers to a point, so it reads as speed
        _trail.numCapVertices = 2;
        _trail.minVertexDistance = 0.08f;   // fewer verts on a fast, straight flight
        _trail.autodestruct = false;
        _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        _trail.alignment = LineAlignment.View;
        _trail.sharedMaterial = TrailMaterial();
        _trail.startColor = trailColor;
        _trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        _trail.Clear();
        _trail.emitting = true;
    }

    private void StopTrail()
    {
        if (_trail == null) return;
        // Stop EMITTING rather than disabling: the streak already in the air
        // gets to fade out over trailTime instead of vanishing on the frame the
        // arrow lands, which is what makes an impact read as an impact.
        _trail.emitting = false;
    }

    private Vector3 velocity;
    private float gravity;
    private float damage;
    private GameObject owner;
    private bool launched;

    private void Awake()
    {
        // If the prefab carries a Rigidbody, keep it kinematic + no gravity so
        // physics never fights our transform move (that made arrows hang / arc
        // oddly). Movement is driven purely by our own integration below.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        // ==== TWO SCHEDULED DESTROYS FOR ONE ARROW ====
        //
        // Awake registered a backstop Destroy and LaunchBallistic registered
        // another, so every shot queued two native destroy callbacks against the
        // same object. Harmless in effect — the second finds nothing — but it is
        // twice the bookkeeping per shot, and it makes the object impossible to
        // RECYCLE, because a scheduled Destroy cannot be cancelled.
        //
        // One timer, run in Update, which a pooled projectile can simply reset.
        _expiresAt = Time.time + Mathf.Max(lifetime, 0.5f) + 1f;
    }

    // Straight-line shot (kept for compatibility / non-arcing bolts).
    public void Launch(Vector3 dir, float speed, float dmg, GameObject source)
    {
        LaunchBallistic(dir.normalized * speed, 0f, dmg, source);
    }

    // Ballistic shot: initial velocity + gravity. EnemyAI computes the velocity
    // that arcs to the player, so the arrow lobs and drops naturally.
    public void LaunchBallistic(Vector3 initialVelocity, float grav, float dmg, GameObject source)
    {
        velocity = initialVelocity;
        gravity = grav;
        damage = dmg;
        owner = source;
        launched = true;
        if (velocity.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(velocity);
        _expiresAt = Time.time + lifetime;
        EnsureTrail();
    }

    private float _expiresAt = float.MaxValue;

    // Set by whoever owns a RECYCLED projectile. When present it is called
    // instead of Destroy, so the object goes back to its owner's pool rather
    // than to the garbage collector. Null for ordinary prefab arrows, which are
    // destroyed exactly as before.
    // ==== A PUBLIC DELEGATE ON A MonoBehaviour BREAKS THE BUILD ====
    //
    // Unity walks every PUBLIC field of a MonoBehaviour when it builds the
    // script's serialization layout. System.Action is not serializable, so the
    // editor and the player disagree about what this class contains — which
    // surfaces as "script class layout is incompatible between the editor and
    // the player" and a build failure naming the fields on either side of it,
    // not this one.
    //
    // NonSerialized states the intent and takes it out of the layout entirely.
    [System.NonSerialized] public System.Action<GameObject> Retire;

    // Put a reused body back into a launchable state. Everything a previous
    // flight could have left behind is cleared here; a pooled object that keeps
    // one stale field is the hardest kind of bug to find later.
    public void ResetForReuse()
    {
        launched = false;
        velocity = Vector3.zero;
        gravity = 0f;
        owner = null;
        _expiresAt = float.MaxValue;
        if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        transform.SetParent(null, true);
        transform.localScale = _spawnScale == Vector3.zero ? transform.localScale : _spawnScale;
    }

    private Vector3 _spawnScale;

    private void Start() { if (_spawnScale == Vector3.zero) _spawnScale = transform.localScale; }

    private void Retreat()
    {
        if (Retire != null) { Retire(gameObject); return; }
        Destroy(gameObject);
    }

    private void Update()
    {
        if (Time.time >= _expiresAt) { Retreat(); return; }
        if (!launched) return;

        float dt = Time.deltaTime;
        // Integrate gravity so the path arcs and the arrow descends.
        velocity += Vector3.down * gravity * dt;

        Vector3 pos = transform.position;
        Vector3 stepVec = velocity * dt;
        float step = stepVec.magnitude;

        // ==== THE LAST UNMASKED PER-FRAME QUERY IN A GAMEPLAY SCRIPT ====
        //
        // This cast took no layer mask and no trigger override. With
        // queriesHitTriggers on globally, every arrow tested every collider on
        // every layer each frame of its flight — minimap volumes, foliage
        // triggers and the terrain's ~2500 tree colliders included — and then
        // walked a parent chain to find out it had hit scenery. Every other
        // per-frame query in the project already passes a mask.
        //
        // Only three layers can ever matter to an arrow: the player, the world
        // it stops against, and the enemies it must pass THROUGH (so the
        // pass-through test below still gets its chance to run).
        if (s_arrowMask == int.MinValue)
        {
            int m = 0;
            foreach (var n in new[] { "Default", "Obstacles", "PlayerPhysics", "Player", "Damageable" })
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0) m |= 1 << l;
            }
            s_arrowMask = m != 0 ? m : ~0;
        }

        if (step > 0.0001f && Physics.Raycast(pos, stepVec.normalized, out RaycastHit hit, step + 0.15f,
                                              s_arrowMask, QueryTriggerInteraction.Ignore))
        {
            PlayerController pc = hit.collider.GetComponentInParent<PlayerController>();
            if (pc != null)
            {
                pc.TakeDamage(new DamageInfo
                {
                    Amount = damage,
                    PushDirection = velocity.normalized,
                    SourceName = "Archer"
                });

                // ==== AN ARROW THE SHIELD STOPPED BELONGS TO THE SHIELD ====
                //
                // This buried every arrow in whatever collider the cast found,
                // which for a player behind a raised guard is still the
                // CharacterController — so a shot the shield had just absorbed
                // ended up sticking out of the player's chest. That reads as the
                // block having failed, which is the exact opposite of what
                // happened, and it undermines the one defensive move the game
                // most wants the player to trust.
                //
                // PlayerBlock records what it did and on which frame, so asking
                // immediately after TakeDamage gets this hit's answer and never a
                // stale one.
                Transform stickTo = hit.collider.transform;
                var blk = PlayerBlock.Instance;
                if (blk != null && blk.LastResultFrame == Time.frameCount
                    && (blk.LastResult == PlayerBlock.Result.Blocked
                        || blk.LastResult == PlayerBlock.Result.Parried))
                {
                    // A parried arrow should not stick at all — it is knocked
                    // away, and leaving it planted in the shield turns the game's
                    // best defensive moment into a pincushion.
                    if (blk.LastResult == PlayerBlock.Result.Parried) { Impact(); return; }

                    Transform shield = blk.ShieldTransform;
                    if (shield != null) stickTo = shield;
                }

                if (stickOnHit) StickInto(stickTo, hit.point);
                else Impact();
                return;
            }

            // Pass through enemies, including the shooter; stop on world.
            // Triggers no longer need testing here — the cast ignores them.
            bool isEnemy = hit.collider.GetComponentInParent<EnemyAI>() != null;
            if (!isEnemy)
            {
                Impact();
                return;
            }
        }

        transform.position = pos + stepVec;
        // Tilt the arrow to follow its arc.
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity);
    }

    private static int s_arrowMask = int.MinValue;

    private void Impact()
    {
        if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, transform.rotation);
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Arrow_Hit, transform.position);
        StopTrail();
        Retreat();
    }

    // Embed the arrow in the player's body and leave it there for a few seconds
    // (per feedback) so hits feel weighty, instead of vanishing on contact.
    private void StickInto(Transform body, Vector3 point)
    {
        launched = false;   // stop integrating / raycasting
        StopTrail();
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Arrow_Hit, transform.position);

        // Bury the head INTO the body. The raycast hits the CharacterController
        // capsule, which is wider than the visible mesh, so sitting at the exact
        // hit point left the arrow floating in the air beside the player. Push it
        // forward along its flight so the shaft actually sinks into the model.
        Vector3 dir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : transform.forward;
        transform.position = point + dir * 0.35f;
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-8f, 8f), 0f);

        // Ride along with whatever it hit (follows the player as they move).
        if (body != null) transform.SetParent(body, worldPositionStays: true);

        // A collider left on the stuck arrow could re-trigger things — strip it.
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Linger, then drop off. Through the same timer as everything else, so a
        // recycled body is returned to its pool rather than destroyed out from
        // under it.
        _expiresAt = Time.time + 4f;
    }
}
