using UnityEngine;

public class MissionBoardMarker : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject exclamationMark; // Перетягни сюди знак оклику дошки місій

    private float checkTimer = 0f;

    private void Start()
    {
        UpdateMarkerState();
    }

    private void Update()
    {
        // Перевіряємо статус кожну секунду (щоб не навантажувати гру щокадру)
        checkTimer += Time.deltaTime;
        if (checkTimer >= 1f)
        {
            checkTimer = 0f;
            UpdateMarkerState();
        }
    }

    public void UpdateMarkerState()
    {
        if (exclamationMark == null) return;

        // 1. Чи прослухав гравець найперший діалог Еліаса?
        bool hasHeardIntro = PlayerPrefs.GetInt("Elias_Intro", 0) == 1;

        // 2. Чи відкривав гравець дошку місій хоча б раз? (0 - ще не відкривав)
        bool hasVisitedBoard = PlayerPrefs.GetInt("MissionBoard_Visited", 0) == 1;

        // Показуємо знак оклику ТІЛЬКИ якщо діалог прослухано, а дошку ще не відкривали
        // ==== AND ONLY WHILE THERE IS SOMETHING TO TAKE ====
        //
        // The two flags above are a one-time "go and look at this" hint: they
        // stop being true once the player has visited the board, and they knew
        // nothing about whether the board actually had anything on it. So the
        // mark hung over an empty board, pointing the player at a walk that
        // ends in "no new missions available right now".
        //
        // A marker is a promise. It should be up when the promise can be kept.
        bool boardHasWork = NoticeBoardManager.Instance == null
                            || NoticeBoardManager.Instance.HasMissionsToTake;

        exclamationMark.SetActive(hasHeardIntro && !hasVisitedBoard && boardHasWork);
    }

    // ВАЖЛИВО: Виклич цей метод зі скрипта взаємодії з дошкою, 
    // коли гравець натискає [E], щоб відкрити карту!
    public void MarkAsVisited()
    {
        PlayerPrefs.SetInt("MissionBoard_Visited", 1);
        PlayerPrefs.Save();
        UpdateMarkerState(); // Миттєво ховаємо знак оклику
    }
}