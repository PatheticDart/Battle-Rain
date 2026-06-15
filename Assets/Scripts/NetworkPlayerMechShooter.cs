using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerMechShooter : NetworkBehaviour
{
    [Header("Base Weapon Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float baseFireRate = 0.2f;
    [SerializeField] private float bulletSpeed = 12f;
    [SerializeField] private int baseBulletDamage = 25;

    [Header("Reference Required")]
    [SerializeField] private Transform upperBody; // Drag Player Mech Upper here

    private float nextFireTime;
    private Transform[] weaponBarrels = new Transform[5];

    // Reference to our new upgrade manager
    private PlayerUpgrades playerUpgrades;

    private void Awake()
    {
        FindBarrelsInHierarchy();
        playerUpgrades = GetComponent<PlayerUpgrades>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            // Apply Fire Rate multiplier (higher multiplier = smaller delay between shots)
            float currentFireRateDelay = baseFireRate / playerUpgrades.fireRateMultiplier.Value;
            nextFireTime = Time.time + currentFireRateDelay;

            // Send the exact angle your client is looking at up to the Server RPC
            FireWeaponServerRpc(upperBody.eulerAngles.z);
        }
    }

    [ServerRpc]
    private void FireWeaponServerRpc(float clientAimAngle, ServerRpcParams serverRpcParams = default)
    {
        Quaternion fireRotation = Quaternion.Euler(0, 0, clientAimAngle);
        ulong shooterId = serverRpcParams.Receive.SenderClientId;

        // Apply Damage Multiplier
        int finalDamage = Mathf.RoundToInt(baseBulletDamage * playerUpgrades.damageMultiplier.Value);

        // 👈 NEW: Trigger the sound exactly ONCE per shot, regardless of barrel count!
        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlayGunshotClientRpc(upperBody.position, true); // true = Player Sound
        }

        // Fetch exactly which barrels should be firing based on the current weapon level
        List<Transform> activeBarrels = playerUpgrades.GetActiveBarrels(weaponBarrels);

        foreach (Transform barrel in activeBarrels)
        {
            if (barrel == null) continue;

            // Spawn bullet
            GameObject bullet = Instantiate(bulletPrefab, barrel.position, fireRotation);

            // Pass variables into our Raycast script
            ProjectileBehavior projScript = bullet.GetComponent<ProjectileBehavior>();
            if (projScript != null)
            {
                projScript.speed = bulletSpeed;
                projScript.damage = finalDamage;
                projScript.ownerClientId = shooterId;
            }

            // Replicate across network
            bullet.GetComponent<NetworkObject>().Spawn(true);

            // Auto-cleanup after 3 seconds so they don't fly forever
            Destroy(bullet, 3f);
        }
    }

    private void FindBarrelsInHierarchy()
    {
        // ⚠️ CRITICAL: Ensure your barrel child objects are named exactly "Barrel1", "Barrel2", etc.
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        for (int i = 1; i <= 5; i++)
        {
            string targetName = "Barrel" + i;
            foreach (Transform child in allChildren)
            {
                if (child.name == targetName)
                {
                    weaponBarrels[i - 1] = child;
                    break;
                }
            }
        }
    }
}