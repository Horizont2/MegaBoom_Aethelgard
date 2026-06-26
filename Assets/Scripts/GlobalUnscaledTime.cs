using UnityEngine;

[ExecuteAlways] // Цей рядок змушує скрипт працювати навіть коли гра не запущена
public class GlobalUnscaledTime : MonoBehaviour
{
    void Update()
    {
        // Передаємо час. У редакторі Time.unscaledTime теж працює
        Shader.SetGlobalFloat("_GlobalUnscaledTime", Time.unscaledTime);
    }
}