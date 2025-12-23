using UnityEngine;
using UnityEngine.InputSystem;

public class InputSampler : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private LocalInputBuffer buffer;
    [SerializeField] private Camera cam;

    [Header("Fire Settings")]
    [SerializeField] private int fireCooldownTicks = 5;

    private Vector2 moveInput;
    private Vector2 lookScreenPos;
    private bool fireHeld;
    private bool firePressedThisFrame;

    private int lastFireTick = -999;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();

        if (buffer == null)
            buffer = GetComponent<LocalInputBuffer>();

        if (cam == null)
            cam = Camera.main;
    }
    
    // IMPORTANT: subscribe in Start, not OnEnable
    private void Start()
    {
        // Disable input sampling on remote players
        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[InputSampler] Disabling InputSampler on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            return;
        }
        
        Debug.Log("[InputSampler] Enabled for LOCAL player");

        if (TickManager.Instance == null)
        {
            Debug.LogError("TickManager.Instance is NULL in InputSampler.Start()");
            return;
        }

        TickManager.Instance.OnTick += HandleTick;
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;
    }

    /* ================= INPUT CALLBACKS ================= */

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookScreenPos = context.ReadValue<Vector2>();
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (context.started)
            firePressedThisFrame = true;

        fireHeld = context.ReadValue<float>() > 0.5f;
    }

    /* ================= TICK ================= */

    private void HandleTick(int tick)
    {
        if (buffer == null || cam == null || player == null)
            return;

        Vector2 aimDir = ComputeAimDirWorld(player.transform.position, lookScreenPos);

        bool canFire = tick - lastFireTick >= fireCooldownTicks;
        bool firePressed = false;

        if (canFire && (firePressedThisFrame || fireHeld))
        {
            lastFireTick = tick;
            firePressed = true;
        }

        InputCommand cmd = new InputCommand(
            tick,
            moveInput,
            aimDir,
            fireHeld,
            firePressed,
            NetworkManager.Instance != null ? NetworkManager.Instance.LocalPlayerId : ""
        );

        buffer.Store(cmd);

        firePressedThisFrame = false;
    }

    private Vector2 ComputeAimDirWorld(Vector3 playerWorldPos, Vector2 screenPos)
    {
        float zDistance = Mathf.Abs(cam.transform.position.z - playerWorldPos.z);
        Vector3 mouseWorld = cam.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, zDistance)
        );

        Vector2 dir = (Vector2)(mouseWorld - playerWorldPos);
        if (dir.sqrMagnitude < 0.000001f)
            return Vector2.right;

        return dir.normalized;
    }
}
