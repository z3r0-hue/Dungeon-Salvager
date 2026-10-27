using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Side-view / platformer controller. Works with the Animator built by PlayerSetupWizard.
// Controls:  A/D or ←/→ move  |  Space/W/↑ jump  |  J or Left Click = Attack1  |  K or Right Click = Attack2
//            H = take damage (test key, turn off "Debug Hurt Key" for the real game)
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float jumpForce = 14f;

    [Tooltip("Set by the RunManager: carrying heavy scrap lowers this below 1 and slows you down.")]
    [Range(0.1f, 1f)] public float speedMultiplier = 1f;

    [Tooltip("When ticked, the player ignores all input (used when a run ends).")]
    public bool inputLocked;

    [Header("Ground check")]
    public LayerMask groundLayers = ~0;
    public float groundCheckDepth = 0.1f;

    [Header("Health")]
    public int maxHealth = 3;
    [Tooltip("Press H in Play mode to test the hurt/death animations")]
    public bool debugHurtKey = true;

    // The HUD listens to these
    public event Action<int, int> HealthChanged; // (current, max)
    public event Action Died;

    Rigidbody2D rb;
    Animator anim;
    SpriteRenderer sr;
    Collider2D col;

    float moveX;
    bool jumpQueued;
    bool grounded;
    int health;
    bool dead;

    public bool IsDead => dead;
    public int Health => health;
    public int MaxHealth => maxHealth;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        health = maxHealth;
    }

    void Start() => HealthChanged?.Invoke(health, maxHealth);

    void Update()
    {
        if (dead || inputLocked)
        {
            moveX = 0f;
            jumpQueued = false;
            UpdateAnimator();
            return;
        }

        ReadInput(out moveX, out bool jump, out bool atk1, out bool atk2, out bool hurt);

        bool busy = IsBusy();
        if (jump && grounded && !busy) jumpQueued = true;
        if (atk1 && !busy && anim) anim.SetTrigger("Attack1");
        if (atk2 && !busy && anim) anim.SetTrigger("Attack2");
        if (hurt && debugHurtKey) TakeDamage(1);

        if (moveX != 0 && !busy && sr) sr.flipX = moveX < 0;

        UpdateAnimator();
    }

    void FixedUpdate()
    {
        grounded = CheckGrounded();

        Vector2 v = GetVelocity();
        bool lockMovement = dead || (IsBusy() && grounded); // stand still while attacking/hurt on the ground
        v.x = lockMovement ? 0f : moveX * moveSpeed * speedMultiplier;

        if (jumpQueued)
        {
            v.y = jumpForce;
            jumpQueued = false;
        }
        SetVelocity(v);
    }

    // Call this from enemies, spikes, etc.
    public void TakeDamage(int amount)
    {
        if (dead) return;
        health = Mathf.Max(0, health - amount);
        HealthChanged?.Invoke(health, maxHealth);
        if (health <= 0)
        {
            dead = true;
            if (anim) anim.SetTrigger("Die");
            Died?.Invoke();
        }
        else if (anim)
        {
            anim.SetTrigger("Hurt");
        }
    }

    // For healing items later (mixing scraps in Level 3)
    public void Heal(int amount)
    {
        if (dead) return;
        health = Mathf.Min(maxHealth, health + amount);
        HealthChanged?.Invoke(health, maxHealth);
    }

    void UpdateAnimator()
    {
        if (!anim) return;
        anim.SetFloat("Speed", dead ? 0f : Mathf.Abs(moveX));
        anim.SetBool("Grounded", grounded);
        anim.SetFloat("VerticalVelocity", GetVelocity().y);
    }

    bool IsBusy()
    {
        if (!anim) return false;
        var s = anim.GetCurrentAnimatorStateInfo(0);
        return s.IsName("Attack1") || s.IsName("Attack2") || s.IsName("TakeHit") || s.IsName("Death");
    }

    bool CheckGrounded()
    {
        if (!col) return false;
        Bounds b = col.bounds;
        Vector2 center = new Vector2(b.center.x, b.min.y - groundCheckDepth * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, groundCheckDepth);
        foreach (var h in Physics2D.OverlapBoxAll(center, size, 0f, groundLayers))
            if (h != null && !h.isTrigger && h.attachedRigidbody != rb) return true;
        return false;
    }

    void ReadInput(out float x, out bool jump, out bool atk1, out bool atk2, out bool hurt)
    {
        x = 0; jump = atk1 = atk2 = hurt = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var ms = Mouse.current;
        if (kb == null) return;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  x -= 1;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1;
        jump = kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
        atk1 = kb.jKey.wasPressedThisFrame || (ms != null && ms.leftButton.wasPressedThisFrame);
        atk2 = kb.kKey.wasPressedThisFrame || (ms != null && ms.rightButton.wasPressedThisFrame);
        hurt = kb.hKey.wasPressedThisFrame;
#else
        x = Input.GetAxisRaw("Horizontal");
        jump = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
        atk1 = Input.GetKeyDown(KeyCode.J) || Input.GetMouseButtonDown(0);
        atk2 = Input.GetKeyDown(KeyCode.K) || Input.GetMouseButtonDown(1);
        hurt = Input.GetKeyDown(KeyCode.H);
#endif
    }

    Vector2 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    void SetVelocity(Vector2 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}
