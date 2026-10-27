using UnityEngine;

// The way out. Touch it to extract and keep your loot.
// Needs a SpriteRenderer and a Collider2D with "Is Trigger" ticked.
[RequireComponent(typeof(Collider2D))]
public class ExtractionPortal : MonoBehaviour
{
    [Tooltip("Animation frames for the swirl. Leave empty for a still portal.")]
    public Sprite[] frames;
    public float framesPerSecond = 8f;
    public bool pulse = true;

    SpriteRenderer sr;
    Vector3 baseScale;
    bool used;

    void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
    }

    void Update()
    {
        if (sr != null && frames != null && frames.Length > 0)
            sr.sprite = frames[(int)(Time.time * framesPerSecond) % frames.Length];

        if (pulse)
            transform.localScale = baseScale * (1f + Mathf.Sin(Time.time * 3f) * 0.015f);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (used) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        var menu = PauseMenu.Instance;
        if (menu == null) menu = FindFirstObjectByType<PauseMenu>();
        if (menu == null) { Debug.LogWarning("ExtractionPortal: no PauseMenu in the scene. Build the HUD first."); return; }

        used = true;
        menu.ShowExtracted();
    }
}
