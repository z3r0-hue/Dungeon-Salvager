using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// The dungeon collapse timer. Put it on the Timer panel in your Canvas.
public class GameTimer : MonoBehaviour
{
    [Tooltip("Seconds until the dungeon collapses. 180 = 3 minutes.")]
    public float startTime = 180f;
    [Tooltip("Turns red and pulses when this many seconds are left.")]
    public float warningTime = 30f;

    [Header("UI")]
    public TextMeshProUGUI timeText;
    [Tooltip("An Image with Image Type set to Filled (Horizontal). Optional.")]
    public Image sandBar;
    public Color normalColor = Color.white;
    public Color warningColor = new Color(1f, 0.45f, 0.4f);
    public Color barColor = new Color(0.94f, 0.77f, 0.3f);

    [Header("When time runs out")]
    public UnityEvent onTimeUp = new UnityEvent();   // hook up your "Dungeon Collapsed" screen here later

    public float TimeLeft { get; private set; }
    bool finished;

    void Start()
    {
        TimeLeft = startTime;
        UpdateUI();
    }

    void Update()
    {
        if (finished) return;

        // Time.deltaTime is 0 while the game is paused, so the timer pauses too
        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f)
        {
            TimeLeft = 0f;
            finished = true;
            onTimeUp?.Invoke();
        }
        UpdateUI();
    }

    void UpdateUI()
    {
        int secs = Mathf.CeilToInt(TimeLeft);
        bool warning = TimeLeft <= warningTime;

        if (timeText != null)
        {
            timeText.text = $"{secs / 60}:{secs % 60:00}";
            timeText.color = warning ? warningColor : normalColor;
            float pulse = warning ? 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 0.08f : 1f;
            timeText.transform.localScale = Vector3.one * pulse;
        }

        if (sandBar != null)
        {
            sandBar.fillAmount = startTime > 0 ? TimeLeft / startTime : 0f;
            sandBar.color = warning ? warningColor : barColor;
        }
    }
}
