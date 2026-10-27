using UnityEngine;
using UnityEngine.UI;

// Shows the player's health as hearts (top-left).
public class HeartsUI : MonoBehaviour
{
    public PlayerController player;
    public Image[] hearts;
    public Sprite fullHeart;
    public Sprite emptyHeart;

    void Update()
    {
        if (player == null) return;
        int hp = PlayerBridge.GetHealth(player);
        for (int i = 0; i < hearts.Length; i++)
        {
            hearts[i].gameObject.SetActive(i < player.maxHealth);
            hearts[i].sprite = i < hp ? fullHeart : emptyHeart;
        }
    }
}
