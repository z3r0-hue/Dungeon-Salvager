using UnityEngine;

// Put this on a collectable in the level (a fruit or scrap lying on the floor).
// The object needs a SpriteRenderer and a Collider2D with "Is Trigger" ticked.
public class ItemPickup : MonoBehaviour
{
    public ItemData item;

    [Header("Floating effect")]
    public float bobHeight = 0.1f;
    public float bobSpeed = 2.5f;

    Vector3 startPos;
    float ignoreUntil;
    bool collected;

    void Start()
    {
        startPos = transform.position;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && item != null && sr.sprite == null) sr.sprite = item.icon;
    }

    void Update()
    {
        transform.position = startPos + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobHeight;
    }

    public void IgnorePickupFor(float seconds)
    {
        ignoreUntil = Time.time + seconds;
    }

    void OnTriggerEnter2D(Collider2D other) => TryPickUp(other);
    void OnTriggerStay2D(Collider2D other) => TryPickUp(other);

    void TryPickUp(Collider2D other)
    {
        if (collected || Time.time < ignoreUntil || item == null) return;

        var inventory = other.GetComponentInParent<Inventory>();
        if (inventory == null) return;          // not the player

        if (inventory.Add(item))
        {
            collected = true;                   // so it can't be added twice
            gameObject.SetActive(false);        // gone from the map right away
            Destroy(gameObject);                // it's in the bag now
        }
    }
}
