using Unity.Netcode;
using UnityEngine;

public class NetworkEnemyController : NetworkBehaviour
{
    [Header("AI Movement Settings")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float minEngagementDistance = 4f;
    [SerializeField] private float maxEngagementDistance = 8f;
    [SerializeField] private float playerApproachDistance = 15f; 
    [SerializeField] private float lowerRotationSpeed = 300f;
    [SerializeField] private float upperRotationSpeed = 200f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float sensorLength = 2.0f;      
    [SerializeField] private float sideSensorAngle = 30f;    
    [SerializeField] private LayerMask obstacleLayer;        

    [Header("Swarm Behavior")]
    [SerializeField] private float separationRadius = 1.5f; // How far to look for other enemies
    [SerializeField] private float separationWeight = 0.6f; // How strongly to push away from them

    [Header("Body Parts")]
    [SerializeField] private Transform lowerBody;
    [SerializeField] private Transform upperBody;

    [HideInInspector] public Transform currentTarget;

    private Rigidbody2D rb;
    private Vector2 currentMoveInput;
    private float nextGuardianAttackTime;
    private NetworkEnemyWaveSpawner waveSpawner; 
    
    private float avoidanceSteer; 

    private readonly NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netLowerBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netUpperBodyAngle = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // 👈 NEW: Randomize initial steer direction so swarms naturally split around walls
        avoidanceSteer = Random.value > 0.5f ? 1f : -1f; 
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }
        else
        {
            waveSpawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
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

        FindTarget(); 
        TryAttackGuardian();
        CalculateMovement();
        HandleRotations();

        netPosition.Value = rb.position;
    }

    private void TryAttackGuardian()
    {
        if (waveSpawner == null) waveSpawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (waveSpawner == null || Time.time < nextGuardianAttackTime) return;

        foreach (GuardianHealth guardian in waveSpawner.Guardians)
        {
            if (guardian == null || guardian.IsDestroyed) continue;
            if (Vector2.Distance(transform.position, guardian.transform.position) <= guardian.InteractionRadius)
            {
                guardian.TakeDamage(10);
                nextGuardianAttackTime = Time.time + 1f;
                break;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        rb.linearVelocity = currentMoveInput * moveSpeed;
    }

    private void FindTarget()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float closestPlayerDistance = Mathf.Infinity;
        Transform closestPlayer = null;

        foreach (GameObject player in players)
        {
            if (player.TryGetComponent<PlayerHealth>(out var playerHealth) && playerHealth.isDead.Value)
                continue;

            float distance = Vector2.Distance(transform.position, player.transform.position);
            if (distance < closestPlayerDistance)
            {
                closestPlayerDistance = distance;
                closestPlayer = player.transform;
            }
        }

        if (closestPlayer != null && closestPlayerDistance <= playerApproachDistance)
        {
            currentTarget = closestPlayer;
            return;
        }

        if (waveSpawner == null) waveSpawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (waveSpawner != null)
        {
            float closestGuardianDistance = Mathf.Infinity;
            Transform closestGuardian = null;

            foreach (GuardianHealth guardian in waveSpawner.Guardians)
            {
                if (guardian == null || guardian.IsDestroyed) continue;

                float distance = Vector2.Distance(transform.position, guardian.transform.position);
                if (distance < closestGuardianDistance)
                {
                    closestGuardianDistance = distance;
                    closestGuardian = guardian.transform;
                }
            }

            if (closestGuardian != null)
            {
                currentTarget = closestGuardian;
                return;
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

        float currentMaxEngagement = maxEngagementDistance;
        float currentMinEngagement = minEngagementDistance;

        if (currentTarget.TryGetComponent<GuardianHealth>(out var guardian))
        {
            currentMaxEngagement = guardian.InteractionRadius * 0.9f; 
            currentMinEngagement = 0f; 
        }

        Vector2 desiredDirection = Vector2.zero;

        if (distanceToTarget > currentMaxEngagement)
        {
            desiredDirection = directionToTarget;
        }
        else if (distanceToTarget < currentMinEngagement)
        {
            desiredDirection = -directionToTarget;
        }

        if (desiredDirection != Vector2.zero)
        {
            // 👈 NEW: Step 1. Apply Flocking/Separation so they don't clump
            desiredDirection = ApplySwarmSeparation(desiredDirection);
            
            // 👈 NEW: Step 2. Navigate around hard walls using the adjusted direction
            currentMoveInput = ApplyObstacleAvoidance(desiredDirection);
        }
        else
        {
            currentMoveInput = Vector2.zero;
        }
    }

    // 👈 NEW: Pushes enemies away from each other if they get too close
    private Vector2 ApplySwarmSeparation(Vector2 baseDirection)
    {
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(transform.position, separationRadius);
        Vector2 separationForce = Vector2.zero;
        int count = 0;

        foreach (var col in neighbors)
        {
            // Only care about other enemies, ignore self
            if (col.gameObject != gameObject && col.CompareTag("Enemy"))
            {
                Vector2 diff = transform.position - col.transform.position;
                float dist = diff.magnitude;
                if (dist > 0.01f) // Prevent divide by zero
                {
                    // The closer they are, the harder they push away
                    separationForce += (diff.normalized / dist);
                    count++;
                }
            }
        }

        if (count > 0)
        {
            separationForce /= count;
            // Blend the separation force into their intended path
            return (baseDirection + (separationForce * separationWeight)).normalized;
        }

        return baseDirection;
    }

    private Vector2 ApplyObstacleAvoidance(Vector2 moveDir)
    {
        Vector2 leftWhiskerDir = Quaternion.Euler(0, 0, sideSensorAngle) * moveDir;
        Vector2 rightWhiskerDir = Quaternion.Euler(0, 0, -sideSensorAngle) * moveDir;

        RaycastHit2D centerHit = GetObstacleHit(transform.position, moveDir, sensorLength);
        RaycastHit2D leftHit = GetObstacleHit(transform.position, leftWhiskerDir, sensorLength);
        RaycastHit2D rightHit = GetObstacleHit(transform.position, rightWhiskerDir, sensorLength);

        Debug.DrawRay(transform.position, moveDir * sensorLength, centerHit.collider != null ? Color.red : Color.green);
        Debug.DrawRay(transform.position, leftWhiskerDir * sensorLength, leftHit.collider != null ? Color.red : Color.green);
        Debug.DrawRay(transform.position, rightWhiskerDir * sensorLength, rightHit.collider != null ? Color.red : Color.green);

        Vector2 avoidanceDir = Vector2.zero;

        if (centerHit.collider != null)
        {
            Vector2 slideDirection = new Vector2(-centerHit.normal.y, centerHit.normal.x);
            float dot = Vector2.Dot(slideDirection, moveDir);

            // 👈 FIX: Increased this from 0.1f to 0.5f. 
            // They are now much more stubborn and will stick to their initial dodge 
            // direction unless the wall is at a very sharp, clear angle.
            if (Mathf.Abs(dot) > 0.5f)
            {
                avoidanceSteer = Mathf.Sign(dot);
            }

            slideDirection *= avoidanceSteer;
            
            avoidanceDir = (slideDirection * 2f) + (centerHit.normal * 1f);
        }
        else if (leftHit.collider != null)
        {
            avoidanceDir = rightWhiskerDir + leftHit.normal;
        }
        else if (rightHit.collider != null)
        {
            avoidanceDir = leftWhiskerDir + rightHit.normal;
        }

        if (avoidanceDir != Vector2.zero)
        {
            return Vector2.Lerp(moveDir, avoidanceDir.normalized, 0.8f).normalized;
        }

        return moveDir; 
    }

    private RaycastHit2D GetObstacleHit(Vector2 origin, Vector2 direction, float distance)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, distance, obstacleLayer);
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform)) 
                continue;

            if (currentTarget != null)
            {
                if (hit.transform == currentTarget || hit.transform.IsChildOf(currentTarget) || currentTarget.IsChildOf(hit.transform))
                    continue;
            }

            return hit;
        }
        return default;
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