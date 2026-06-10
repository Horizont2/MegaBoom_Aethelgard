using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ReturnToPoolOnStop : MonoBehaviour
{
    private ParticleSystem ps;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();

        // Примусово вмикаємо 콜бек у налаштуваннях частинок, щоб Unity сам повідомив нас про зупинку
        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Callback;
    }

    // Вбудована подія Unity, яка спрацьовує, коли всі частинки зникли
    private void OnParticleSystemStopped()
    {
        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.ReturnToPool(gameObject);
        }
        else
        {
            // Якщо пулу раптом немає (наприклад, перехід між сценами), просто вимикаємо об'єкт
            gameObject.SetActive(false);
        }
    }
}