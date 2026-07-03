using UnityEngine;
using UnityEngine.Video;

[CreateAssetMenu(fileName = "NewTutorialHint", menuName = "Tutorial/Hint Data")]
public class TutorialHintData : ScriptableObject
{
    [Tooltip("Унікальний ключ для PlayerPrefs (наприклад: 'move_hint')")]
    public string key;

    public string title = "TIP";

    [TextArea(3, 5)]
    public string body;

    [Tooltip("Скільки секунд показувати підказку (якщо не чекаємо інпуту)")]
    public float duration = 6f;

    [Tooltip("Чи має панель висіти, поки гравець не натисне кнопку закриття?")]
    public bool waitForInput = false;

    [Header("Visuals (Optional)")]
    public Sprite icon;
    public VideoClip videoClip;

    [Header("Audio (Optional)")]
    [Tooltip("Звук, який грає при появі цієї конкретної підказки")]
    public AudioClip showSound;
}