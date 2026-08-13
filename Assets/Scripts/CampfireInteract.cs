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

    private void Awake()
    {
        // Auto-attach the looping fire-crackle audio to every campfire so
        // the sound plays without hand-wiring each prefab. The actual FMOD
        // event lives under AudioID.Ambient_CampFire — assign it in the
        // AudioManager inspector (the SoundGroup for "AMB/AMB_CampFire").
        // Until it's assigned CampfireAudio no-ops with a warning, so this
        // is safe to ship even before the event is wired.
        if (GetComponent<CampfireAudio>() == null)
            gameObject.AddComponent<CampfireAudio>();
    }

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

        // ճ���� ����� ���� �� � ����� � ������'� �� �����
        if (distance <= healRadius && player.currentHealth < player.maxHealth)
        {
            player.Heal(healPerSecond * Time.deltaTime);

            if (!isHealing)
            {
                isHealing = true;
                if (healEffect != null)
                {
                    // ���-Գ��: ������� ����'����� ����� �� ������, ��� �� �������� ����� �� ���
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
                    // �'��� ���'����� �����, ��� ������� ����� ���������� � �����, � �� ���������������
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