using Unity.Netcode;
using UnityEngine;

public class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int scoreValue = 50; // Points given on kill

    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer) currentHealth.Value = maxHealth;
    }

    // Called strictly by the Server via the Raycast Projectile
    public void TakeDamage(int damage, ulong shooterClientId)
    {
        if (!IsServer) return;

        currentHealth.Value -= damage;

        if (currentHealth.Value <= 0)
        {
            Die(shooterClientId);
        }
    }

    private void Die(ulong shooterClientId)
    {
        if (IsServer)
        {
            // 👈 NEW: Trigger the explosion effect and sound to all clients!
            if (NetworkEffectManager.Instance != null)
            {
                NetworkEffectManager.Instance.PlayExplosionClientRpc(transform.position);
            }

            // Look up the specific Player network asset using the client identity payload
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterClientId, out var networkClient))
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