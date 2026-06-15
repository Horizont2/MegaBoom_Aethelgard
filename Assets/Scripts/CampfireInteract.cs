using UnityEngine;

public class CampfireInteract : MonoBehaviour
{
    [Header("Heal Settings")]
    public float healPerSecond = 10f;
    public float healRadius = 5f;

    [Header("Visual Effects")]
    public ParticleSystem healEffect;

    private PlayerController player;
    private bool isHealing = false;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerController>();

        if (healEffect != null) healEffect.Stop();
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);

        // Хілимо тільки якщо ми в радіусі І здоров'я не повне
        if (distance <= healRadius && player.currentHealth < player.maxHealth)
        {
            player.Heal(healPerSecond * Time.deltaTime);

            if (!isHealing)
            {
                isHealing = true;
                if (healEffect != null)
                {
                    // ААА-Фікс: Жорстко прив'язуємо ефект до гравця, щоб він ідеально ходив за ним
                    healEffect.transform.SetParent(player.transform);
                    healEffect.transform.localPosition = Vector3.up * 0.1f;
                    healEffect.transform.localRotation = Quaternion.identity;
                    healEffect.Play();
                }
            }
        }
        else
        {
            if (isHealing)
            {
                isHealing = false;
                if (healEffect != null)
                {
                    healEffect.Stop();
                    // М'яко відв'язуємо ефект, щоб останні іскри залишилися в повітрі, а не телепортувалися
                    healEffect.transform.SetParent(null);
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, healRadius);
    }
}