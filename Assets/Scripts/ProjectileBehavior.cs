using Unity.Netcode;
using UnityEngine;

public class ProjectileBehavior : NetworkBehaviour
{
    [HideInInspector] public ulong ownerClientId;
    [HideInInspector] public float speed = 12f;
    [HideInInspector] public int damage = 10;

    // Projectiles are moved locally on clients for smooth visuals, so the
    // runtime speed selected by the server must be replicated with the spawn.
    private readonly NetworkVariable<float> replicatedSpeed = new NetworkVariable<float>(
        12f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public void Configure(float projectileSpeed, int projectileDamage, ulong projectileOwnerClientId)
    {
        speed = projectileSpeed;
        damage = projectileDamage;
        ownerClientId = projectileOwnerClientId;
        replicatedSpeed.Value = projectileSpeed;
    }

    public override void OnNetworkSpawn()
    {
        speed = replicatedSpeed.Value;
        replicatedSpeed.OnValueChanged += OnReplicatedSpeedChanged;
    }

    public override void OnNetworkDespawn()
    {
        replicatedSpeed.OnValueChanged -= OnReplicatedSpeedChanged;
    }

    private void OnReplicatedSpeedChanged(float previousSpeed, float newSpeed)
    {
        speed = newSpeed;
    }

    // 👈 FIXED: Server-authoritative raycasting shifted to FixedUpdate to stay perfectly in line with physics frames
    private void FixedUpdate()
    {
        if (!IsServer) return;

        float stepDistance = speed * Time.fixedDeltaTime;

        // Force Unity to map any recent network transform translations directly onto collider structures
        Physics2D.SyncTransforms();

        RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.up, stepDistance);

        if (hit.collider != null)
        {
            bool isEnemyBullet = (gameObject.CompareTag("EnemyBullet") || ownerClientId == 999999);

            // Hit checks
            bool hitAnEnemy = hit.collider.CompareTag("Enemy") || (hit.collider.transform.parent != null && hit.collider.transform.parent.CompareTag("Enemy"));
            bool hitAPlayer = hit.collider.CompareTag("Player") || hit.collider.gameObject.layer == LayerMask.NameToLayer("Player") ||
                              (hit.collider.transform.parent != null && (hit.collider.transform.parent.CompareTag("Player") || hit.collider.transform.parent.gameObject.layer == LayerMask.NameToLayer("Player")));

            // Hit an Enemy
            if (hitAnEnemy)
            {
                if (!isEnemyBullet)
                {
                    EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
                    if (enemyHealth != null) enemyHealth.TakeDamage(damage, ownerClientId);

                    TriggerSparkAndDestroy(hit.point);
                    return;
                }
            }
            // Hit a Player
            else if (hitAPlayer)
            {
                if (isEnemyBullet)
                {
                    PlayerHealth playerHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        playerHealth.TakeDamage(damage);
                    }

                    TriggerSparkAndDestroy(hit.point);
                    return;
                }
            }
            // Hit an Obstacle
            else if (hit.collider.CompareTag("Obstacle") || hit.collider.gameObject.layer == LayerMask.NameToLayer("Obstacles"))
            {
                TriggerSparkAndDestroy(hit.point);
                return;
            }
        }

        transform.position += transform.up * stepDistance;
    }

    private void Update()
    {
        // Non-server instances handle local bullet visualization smoothly in standard Update frames
        if (!IsServer)
        {
            float stepDistance = speed * Time.deltaTime;
            transform.position += transform.up * stepDistance;
        }
    }

    private void TriggerSparkAndDestroy(Vector2 hitPoint)
    {
        // Send the impact through the projectile's own network object before
        // despawning it. The previous implementation sent the RPC through a
        // separate scene singleton, which could be skipped for clients when
        // the projectile despawn was processed in the same network tick.
        PlayHitEffectClientRpc(hitPoint);

        if (TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    [ClientRpc]
    private void PlayHitEffectClientRpc(Vector2 hitPoint)
    {
        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlaySparkLocal(hitPoint);
        }
    }
}
