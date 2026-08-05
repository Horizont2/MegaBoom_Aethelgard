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
    // � ����������� ���� ����� ������: public DamageType Type; (������, ˳�, Գ�����)
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}