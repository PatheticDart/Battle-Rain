using Unity.Netcode;
using UnityEngine;

public class GuardianHealth : HasHealth
{
    [Header("Guardian Settings")]
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private int baseRepairCost = 100;
    [SerializeField] private int baseUpgradeCost = 250;
    [SerializeField] private float repairPercent = 0.25f;
    [SerializeField] private float maxHealthUpgradeMultiplier = 1.25f;
    [SerializeField] private GameObject activeVisual;
    [SerializeField] private GameObject disabledVisual;

    private NetworkEnemyWaveSpawner waveSpawner;

    public float InteractionRadius => interactionRadius;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        waveSpawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (IsServer) SetGuardianStateClientRpc(!IsDestroyed);
    }

    protected override void OnHealthDepleted()
    {
        SetGuardianStateClientRpc(false);
        if (waveSpawner != null) waveSpawner.CheckGuardianObjective();
    }

    protected override void OnHealthRepaired()
    {
        SetGuardianStateClientRpc(true);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRepairOrUpgradeServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(
                rpcParams.Receive.SenderClientId, out NetworkClient client) || client.PlayerObject == null)
            return;

        if (Vector2.Distance(client.PlayerObject.transform.position, transform.position) > interactionRadius)
            return;

        PlayerScore playerScore = client.PlayerObject.GetComponent<PlayerScore>();
        if (playerScore == null) return;

        int round = waveSpawner != null ? Mathf.Max(1, waveSpawner.CurrentWave) : 1;
        int repairCost = GetExponentialCost(baseRepairCost, round);
        int upgradeCost = GetExponentialCost(baseUpgradeCost, round);

        if (currentHealth.Value < maxHealth)
        {
            if (playerScore.runCurrency.Value < repairCost) return;
            playerScore.runCurrency.Value -= repairCost;
            Repair(Mathf.Max(1, Mathf.CeilToInt(maxHealth * repairPercent)));
        }
        else
        {
            if (playerScore.runCurrency.Value < upgradeCost) return;
            playerScore.runCurrency.Value -= upgradeCost;
            SetMaxHealth(Mathf.CeilToInt(maxHealth * maxHealthUpgradeMultiplier));
        }
    }

    private int GetExponentialCost(int baseCost, int round)
    {
        return Mathf.CeilToInt(baseCost * Mathf.Pow(2f, Mathf.Max(0, round - 1)));
    }

    [ClientRpc]
    private void SetGuardianStateClientRpc(bool active)
    {
        if (activeVisual != null) activeVisual.SetActive(active);
        if (disabledVisual != null) disabledVisual.SetActive(!active);
    }
}
