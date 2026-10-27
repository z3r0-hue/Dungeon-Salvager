using UnityEngine;
using UnityEngine.UI;

// One square in the bag. Put this on each Slot object (which also has an Image and a Button).
public class InventorySlotUI : MonoBehaviour
{
    [Tooltip("The child Image that shows the item's picture.")]
    public Image icon;

    [HideInInspector] public int index;
    [HideInInspector] public InventoryUI owner;

    Image background;
    Button button;

    void Awake()
    {
        background = GetComponent<Image>();
        button = GetComponent<Button>();
        button.onClick.AddListener(() => owner.OnSlotClicked(index));
    }

    public void SetItem(ItemData item)
    {
        icon.enabled = item != null;
        if (item != null) icon.sprite = item.icon;
    }

    // selected = teal frame, dimmed = greyed out (can't be picked right now)
    public void SetLook(Sprite bg, bool dimmed)
    {
        background.sprite = bg;
        icon.color = dimmed ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
        button.interactable = !dimmed;
    }
}
