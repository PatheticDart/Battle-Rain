using Unity.Netcode;
using UnityEngine;

public class NetworkDefense : NetworkBehaviour
{
    [Header("Turret Visuals & Setup")]
    [SerializeField] private GameObject turretVisual;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject projectilePrefab;

    [Header("Base Combat Stats")]
    [SerializeField] private float attackRange = 7f;
    [SerializeField] private float baseAttackCooldown = 0.75f;
    [SerializeField] private int baseDamage = 10;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float rotationSpeed = 200f;
    
    [Header("Progression / Economy")]
    public int baseUpgradeCost = 250;
    public int maxLevel = 5;

    public NetworkVariable<int> currentLevel = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // 👈 NEW: Sync the exact angle the server is aiming at to all clients
    public NetworkVariable<float> targetRotationAngle = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float nextFireTime;
    private Transform currentTarget;
    private Rigidbody2D targetRb;

    public override void OnNetworkSpawn()
    {
        turretVisual.SetActive(currentLevel.Value > 0);
        currentLevel.OnValueChanged += (prev, current) => turretVisual.SetActive(current > 0);
    }

    public override void OnNetworkDespawn()
    {
        currentLevel.OnValueChanged -= (prev, current) => turretVisual.SetActive(current > 0);
    }

    public void ResetDefense()
    {
        if (!IsServer) return;
        currentLevel.Value = 0; 
    }

    private void Update()
    {
        if (currentLevel.Value == 0) return; // Inactive

        // 👈 FIX: Only the server calculates targets and updates the angle
        if (IsServer)
        {
            FindTarget();
            if (currentTarget != null)
            {
                CalculateTargetAngle();
                if (Time.time >= nextFireTime)
                {
                    FireProjectile();
                    nextFireTime = Time.time + GetCurrentCooldown();
                }
            }
        }
        
        // 👈 FIX: BOTH the Server and Client simply animate the rotation to match the synced angle
        ApplyRotation();
    }

    private void FindTarget()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, attackRange);
        float closestDistance = Mathf.Infinity;
        Transform bestTarget = null;
        Rigidbody2D bestRb = null;

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Enemy"))
            {
                float dist = Vector2.Distance(transform.position, hit.transform.position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    bestTarget = hit.transform;
                    bestRb = hit.GetComponent<Rigidbody2D>();
                }
            }
        }

        currentTarget = bestTarget;
        targetRb = bestRb;
    }

    private void CalculateTargetAngle()
    {
        Vector2 aimPosition = currentTarget.position;

        if (targetRb != null)
        {
            float distanceToTarget = Vector2.Distance(firePoint.position, currentTarget.position);
            float timeToReachTarget = distanceToTarget / projectileSpeed;
            aimPosition = (Vector2)currentTarget.position + (targetRb.linearVelocity * timeToReachTarget);
        }

        Vector2 lookDirection = aimPosition - (Vector2)turretVisual.transform.position;
        targetRotationAngle.Value = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
    }

    private void ApplyRotation()
    {
        // Smoothly rotate the visual towards the server-approved angle
        Quaternion targetRot = Quaternion.Euler(0, 0, targetRotationAngle.Value);
        turretVisual.transform.rotation = Quaternion.RotateTowards(turretVisual.transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    private void FireProjectile()
    {
        if (projectilePrefab == null || firePoint == null) return;

        GameObject bullet = Instantiate(projectilePrefab, firePoint.position, turretVisual.transform.rotation);
        
        if (bullet.TryGetComponent(out ProjectileBehavior projScript))
        {
            projScript.Configure(projectileSpeed, GetCurrentDamage(), NetworkManager.ServerClientId);
        }

        bullet.GetComponent<NetworkObject>().Spawn(true);
        Destroy(bullet, 3f);

        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlayTurretClientRpc(firePoint.position);
        }
    }

    private int GetCurrentDamage()
    {
        return baseDamage + ((currentLevel.Value - 1) * 5);
    }

    private float GetCurrentCooldown()
    {
        float reduction = (currentLevel.Value - 1) * 0.1f;
        return Mathf.Max(0.2f, baseAttackCooldown - reduction);
    }

    public int GetNextUpgradeCost()
    {
        return baseUpgradeCost * (currentLevel.Value + 1);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestInteractServerRpc(ServerRpcParams rpcParams = default)
    {
        if (currentLevel.Value >= maxLevel) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client))
        {
            if (client.PlayerObject.TryGetComponent(out PlayerScore playerScore))
            {
                int cost = GetNextUpgradeCost();
                if (playerScore.score.Value >= cost)
                {
                    playerScore.score.Value -= cost;
                    currentLevel.Value++;
                }
            }
        }
    }
}