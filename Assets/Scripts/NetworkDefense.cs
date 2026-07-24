using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative defense that can be placed on an in-scene NetworkObject.
/// The defense attacks the nearest enemy and can be upgraded by a nearby player.
/// </summary>
public class NetworkDefense : NetworkBehaviour
{
    [Header("Defense Settings")]
    [SerializeField] private int upgradeCost = 250;
    [SerializeField] private int baseDamage = 10;
    [SerializeField] private float attackRange = 7f;
    [SerializeField] private float attackCooldown = 0.75f;
    [SerializeField] private int maxLevel = 5;
    [SerializeField] private GameObject[] levelVisuals;

    public NetworkVariable<int> level = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float nextAttackTime;

    private void Update()
    {
        if (!IsServer || level.Value <= 0 || Time.time < nextAttackTime) return;

        EnemyHealth target = FindNearestEnemy();
        if (target == null) return;

        target.TakeDamage(baseDamage * level.Value, 0);
        nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown / Mathf.Sqrt(level.Value));
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer || level.Value >= maxLevel) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(
                rpcParams.Receive.SenderClientId, out NetworkClient client) || client.PlayerObject == null)
            return;

        if (Vector2.Distance(client.PlayerObject.transform.position, transform.position) > 2.5f) return;

        PlayerScore playerScore = client.PlayerObject.GetComponent<PlayerScore>();
        if (playerScore == null || playerScore.score.Value < upgradeCost) return;

        playerScore.score.Value -= upgradeCost;
        level.Value++;
        UpdateVisualsClientRpc(level.Value);
    }

    private EnemyHealth FindNearestEnemy()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        EnemyHealth nearest = null;
        float nearestDistance = attackRange * attackRange;

        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null || enemy.currentHealth.Value <= 0) continue;
            float distance = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
            }
        }
        return nearest;
    }

    [ClientRpc]
    private void UpdateVisualsClientRpc(int newLevel)
    {
        if (levelVisuals == null || levelVisuals.Length == 0) return;
        for (int i = 0; i < levelVisuals.Length; i++)
            if (levelVisuals[i] != null) levelVisuals[i].SetActive(i == newLevel - 1);
    }
}
