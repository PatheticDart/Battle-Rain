using Unity.Netcode;
using UnityEngine;

public class NetworkEffectManager : NetworkBehaviour
{
    public static NetworkEffectManager Instance { get; private set; }

    [Header("Visual Prefabs")]
    [SerializeField] private GameObject explosionPrefab;
    [SerializeField] private GameObject sparkPrefab;

    [Header("Audio Settings - VFX")]
    [SerializeField] private AudioClip explosionSound;
    [SerializeField][Range(0f, 1f)] private float explosionVolume = 0.8f;

    [Header("Audio Settings - Weapons")]
    [SerializeField] private AudioClip playerGunshotSound;
    [SerializeField][Range(0f, 1f)] private float playerGunshotVolume = 0.5f; 
    
    [SerializeField] private AudioClip playerLaserShotSound; // 👈 NEW
    [SerializeField][Range(0f, 1f)] private float playerLaserVolume = 0.5f; // 👈 NEW
    
    [SerializeField] private AudioClip enemyGunshotSound;
    [SerializeField][Range(0f, 1f)] private float enemyGunshotVolume = 0.5f; 
    
    [SerializeField] private AudioClip defenseTurretShotSound; // 👈 NEW
    [SerializeField][Range(0f, 1f)] private float defenseTurretVolume = 0.5f; // 👈 NEW

    [Header("Audio Settings - Movement")]
    [SerializeField] private AudioClip[] footstepSounds; 
    [SerializeField][Range(0f, 1f)] private float footstepVolume = 0.3f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [ClientRpc]
    public void PlayExplosionClientRpc(Vector3 position)
    {
        if (explosionPrefab != null)
        {
            GameObject fx = Instantiate(explosionPrefab, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        if (explosionSound != null)
        {
            AudioSource.PlayClipAtPoint(explosionSound, position, explosionVolume);
        }
    }

    [ClientRpc]
    public void PlaySparkClientRpc(Vector3 position)
    {
        PlaySparkLocal(position);
    }

    public void PlaySparkLocal(Vector3 position)
    {
        if (sparkPrefab != null)
        {
            GameObject fx = Instantiate(sparkPrefab, position, Quaternion.identity);
            Destroy(fx, 0.5f);
        }
    }

    [ClientRpc]
    public void PlayGunshotClientRpc(Vector3 position, bool isPlayer)
    {
        AudioClip clipToPlay = isPlayer ? playerGunshotSound : enemyGunshotSound;
        float volumeToPlay = isPlayer ? playerGunshotVolume : enemyGunshotVolume;

        if (clipToPlay != null)
        {
            AudioSource.PlayClipAtPoint(clipToPlay, position, volumeToPlay);
        }
    }

    // 👈 NEW: Dedicated RPC for the Laser
    [ClientRpc]
    public void PlayLaserClientRpc(Vector3 position)
    {
        if (playerLaserShotSound != null)
        {
            AudioSource.PlayClipAtPoint(playerLaserShotSound, position, playerLaserVolume);
        }
    }

    // 👈 NEW: Dedicated RPC for the Turret
    [ClientRpc]
    public void PlayTurretClientRpc(Vector3 position)
    {
        if (defenseTurretShotSound != null)
        {
            AudioSource.PlayClipAtPoint(defenseTurretShotSound, position, defenseTurretVolume);
        }
    }

    public void PlayFootstepLocal(Vector3 position)
    {
        if (footstepSounds != null && footstepSounds.Length > 0)
        {
            AudioClip clip = footstepSounds[Random.Range(0, footstepSounds.Length)];
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, position, footstepVolume);
            }
        }
    }
}