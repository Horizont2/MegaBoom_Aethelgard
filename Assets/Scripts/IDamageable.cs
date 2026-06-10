using UnityEngine;

public struct DamageInfo
{
    public float Amount;
    public bool IsCritical;
    public Vector3 HitPoint;
    public Vector3 PushDirection;
    public float KnockbackForce;
    public float StunDuration;
    // В майбутньому сюди легко додати: public DamageType Type; (Вогонь, Лід, Фізична)
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}