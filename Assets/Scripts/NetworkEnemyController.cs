using Unity.Netcode;
using UnityEngine;

public class NetworkEnemyController : NetworkBehaviour
{
    [Header("AI Movement Settings")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float minEngagementDistance = 4f;
    [SerializeField] private float maxEngagementDistance = 8f;
    [SerializeField] private float lowerRotationSpeed = 300f;
    [SerializeField] private float upperRotationSpeed = 200f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float sensorLength = 2.0f;      // How far ahead the tank looks for walls
    [SerializeField] private float sideSensorAngle = 30f;    // Angle of diagonal left/right whisker sensors
    [SerializeField] private LayerMask obstacleLayer;        // Set this to your "Obstacles" layer in the inspector

    [Header("Body Parts")]
    [SerializeField] private Transform lowerBody;
    [SerializeField] private Transform upperBody;

    [HideInInspector] public Transform currentTarget;

    private Rigidbody2D rb;
    private Vector2 currentMoveInput;

    private readonly NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netLowerBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netUpperBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }
    }

    private void Update()
    {
        transform.rotation = Quaternion.identity;

        if (!IsServer)
        {
            transform.position = Vector2.Lerp(transform.position, netPosition.Value, Time.deltaTime * 15f);
            lowerBody.rotation = Quaternion.Euler(0, 0, netLowerBodyAngle.Value);
            upperBody.rotation = Quaternion.Euler(0, 0, netUpperBodyAngle.Value);
            return;
        }

        FindClosestPlayer();
        CalculateMovement();
        HandleRotations();

        netPosition.Value = rb.position;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        rb.linearVelocity = currentMoveInput * moveSpeed;
    }

    private void FindClosestPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float closestDistance = Mathf.Infinity;
        Transform closestPlayer = null;

        foreach (GameObject player in players)
        {
            float distance = Vector2.Distance(transform.position, player.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player.transform;
            }
        }
        currentTarget = closestPlayer;
    }

    private void CalculateMovement()
    {
        if (currentTarget == null)
        {
            currentMoveInput = Vector2.zero;
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.position);
        Vector2 directionToTarget = (currentTarget.position - transform.position).normalized;

        Vector2 desiredDirection = Vector2.zero;

        if (distanceToTarget > maxEngagementDistance)
        {
            desiredDirection = directionToTarget;
        }
        else if (distanceToTarget < minEngagementDistance)
        {
            desiredDirection = -directionToTarget;
        }
        else
        {
            desiredDirection = Vector2.zero; // Comfortable sweet spot, don't move forward/backward
        }

        // Only apply avoidance math if the tank actually wants to travel somewhere
        if (desiredDirection != Vector2.zero)
        {
            currentMoveInput = ApplyObstacleAvoidance(desiredDirection);
        }
        else
        {
            currentMoveInput = Vector2.zero;
        }
    }

    private Vector2 ApplyObstacleAvoidance(Vector2 moveDir)
    {
        // Generate right/left diagonal whiskers
        Vector2 leftWhiskerDir = Quaternion.Euler(0, 0, sideSensorAngle) * moveDir;
        Vector2 rightWhiskerDir = Quaternion.Euler(0, 0, -sideSensorAngle) * moveDir;

        // Cast out rays to probe for walls
        RaycastHit2D centerHit = Physics2D.Raycast(transform.position, moveDir, sensorLength, obstacleLayer);
        RaycastHit2D leftHit = Physics2D.Raycast(transform.position, leftWhiskerDir, sensorLength * 0.75f, obstacleLayer);
        RaycastHit2D rightHit = Physics2D.Raycast(transform.position, rightWhiskerDir, sensorLength * 0.75f, obstacleLayer);

        // Debug visualization lines inside Unity's scene editor
        Debug.DrawRay(transform.position, moveDir * sensorLength, centerHit ? Color.red : Color.green);
        Debug.DrawRay(transform.position, leftWhiskerDir * (sensorLength * 0.75f), leftHit ? Color.red : Color.green);
        Debug.DrawRay(transform.position, rightWhiskerDir * (sensorLength * 0.75f), rightHit ? Color.red : Color.green);

        Vector2 avoidanceDir = Vector2.zero;

        // CENTER HIT: The tank is driving straight into a flat wall
        if (centerHit.collider != null)
        {
            // Calculate the "Tangent" (the line perfectly parallel to the wall)
            Vector2 slideDirection = new Vector2(-centerHit.normal.y, centerHit.normal.x);

            // Make sure the tank picks the slide direction that gets it closer to the player, not further away
            if (Vector2.Dot(slideDirection, moveDir) < 0)
            {
                slideDirection = -slideDirection;
            }

            // Heavily prioritize sliding, with a slight push off the wall to prevent scraping
            avoidanceDir = (slideDirection * 2f) + (centerHit.normal * 1f);
        }
        // LEFT HIT: Glancing blow on the left, steer right
        else if (leftHit.collider != null)
        {
            avoidanceDir = rightWhiskerDir + leftHit.normal;
        }
        // RIGHT HIT: Glancing blow on the right, steer left
        else if (rightHit.collider != null)
        {
            avoidanceDir = leftWhiskerDir + rightHit.normal;
        }

        // If we need to avoid something, aggressively overwrite the original movement direction
        if (avoidanceDir != Vector2.zero)
        {
            // We use Lerp to smoothly blend 80% avoidance and 20% original intent so it doesn't snap unnaturally
            return Vector2.Lerp(moveDir, avoidanceDir.normalized, 0.8f).normalized;
        }

        return moveDir; // Path is clear, keep going straight!
    }

    private void HandleRotations()
    {
        if (currentMoveInput.sqrMagnitude > 0.01f)
        {
            float targetAngle = Mathf.Atan2(currentMoveInput.y, currentMoveInput.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRot = Quaternion.Euler(0, 0, targetAngle);
            lowerBody.rotation = Quaternion.RotateTowards(lowerBody.rotation, targetRot, lowerRotationSpeed * Time.deltaTime);
            netLowerBodyAngle.Value = lowerBody.rotation.eulerAngles.z;
        }

        if (currentTarget != null)
        {
            Vector2 lookDirection = currentTarget.position - upperBody.position;
            float targetAngle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRot = Quaternion.Euler(0, 0, targetAngle);
            upperBody.rotation = Quaternion.RotateTowards(upperBody.rotation, targetRot, upperRotationSpeed * Time.deltaTime);
            netUpperBodyAngle.Value = upperBody.rotation.eulerAngles.z;
        }
    }
}