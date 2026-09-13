using UnityEngine;

// Puts the bought shield in the player's off hand and tells the guard what it
// is made of.
//
// ==== WHY IT FINDS ITS OWN SOCKET ====
//
// The player prefab has a WeaponSocket for the right hand and nothing for the
// left, because until now nothing went there. Rather than requiring every
// player prefab in every scene to be re-authored — and silently doing nothing
// in whichever one somebody forgets — this resolves the off hand the same way
// SpawnEquippedWeapon resolves the main one: a named socket if a designer made
// it, the rig's own left hand bone otherwise, and it creates the attachment
// point itself if it has to.
//
// The rig is KayKit, the same pack the shield models come from, so its bone is
// `handslot.l`. The other names are there because a prefab swapped later should
// not silently lose its shield.
[DisallowMultipleComponent]
public class ShieldLoadout : MonoBehaviour
{
    // The SLOT is separate — a shield is held in the other hand and must not
    // displace the sword. Ownership and upgrade level are NOT: shields share
    // WeaponData and their IDs start at 900, well clear of every weapon, so
    // reusing the weapon keys means the shop's existing owned/level/upgrade
    // paths work on them untouched instead of eight call sites growing a
    // special case each.
    public const string PP_SELECTED = "SelectedShieldID";
    public const string PP_LEVEL_PREFIX = "WeaponLevel_";
    public const string PP_UNLOCK_PREFIX = "WeaponUnlocked_";

    [Tooltip("Local offset applied to the shield inside the hand. Most models sit right at identity; this is the escape hatch for one that does not.")]
    public Vector3 localOffset = Vector3.zero;
    public Vector3 localEuler = Vector3.zero;

    private GameObject _current;

    private void Start() => Refresh();

    public void Refresh()
    {
        if (_current != null) { Destroy(_current); _current = null; }

        var block = GetComponent<PlayerBlock>();
        var data = EquippedShield();

        if (data == null)
        {
            // No shield is a valid loadout, not an error. Blocking bare-handed
            // is allowed and simply bad — which teaches what a shield is for
            // far better than disabling the button and explaining nothing.
            if (block != null) block.EquipShield(PlayerBlock.ShieldStats.Bare);
            return;
        }

        int level = PlayerPrefs.GetInt(PP_LEVEL_PREFIX + data.weaponID, 0);

        if (block != null)
        {
            block.EquipShield(new PlayerBlock.ShieldStats
            {
                parryWindowBonus = data.parryWindowBonus,
                // Upgrades make a shield LIGHTER to hold rather than stronger:
                // a shield that blocked more damage per point of stamina would
                // eventually make the pool irrelevant and turn guarding back
                // into a permanent state, which is the thing the whole system
                // is built to avoid.
                staminaMultiplier = Mathf.Max(0.35f, data.staminaMultiplier + data.staminaMultiplierPerLevel * level),
                angleBonus = data.guardAngleBonus,
                reflectFraction = Mathf.Clamp01(data.reflectFraction + data.reflectPerLevel * level),
                bonusStamina = data.bonusStamina + data.bonusStaminaPerLevel * level,
            });
        }

        if (data.inGamePrefab == null) return;

        Transform socket = ResolveOffHandSocket();
        if (socket == null)
        {
            Debug.LogWarning("[Shield] No off-hand socket or left-hand bone found on the player, so the shield is " +
                             "invisible. Its stats still apply. Add an empty child called 'ShieldSocket' under the " +
                             "left hand bone to fix it.", this);
            return;
        }

        _current = Instantiate(data.inGamePrefab, socket);
        _current.transform.localPosition = localOffset;
        _current.transform.localRotation = Quaternion.Euler(localEuler);

        // A shield is scenery on the arm — nothing on it should be able to hit,
        // be hit, or trail. A stray collider in the hand pushes the player
        // around and blocks their own melee overlap.
        foreach (var c in _current.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var tr in _current.GetComponentsInChildren<TrailRenderer>(true)) { tr.emitting = false; tr.enabled = false; }
    }

    public WeaponData EquippedShield()
    {
        int id = PlayerPrefs.GetInt(PP_SELECTED, -1);
        if (id < 0) return null;

        var index = WeaponIndex.Load();
        if (index == null || index.weapons == null) return null;
        foreach (var w in index.weapons)
            if (w != null && w.category == ItemCategory.Shield && w.weaponID == id) return w;
        return null;
    }

    private Transform ResolveOffHandSocket()
    {
        Transform t = FindDeep(transform, "ShieldSocket");
        if (t != null) return t;

        Transform hand = FindDeep(transform, "handslot.l")
                      ?? FindDeep(transform, "hand_l")
                      ?? FindDeep(transform, "hand_L")
                      ?? FindDeep(transform, "LeftHand")
                      ?? FindDeep(transform, "mixamorig:LeftHand");
        if (hand == null) return null;

        // Made once and reused, so repeated Refresh calls do not stack empties
        // in the hand.
        var made = new GameObject("ShieldSocket").transform;
        made.SetParent(hand, false);
        return made;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
