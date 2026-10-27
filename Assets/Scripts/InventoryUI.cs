using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Shows the bag on screen and handles clicking: open/close, select, EAT, MIX, DROP.
// Put this on your Canvas (or an empty "BagUI" object inside it).
public class InventoryUI : MonoBehaviour
{
    [Header("The bag")]
    public Inventory inventory;
    public bool pauseWhileOpen = true;

    [Header("Bag button (bottom-right)")]
    public Image bagButtonIcon;
    public Sprite bagClosedSprite;
    public Sprite bagOpenSprite;
    public TextMeshProUGUI countText;   // the number on the gold badge

    [Header("Bag panel")]
    public GameObject bagPanel;
    public TextMeshProUGUI titleText;
    public Image weightFill;            // Image Type = Filled, Horizontal
    public TextMeshProUGUI weightText;
    [Tooltip("The SlotGrid object. The slots inside it are found automatically.")]
    public Transform slotGrid;
    public InventorySlotUI[] slots;     // filled in from Slot Grid if you leave it empty

    [Header("Slot looks")]
    public Sprite slotNormal;
    public Sprite slotSelected;
    public Sprite slotDisabled;

    [Header("Info strip")]
    public Image infoIcon;
    public TextMeshProUGUI infoName;
    public TextMeshProUGUI infoDescription;

    [Header("Action menu (EAT / MIX / DROP)")]
    public RectTransform actionMenu;
    public TextMeshProUGUI actionTitle;
    public Button eatButton;
    public Button mixButton;
    public Button dropButton;

    public bool IsOpen => bagPanel != null && bagPanel.activeSelf;
    public static int EscUsedFrame = -1;   // so ESC doesn't also open the pause menu

    int selected = -1;
    bool mixing;
    int mixFirst = -1;

    // ---------------------------------------------------------------- setup
    void Start()
    {
        if ((slots == null || slots.Length == 0) && slotGrid != null)
            slots = slotGrid.GetComponentsInChildren<InventorySlotUI>(true);

        for (int i = 0; i < slots.Length; i++) { slots[i].index = i; slots[i].owner = this; }

        eatButton.onClick.AddListener(Eat);
        mixButton.onClick.AddListener(StartMix);
        dropButton.onClick.AddListener(Drop);

        inventory.OnChanged += Refresh;
        bagPanel.SetActive(false);
        actionMenu.gameObject.SetActive(false);
        Refresh();
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnChanged -= Refresh;
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        if (TogglePressed()) ToggleBag();
        if (CancelPressed() && (mixing || IsOpen))
        {
            EscUsedFrame = Time.frameCount;
            if (mixing) CancelMix();
            else CloseBag();
        }
    }

    // ---------------------------------------------------------------- open / close
    // Hook the bag button's On Click to this
    public void ToggleBag()
    {
        if (PauseMenu.IsPaused) return;
        if (bagPanel.activeSelf) CloseBag(); else OpenBag();
    }

    public void OpenBag()
    {
        bagPanel.SetActive(true);
        if (pauseWhileOpen) Time.timeScale = 0f;
        selected = -1; mixing = false;
        Refresh();
    }

    // Hook the X button's On Click to this
    public void CloseBag()
    {
        bagPanel.SetActive(false);
        actionMenu.gameObject.SetActive(false);
        if (!PauseMenu.IsPaused) Time.timeScale = 1f;
        selected = -1; mixing = false;
        Refresh();
    }

    // ---------------------------------------------------------------- clicking slots
    public void OnSlotClicked(int index)
    {
        bool hasItem = index < inventory.Count;

        if (mixing)
        {
            if (index == mixFirst || !hasItem) { CancelMix(); return; }
            int result = inventory.Mix(mixFirst, index);   // Refresh runs automatically
            mixing = false;
            selected = result;
            Refresh();
            return;
        }

        if (!hasItem) { selected = -1; actionMenu.gameObject.SetActive(false); Refresh(); return; }

        selected = index;
        ShowActionMenu(index);
        Refresh();
    }

    void ShowActionMenu(int index)
    {
        var item = inventory.items[index];
        actionTitle.text = item.itemName;
        eatButton.interactable = item.canEat;     // scrap can't be eaten
        mixButton.interactable = HasAnyMix(index);

        // Put the menu just to the left of the clicked slot
        var slotRect = (RectTransform)slots[index].transform;
        Vector3 leftEdge = slotRect.TransformPoint(new Vector3(slotRect.rect.xMin, slotRect.rect.center.y, 0f));
        actionMenu.gameObject.SetActive(true);
        actionMenu.pivot = new Vector2(1f, 0.5f);
        actionMenu.position = leftEdge + Vector3.left * 12f * actionMenu.lossyScale.x;
    }

    // ---------------------------------------------------------------- actions
    void Eat()
    {
        if (selected < 0) return;
        inventory.Eat(selected);
        selected = -1;
        actionMenu.gameObject.SetActive(false);
        Refresh();
    }

    void Drop()
    {
        if (selected < 0) return;
        inventory.Drop(selected);
        selected = -1;
        actionMenu.gameObject.SetActive(false);
        Refresh();
    }

    void StartMix()
    {
        if (selected < 0) return;
        mixing = true;
        mixFirst = selected;
        actionMenu.gameObject.SetActive(false);
        Refresh();
    }

    void CancelMix()
    {
        mixing = false;
        selected = mixFirst;
        Refresh();
    }

    bool HasAnyMix(int index)
    {
        for (int i = 0; i < inventory.Count; i++)
            if (i != index && inventory.GetMixResult(inventory.items[index], inventory.items[i]) != null) return true;
        return false;
    }

    // ---------------------------------------------------------------- drawing
    void Refresh()
    {
        // Badge + bag icon
        if (countText != null) countText.text = inventory.Count.ToString();
        if (bagButtonIcon != null) bagButtonIcon.sprite = bagPanel.activeSelf ? bagOpenSprite : bagClosedSprite;

        // Weight
        float w = inventory.CurrentWeight, max = Mathf.Max(1, inventory.maxWeight);
        if (weightFill != null)
        {
            weightFill.fillAmount = w / max;
            weightFill.color = w / max < 0.6f ? new Color(0.43f, 0.78f, 0.51f)
                             : w / max < 0.9f ? new Color(0.94f, 0.77f, 0.3f)
                             : new Color(0.94f, 0.4f, 0.35f);
        }
        if (weightText != null) weightText.text = $"{inventory.CurrentWeight}/{inventory.maxWeight}";

        if (titleText != null) titleText.text = mixing ? "MIX WITH..." : "BAG";

        // Slots
        for (int i = 0; i < slots.Length; i++)
        {
            ItemData item = i < inventory.Count ? inventory.items[i] : null;
            slots[i].SetItem(item);

            bool isSelected = mixing ? i == mixFirst : i == selected;
            bool dimmed = false;
            if (mixing && i != mixFirst)
                dimmed = item == null || inventory.GetMixResult(inventory.items[mixFirst], item) == null;

            slots[i].SetLook(isSelected ? slotSelected : dimmed ? slotDisabled : slotNormal, dimmed);
        }

        // Info strip
        int show = mixing ? mixFirst : selected;
        if (show >= 0 && show < inventory.Count)
        {
            var it = inventory.items[show];
            infoIcon.enabled = true; infoIcon.sprite = it.icon;
            infoName.text = it.itemName;
            infoDescription.text = $"{it.description}  ·  weight {it.weight}";
        }
        else
        {
            infoIcon.enabled = false;
            infoName.text = "";
            infoDescription.text = mixing ? "" : "Click an item";
        }
    }

    // ---------------------------------------------------------------- keys
    static bool TogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.iKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.I);
#endif
    }

    static bool CancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
