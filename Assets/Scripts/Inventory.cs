using System;
using System.Collections.Generic;
using UnityEngine;

// The bag itself (no visuals). Put this on your player (HeroKnight).
public class Inventory : MonoBehaviour
{
    [Serializable]
    public class MixRecipe
    {
        public ItemData itemA;
        public ItemData itemB;
        public ItemData result;
    }

    [Header("Bag size")]
    public int slotCount = 12;
    public int maxWeight = 12;

    [Header("Recipes (order doesn't matter: A+B is the same as B+A)")]
    public List<MixRecipe> recipes = new List<MixRecipe>();

    [Header("Player")]
    public PlayerController player;

    // The items in the bag, in slot order
    public List<ItemData> items = new List<ItemData>();

    // The bag UI listens to this to redraw itself
    public event Action OnChanged;

    public int Count => items.Count;

    public int CurrentWeight
    {
        get { int w = 0; foreach (var it in items) w += it.weight; return w; }
    }

    void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>();
    }

    // ---------------------------------------------------------------- adding / removing
    public bool CanAdd(ItemData item)
    {
        return items.Count < slotCount && CurrentWeight + item.weight <= maxWeight;
    }

    public bool Add(ItemData item)
    {
        if (!CanAdd(item)) return false;
        items.Add(item);
        OnChanged?.Invoke();
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index);
        OnChanged?.Invoke();
    }

    // ---------------------------------------------------------------- actions
    public bool Eat(int index)
    {
        if (index < 0 || index >= items.Count) return false;
        var item = items[index];
        if (!item.canEat) return false;

        if (player != null) PlayerBridge.Heal(player, item.healAmount);
        RemoveAt(index);
        return true;
    }

    public void Drop(int index)
    {
        if (index < 0 || index >= items.Count) return;
        var item = items[index];
        if (item.worldPrefab != null)
        {
            var go = Instantiate(item.worldPrefab, transform.position + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            var pickup = go.GetComponent<ItemPickup>();
            if (pickup != null) pickup.IgnorePickupFor(1.5f); // so you don't grab it straight back
        }
        RemoveAt(index);
    }

    // Returns the recipe result for two items, or null if they don't mix
    public ItemData GetMixResult(ItemData a, ItemData b)
    {
        foreach (var r in recipes)
        {
            if ((r.itemA == a && r.itemB == b) || (r.itemA == b && r.itemB == a))
                return r.result;
        }
        return null;
    }

    // Mixes two slots. Returns the index of the new item, or -1 if it didn't work.
    public int Mix(int indexA, int indexB)
    {
        if (indexA == indexB) return -1;
        if (indexA < 0 || indexB < 0 || indexA >= items.Count || indexB >= items.Count) return -1;

        var result = GetMixResult(items[indexA], items[indexB]);
        if (result == null) return -1;

        // Remove the higher index first so the lower one doesn't shift
        items.RemoveAt(Mathf.Max(indexA, indexB));
        items.RemoveAt(Mathf.Min(indexA, indexB));

        int newIndex = Mathf.Min(indexA, indexB);
        items.Insert(newIndex, result);
        OnChanged?.Invoke();
        return newIndex;
    }
}
