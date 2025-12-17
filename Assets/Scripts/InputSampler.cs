using UnityEngine;
using UnityEngine.InputSystem;

public class InputSampler : MonoBehaviour
{
    [SerializeField] private PlayerController player;

    [Header("Fire Settings")]
    [SerializeField] private int fireCooldownTicks = 5; // 30 TPS → 6 shots/sec

    private Vector2 moveInput;
    private Vector2 aimInput;
    private bool fireHeld;
    private bool firePressedThisFrame;

    private int lastSampledTick = -1;
    private int lastFireTick = -999;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();
    }

    /* ================= INPUT CALLBACKS ================= */

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        aimInput = context.ReadValue<Vector2>(); // screen position
    }

    public void OnFire(InputAction.CallbackContext context)
    {
       //Debug.Log($"OnFire called - phase: {context.phase}");

        if (context.started)
            firePressedThisFrame = true;

        fireHeld = context.ReadValue<float>() > 0.5f;
    }

    /* ================= TICK SAMPLING ================= */

    private void Update()
    {
        int tick = TickManager.Instance.CurrentTick;


        if (tick == lastSampledTick)
            return;

        lastSampledTick = tick;

        // Movement + look
        player.SimulateMovement(moveInput);
        player.SimulateLookAtCursor(aimInput);

        // Shooting
        bool canFire = tick - lastFireTick >= fireCooldownTicks;

        if (canFire && (firePressedThisFrame || fireHeld))
        {
            //Debug.Log("FIRING AT TICK " + tick);

            lastFireTick = tick;
            player.SimulateShoot(aimInput);
        }

        firePressedThisFrame = false;
    }
}
