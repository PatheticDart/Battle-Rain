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
    private int initialMaxHealth; // 👈 NEW: Stores the starting max health

    public float InteractionRadius => interactionRadius;

    public bool IsDamaged => currentHealth.Value < maxHealth;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        initialMaxHealth = maxHealth; // 👈 NEW: Save the base max health on spawn
        waveSpawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (IsServer) SetGuardianStateClientRpc(!IsDestroyed);
    }

    // 👈 NEW: Public method for your Game Manager to call on restart
    public void ResetGuardian()
    {
        if (!IsServer) return;
        
        SetMaxHealth(initialMaxHealth); // Strip upgrades
        
        // 1. Give it 1 HP so the base class no longer sees it as "dead"
        currentHealth.Value = 1; 
        
        // 2. Now use the official Repair method so all base events (like OnHealthRepaired) trigger properly
        Repair(initialMaxHealth);
        
        // 3. Force the visual active locally on the Host/Server immediately 
        // to prevent it from getting lost in the restart frame's RPC queue
        if (activeVisual != null) activeVisual.SetActive(true);
        if (disabledVisual != null) disabledVisual.SetActive(false);
        
        // 4. Ensure all clients get the message
        SetGuardianStateClientRpc(true);
    }

    protected override void OnHealthDepleted()
    {
        SetGuardianStateClientRpc(false);
        if (waveSpawner != null) waveSpawner.CheckGuardianObjective();

        if (IsServer)
        {
            PlayerHealth[] players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            foreach (PlayerHealth player in players)
            {
                if (!player.isDead.Value)
                {
                    player.TakeDamage(999999); 
                }
            }
        }
    }

    protected override void OnHealthRepaired()
    {
        SetGuardianStateClientRpc(true);
    }

    public int GetCurrentRepairCost()
    {
        int round = waveSpawner != null ? Mathf.Max(1, waveSpawner.CurrentWave) : 1;
        return GetExponentialCost(baseRepairCost, round);
    }

    public int GetCurrentUpgradeCost()
    {
        int round = waveSpawner != null ? Mathf.Max(1, waveSpawner.CurrentWave) : 1;
        return GetExponentialCost(baseUpgradeCost, round);
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

        int repairCost = GetCurrentRepairCost();
        int upgradeCost = GetCurrentUpgradeCost();

        if (currentHealth.Value < maxHealth)
        {
            if (playerScore.score.Value < repairCost) return;
            playerScore.score.Value -= repairCost;
            Repair(Mathf.Max(1, Mathf.CeilToInt(maxHealth * repairPercent)));
        }
        else
        {
            if (playerScore.score.Value < upgradeCost) return;
            playerScore.score.Value -= upgradeCost;
            SetMaxHealth(Mathf.CeilToInt(maxHealth * maxHealthUpgradeMultiplier));
        }
    }

    private int GetExponentialCost(int baseCost, int round)
    {
        return Mathf.CeilToInt(baseCost * Mathf.Pow(1.1f, Mathf.Max(0, round - 1)));
    }

    [ClientRpc]
    private void SetGuardianStateClientRpc(bool active)
    {
        if (activeVisual != null) activeVisual.SetActive(active);
        if (disabledVisual != null) disabledVisual.SetActive(!active);
    }
}