using UnityEngine;

public struct DamageInfo
{
    public float Amount;
    public bool IsCritical;
    public Vector3 HitPoint;
    public Vector3 PushDirection;
    public float KnockbackForce;
    public float StunDuration;
    // Player-facing attacker label — feeds the death recap's
    // "Slain by ___" line. Optional: null / empty is fine and the
    // recap falls back to "Fell in battle".
    public string SourceName;
    // When true, this hit lands even if the player is mid-dash (dash i-frames
    // are ignored). Used for the player's own grenade blast so it can't be
    // negated for free by dashing in place.
    public bool IgnoresIFrames;
    // A shield cannot stop this one. Reserved for the heavy, separately
    // telegraphed attacks that bosses and elites throw — the whole reason they
    // exist is to stop a raised guard being the answer to every fight, so they
    // must be READ and dodged rather than absorbed. Anything using this MUST
    // also telegraph differently, or it is not a mechanic, it is a gotcha.
    public bool Unblockable;
    // Who swung. Optional, and only the shield uses it — a block has to know
    // which enemy to recoil, stagger and mark vulnerable, and working that out
    // by searching for whatever is nearest picks the wrong one in a crowd,
    // which is precisely the situation blocking exists for.
    public EnemyAI Attacker;
    // � ����������� ���� ����� ������: public DamageType Type; (������, ˳�, Գ�����)
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}