using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerMechShooter : NetworkBehaviour
{
    [Header("Weapon Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float fireRate = 0.2f;
    [SerializeField] private float bulletSpeed = 12f;
    [SerializeField] private int bulletDamage = 25; // 👈 New: Adjustable Player Damage

    [Header("Upgrade System")]
    public NetworkVariable<int> weaponLevel = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Reference Required")]
    [SerializeField] private Transform upperBody; // Drag Player Mech Upper here

    private float nextFireTime;
    private Transform[] weaponBarrels = new Transform[5];

    private void Awake()
    {
        FindBarrelsInHierarchy();
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;

            // Send the exact angle your client is looking at up to the Server RPC
            FireWeaponServerRpc(upperBody.eulerAngles.z);
        }
    }

    [ServerRpc]
    private void FireWeaponServerRpc(float clientAimAngle, ServerRpcParams serverRpcParams = default)
    {
        int barrelsToFire = Mathf.Clamp(weaponLevel.Value, 1, 5);
        Quaternion fireRotation = Quaternion.Euler(0, 0, clientAimAngle);

        // Get the ID of the specific client who clicked "Fire"
        ulong shooterId = serverRpcParams.Receive.SenderClientId;

        for (int i = 0; i < barrelsToFire; i++)
        {
            Transform barrel = weaponBarrels[i];
            if (barrel == null) continue;

            // Spawn bullet
            GameObject bullet = Instantiate(bulletPrefab, barrel.position, fireRotation);

            // Pass variables into our Raycast script
            ProjectileBehavior projScript = bullet.GetComponent<ProjectileBehavior>();
            if (projScript != null)
            {
                projScript.speed = bulletSpeed;
                projScript.damage = bulletDamage;   // 👈 New: Hands damage off to the bullet
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