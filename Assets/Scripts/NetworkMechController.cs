using Unity.Netcode;
using UnityEngine;

public class NetworkMechController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lowerRotationSpeed = 400f; // Speed of legs turning
    [SerializeField] private float upperRotationSpeed = 250f; // Speed of torso tracking mouse

    [Header("Body Parts")]
    [SerializeField] private Transform lowerBody;
    [SerializeField] private Transform upperBody;

    private Rigidbody2D rb;
    private Camera mainCamera;
    private Animator lowerAnimator;
    private Vector2 moveInput;
    private Vector2 mouseWorldPosition;

    // Direct network sync variables - Owner writes data, everyone else reads it.
    private readonly NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<float> netLowerBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<float> netUpperBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Sync animation state smoothly across the network
    private readonly NetworkVariable<bool> netIsWalking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;

        // Automatically find the Animator component on the legs/lower body
        if (lowerBody != null)
        {
            lowerAnimator = lowerBody.GetComponent<Animator>();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && CameraFollow2D.Instance != null)
        {
            CameraFollow2D.Instance.SetTarget(this.transform);
        }

        if (!IsOwner)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }
    }

    private void Update()
    {
        // Force the root container to never tip or spin from physics forces
        transform.rotation = Quaternion.identity;

        // ANIMATION SYNC: Update the animator parameter for both local and network players
        if (lowerAnimator != null)
        {
            lowerAnimator.SetBool("IsWalking", netIsWalking.Value);
        }

        // If it's a teammate's mech, read their synced network data smoothly
        if (!IsOwner)
        {
            transform.position = Vector2.Lerp(transform.position, netPosition.Value, Time.deltaTime * 15f);
            lowerBody.rotation = Quaternion.Euler(0, 0, netLowerBodyAngle.Value);
            upperBody.rotation = Quaternion.Euler(0, 0, netUpperBodyAngle.Value);
            return;
        }

        // --- LOCAL PLAYER LOGIC (Runs with ZERO latency) ---
        HandleInput();
        HandleDefenseInteraction();
        HandleUpperBodyRotation();
        HandleLowerBodyRotation();

        // Update network data so other players can see us
        netPosition.Value = rb.position;
        netIsWalking.Value = moveInput.sqrMagnitude > 0.01f;
    }

    private void HandleDefenseInteraction()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        NetworkDefense[] defenses = FindObjectsByType<NetworkDefense>(FindObjectsSortMode.None);
        NetworkDefense closest = null;
        float closestDistance = 2.5f * 2.5f;
        foreach (NetworkDefense defense in defenses)
        {
            float distance = ((Vector2)defense.transform.position - rb.position).sqrMagnitude;
            if (distance <= closestDistance)
            {
                closestDistance = distance;
                closest = defense;
            }
        }

        if (closest != null)
        {
            closest.RequestUpgradeServerRpc();
            return;
        }

        GuardianHealth[] guardians = FindObjectsByType<GuardianHealth>(FindObjectsSortMode.None);
        GuardianHealth closestGuardian = null;
        float closestGuardianDistance = float.MaxValue;
        foreach (GuardianHealth guardian in guardians)
        {
            float distance = ((Vector2)guardian.transform.position - rb.position).sqrMagnitude;
            if (distance <= guardian.InteractionRadius * guardian.InteractionRadius && distance < closestGuardianDistance)
            {
                closestGuardianDistance = distance;
                closestGuardian = guardian;
            }
        }

        if (closestGuardian != null) closestGuardian.RequestRepairOrUpgradeServerRpc();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        // Client Authoritative movement: Moves instantly on your screen without waiting for the server
        rb.linearVelocity = moveInput * moveSpeed;
    }

    private void HandleInput()
    {
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");
        moveInput = moveInput.normalized;

        mouseWorldPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }

    private void HandleLowerBodyRotation()
    {
        if (moveInput.sqrMagnitude > 0.01f)
        {
            float targetAngle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);

            // Mech Weight: Interpolate smoothly toward the driving direction
            lowerBody.rotation = Quaternion.RotateTowards(lowerBody.rotation, targetRotation, lowerRotationSpeed * Time.deltaTime);
            netLowerBodyAngle.Value = lowerBody.rotation.eulerAngles.z;
        }
    }

    private void HandleUpperBodyRotation()
    {
        Vector2 lookDirection = mouseWorldPosition - (Vector2)upperBody.position;
        float targetAngle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
        Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);

        // Mech Weight: Torso catches up smoothly to where your cursor is aiming
        upperBody.rotation = Quaternion.RotateTowards(upperBody.rotation, targetRotation, upperRotationSpeed * Time.deltaTime);
        netUpperBodyAngle.Value = upperBody.rotation.eulerAngles.z;
    }
}
