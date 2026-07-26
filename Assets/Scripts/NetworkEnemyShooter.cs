using Unity.Netcode;
using UnityEngine;

public class NetworkEnemyShooter : NetworkBehaviour
{
    [Header("Weapon Settings")]
    [SerializeField] private GameObject enemyBulletPrefab;
    [SerializeField] private Transform barrel;
    [SerializeField] private float bulletSpeed = 8f;
    [SerializeField] private int bulletDamage = 10;
    [SerializeField] private float bulletLifetime = 3f; // 👈 Configurable bullet lifetime (in seconds)

    [Header("Ranges")]
    [SerializeField] private float firingRange = 10f; // Will only shoot if player is within this distance

    [Header("Firing Mode")]
    [SerializeField] private bool isAutomatic = false;

    [Header("Burst Settings (If Auto is False)")]
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float burstInterval = 2.0f;
    [SerializeField] private float shotInterval = 0.2f;

    [Header("Auto Settings (If Auto is True)")]
    [SerializeField] private float autoFireRate = 0.5f;

    private NetworkEnemyController controller;

    private float nextActionTime;
    private bool isBursting;
    private int currentBurstShotsFired;

    private void Awake()
    {
        controller = GetComponent<NetworkEnemyController>();
    }

    private void Update()
    {
        if (!IsServer || controller.currentTarget == null) return;

        // If the target player is dead, reset burst states and don't shoot!
        if (controller.currentTarget.TryGetComponent<PlayerHealth>(out var targetHealth) && targetHealth.isDead.Value)
        {
            isBursting = false;
            return;
        }

        // Check distance to player before executing any shooting logic
        float distanceToPlayer = Vector2.Distance(transform.position, controller.currentTarget.position);
        if (distanceToPlayer > firingRange)
        {
            isBursting = false;
            return;
        }

        if (isAutomatic)
        {
            ProcessAutomaticFire();
        }
        else
        {
            ProcessBurstFire();
        }
    }

    private void ProcessAutomaticFire()
    {
        if (Time.time >= nextActionTime)
        {
            FireBullet();
            nextActionTime = Time.time + autoFireRate;
        }
    }

    private void ProcessBurstFire()
    {
        if (isBursting)
        {
            if (Time.time >= nextActionTime)
            {
                FireBullet();
                currentBurstShotsFired++;
                nextActionTime = Time.time + shotInterval;

                if (currentBurstShotsFired >= shotsPerBurst)
                {
                    isBursting = false;
                    nextActionTime = Time.time + burstInterval;
                }
            }
        }
        else
        {
            if (Time.time >= nextActionTime)
            {
                isBursting = true;
                currentBurstShotsFired = 0;
            }
        }
    }

    private void FireBullet()
    {
        if (barrel == null) return;

        GameObject bullet = Instantiate(enemyBulletPrefab, barrel.position, barrel.rotation);

        ProjectileBehavior projScript = bullet.GetComponent<ProjectileBehavior>();
        if (projScript != null)
        {
            projScript.Configure(bulletSpeed, bulletDamage, 999999);
        }

        bullet.GetComponent<NetworkObject>().Spawn(true);
        Destroy(bullet, bulletLifetime); // 👈 Uses the configurable lifetime setting

        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlayGunshotClientRpc(barrel.position, false);
        }
    }
}