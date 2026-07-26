using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum UpgradeType
{
    AdditionalWeapon,
    Damage,
    FireRate,
    HealthMax,
    HealthRepair,
    ReviveTeammate // 👈 NEW
}

public class PlayerUpgrades : NetworkBehaviour
{
    public NetworkVariable<int> weaponLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> damageMultiplier = new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> fireRateMultiplier = new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 👈 NEW: Called when the Host clicks Restart Match
    public void ResetUpgrades()
    {
        if (!IsServer) return;
        weaponLevel.Value = 1;
        damageMultiplier.Value = 1f;
        fireRateMultiplier.Value = 1f;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ApplyUpgradeServerRpc(UpgradeType upgradeType, ulong clientId)
    {
        if (OwnerClientId != clientId) return;

        switch (upgradeType)
        {
            case UpgradeType.AdditionalWeapon:
                if (weaponLevel.Value < 5) weaponLevel.Value++;
                break;
            case UpgradeType.Damage:
                damageMultiplier.Value *= 1.2f;
                break;
            case UpgradeType.FireRate:
                fireRateMultiplier.Value *= 1.1f;
                break;
            case UpgradeType.HealthMax:
                if (TryGetComponent(out PlayerHealth ph1)) ph1.ApplyHealthUpgrade(1.1f);
                break;
            case UpgradeType.HealthRepair:
                if (TryGetComponent(out PlayerHealth ph2)) ph2.RepairHealth(0.75f);
                break;
            case UpgradeType.ReviveTeammate:
                // 👈 NEW: Find all dead players, pick a random one, and revive them!
                List<PlayerHealth> deadPlayers = new List<PlayerHealth>();
                foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                {
                    if (p.isDead.Value) deadPlayers.Add(p);
                }

                if (deadPlayers.Count > 0)
                {
                    PlayerHealth randomDeadPlayer = deadPlayers[Random.Range(0, deadPlayers.Count)];

                    Vector3 spawnPos = Vector3.zero;
                    NetworkPlayerSpawner pSpawner = FindFirstObjectByType<NetworkPlayerSpawner>();
                    if (pSpawner != null && pSpawner.spawnPoints.Length > 0)
                    {
                        spawnPos = pSpawner.spawnPoints[Random.Range(0, pSpawner.spawnPoints.Length)].position;
                    }

                    randomDeadPlayer.Revive(spawnPos);
                }
                break;
        }
    }

    public List<Transform> GetActiveBarrels(Transform[] allBarrels)
    {
        List<Transform> active = new List<Transform>();
        int lvl = weaponLevel.Value;

        if (allBarrels == null || allBarrels.Length < 5) return active;

        if (lvl == 1) { active.Add(allBarrels[0]); }
        else if (lvl == 2) { active.Add(allBarrels[1]); active.Add(allBarrels[2]); }
        else if (lvl == 3) { active.Add(allBarrels[0]); active.Add(allBarrels[1]); active.Add(allBarrels[2]); }
        else if (lvl == 4) { active.Add(allBarrels[1]); active.Add(allBarrels[2]); active.Add(allBarrels[3]); active.Add(allBarrels[4]); }
        else if (lvl >= 5) { active.Add(allBarrels[0]); active.Add(allBarrels[1]); active.Add(allBarrels[2]); active.Add(allBarrels[3]); active.Add(allBarrels[4]); }

        return active;
    }
}