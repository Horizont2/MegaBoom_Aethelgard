using UnityEngine;
using System.Collections.Generic;

public class HammerDamage : MonoBehaviour
{
    [Header("Damage Settings")]
    public float baseDamage = 15f;
    public float knockbackForce = 6f;
    public float hitCooldown = 0.4f;

    private PlayerController player;
    private CameraFollow cameraFollow;

    private Dictionary<Collider, float> lastHitTimes = new Dictionary<Collider, float>();

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.GetComponent<PlayerController>();

        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();
    }

    // OnTriggerStay, NOT Enter: the weapon orbits and stays INSIDE a large
    // enemy/boss collider, so OnTriggerEnter fired exactly once and bosses took
    // a single hit ever. Stay + the per-collider cooldown below gives one hit
    // per hitCooldown for every target size.
    private void OnTriggerStay(Collider other)
    {
        // --- Բ��: ����� �'� ����� ��'���� � ����� Enemy ---
        if (!other.CompareTag("Enemy")) return;

        // ���������, ��� ������� �� ������ ��� ����
        if (player != null && other.gameObject == player.gameObject) return;

        // �������� �� ���������
        if (other.TryGetComponent(out IDamageable damageable))
        {
            if (lastHitTimes.TryGetValue(other, out float lastTime))
            {
                if (Time.time < lastTime + hitCooldown) return;
            }

            lastHitTimes[other] = Time.time;

            float actualDamage = baseDamage;
            bool isCrit = false;
            Vector3 pushDir = Vector3.zero;

            if (player != null)
            {
                actualDamage *= player.globalDamageMultiplier;
                // Honour the same crit/STACK/lifesteal the melee swing uses —
                // the orbit weapon previously hardcoded a x2.5 crit and ignored
                // CritDamage, Lifesteal and STACK level-up choices entirely, so
                // builds around them didn't affect the primary auto-weapon.
                isCrit = Random.value <= player.globalCritChance;
                if (isCrit) actualDamage *= player.critDamageMultiplier;
                actualDamage *= player.currentMultiplier;   // STACK x1..x5

                if (player.lifeStealFraction > 0f)
                    player.Heal(actualDamage * player.lifeStealFraction);

                pushDir = (other.transform.position - player.transform.position).normalized;
                pushDir.y = 0;
            }

            DamageInfo info = new DamageInfo
            {
                Amount = actualDamage,
                IsCritical = isCrit,
                PushDirection = pushDir,
                KnockbackForce = isCrit ? knockbackForce * 1.5f : knockbackForce,
                StunDuration = isCrit ? 0.6f : 0.3f,
                HitPoint = other.ClosestPoint(transform.position)
            };

            damageable.TakeDamage(info);

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitEnemy);
            if (cameraFollow != null) cameraFollow.TriggerShake(isCrit ? 0.2f : 0.05f, 0.1f);
        }
    }
}