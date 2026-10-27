using System.Reflection;
using UnityEngine;

// Reads and changes the player's health without you having to edit PlayerController.
// (PlayerController keeps "health" private, so this looks it up by name.)
public static class PlayerBridge
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static int GetHealth(PlayerController p)
    {
        if (p == null) return 0;
        var f = typeof(PlayerController).GetField("health", Flags);
        return f != null ? (int)f.GetValue(p) : p.maxHealth;
    }

    public static void Heal(PlayerController p, int amount)
    {
        if (p == null || p.IsDead) return;
        var heal = typeof(PlayerController).GetMethod("Heal", Flags, null, new[] { typeof(int) }, null);
        if (heal != null) { heal.Invoke(p, new object[] { amount }); return; }

        var f = typeof(PlayerController).GetField("health", Flags);
        if (f != null) f.SetValue(p, Mathf.Min(p.maxHealth, (int)f.GetValue(p) + amount));
    }
}
