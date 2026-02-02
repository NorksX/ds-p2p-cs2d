using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

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
 
    private void Start()
    {
        // Delay the check until next frame to allow PlayerSpawner to set isLocalPlayer
        StartCoroutine(InitializeAfterSpawn());
    }
    
    private IEnumerator InitializeAfterSpawn()
    {
        // Wait one frame to ensure PlayerSpawner has set isLocalPlayer
        yield return null;
        
        // Disable input sampling on remote players
        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[InputSampler] Disabling InputSampler on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            yield break;
        }
        
        Debug.Log("[InputSampler] Enabled for LOCAL player");

        if (TickManager.Instance == null)
        {
            Debug.LogError("TickManager.Instance is NULL in InputSampler.Start()");
            yield break;
        }

        TickManager.Instance.OnTick += HandleTick;
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;
    }

//input callbacks
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

//tick handling
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
