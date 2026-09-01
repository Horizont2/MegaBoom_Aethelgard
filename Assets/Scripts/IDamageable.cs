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
    // � ����������� ���� ����� ������: public DamageType Type; (������, ˳�, Գ�����)
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}