using Unity.Netcode;
using UnityEngine;

public class EnemyHealth : HasHealth
{
    [SerializeField] private int scoreValue = 50; // Points given on kill

    // Called strictly by the Server via the Raycast Projectile
    public void TakeDamage(int damage, ulong shooterClientId)
    {
        if (!IsServer || IsDestroyed) return;
        pendingShooterClientId = shooterClientId;
        base.TakeDamage(damage);
    }

    private ulong pendingShooterClientId;

    protected override void OnHealthDepleted()
    {
        if (IsServer)
        {
            // 👈 NEW: Trigger the explosion effect and sound to all clients!
            if (NetworkEffectManager.Instance != null)
            {
                NetworkEffectManager.Instance.PlayExplosionClientRpc(transform.position);
            }

            // Look up the specific Player network asset using the client identity payload
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(pendingShooterClientId, out var networkClient))
            {
                if (networkClient.PlayerObject != null)
                {
                    PlayerScore shooterScore = networkClient.PlayerObject.GetComponent<PlayerScore>();
                    if (shooterScore != null)
                    {
                        // Safely add points on the authoritative server layer
                        shooterScore.score.Value += scoreValue;
                    }
                }
            }

            NetworkObject.Despawn(true); // Removes enemy from the network
        }
    }
}
