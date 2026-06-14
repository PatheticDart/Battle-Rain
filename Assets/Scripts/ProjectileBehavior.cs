using Unity.Netcode;
using UnityEngine;

public class ProjectileBehavior : NetworkBehaviour
{
    [HideInInspector] public ulong ownerClientId;
    [HideInInspector] public float speed = 12f;
    [HideInInspector] public int damage = 10;

    private void Update()
    {
        // Calculate how far the bullet will move this exact frame
        float stepDistance = speed * Time.deltaTime;

        // CRUCIAL: Only the server handles hit detection to prevent network desync
        if (IsServer)
        {
            // Shoot an invisible ray forward to see if we will hit anything this frame
            RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.up, stepDistance);

            if (hit.collider != null)
            {
                // Identify if this bullet belongs to an enemy or a player
                // (Enemy bullets use our placeholder ID 999999 assigned in NetworkEnemyShooter)
                bool isEnemyBullet = (gameObject.CompareTag("EnemyBullet") || ownerClientId == 999999);

                // Hit an Enemy
                if (hit.collider.CompareTag("Enemy"))
                {
                    if (isEnemyBullet)
                    {
                        // Friendly Fire Protection: Let the enemy bullet pass through other enemies
                    }
                    else
                    {
                        EnemyHealth enemyHealth = hit.collider.GetComponent<EnemyHealth>();
                        if (enemyHealth != null)
                        {
                            // Pass the damage and the owner's ClientId to the enemy for tracking points
                            enemyHealth.TakeDamage(damage, ownerClientId);
                        }
                        Destroy(gameObject);
                        return;
                    }
                }
                // Hit a Player
                else if (hit.collider.CompareTag("Player"))
                {
                    if (!isEnemyBullet)
                    {
                        // Friendly Fire Protection: Let player bullets pass through other players
                    }
                    else
                    {
                        // TODO: Add your player health hook here when ready
                        // hit.collider.GetComponent<PlayerHealth>().TakeDamage(damage);
                        Destroy(gameObject);
                        return;
                    }
                }
                // Hit an Obstacle
                else if (hit.collider.CompareTag("Obstacle") || hit.collider.gameObject.layer == LayerMask.NameToLayer("Obstacles"))
                {
                    Destroy(gameObject);
                    return; // Stop moving immediately
                }
            }
        }

        // Move the bullet locally on EVERY player's screen for perfectly smooth visuals
        transform.position += transform.up * stepDistance;
    }
}