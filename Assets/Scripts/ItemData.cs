using UnityEngine;

// One of these per item type (Red Apple, Banana, Power Core...).
// Make one in the Project window: right-click > Create > Dungeon Salvager > Item
[CreateAssetMenu(fileName = "NewItem", menuName = "Dungeon Salvager/Item")]
public class ItemData : ScriptableObject
{
    public string itemName = "New Item";
    public Sprite icon;
    [TextArea] public string description = "Restores 1 heart";

    [Header("Eating")]
    [Tooltip("Tick for fruits and tonics. Leave off for scrap.")]
    public bool canEat = false;
    [Tooltip("How many hearts it gives back when eaten.")]
    public int healAmount = 1;

    [Header("Salvage")]
    [Tooltip("What it's worth if you extract with it.")]
    public int value = 0;
    [Tooltip("How heavy it is. The bag has a weight limit.")]
    public int weight = 1;

    [Header("Dropping")]
    [Tooltip("Optional: the pickup prefab to spawn when you DROP this item.")]
    public GameObject worldPrefab;
}
