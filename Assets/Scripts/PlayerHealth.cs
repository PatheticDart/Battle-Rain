using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;

    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // 👈 CRITICAL NETCODE FIX: If this is a client-owned player object living on the server,
            // change its Rigidbody to Kinematic. This prevents the server physics engine from 
            // fighting incoming network transform updates, keeping the collider perfectly synced!
            if (!IsOwner && TryGetComponent(out Rigidbody2D rb))
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.simulated = true;
            }

            if (GameUIManager.Instance != null && GameUIManager.Instance.matchStarted.Value)
            {
                currentHealth.Value = 0;
                isDead.Value = true;
            }
            else
            {
                currentHealth.Value = maxHealth;
                isDead.Value = false;
            }
        }

        currentHealth.OnValueChanged += OnHealthChanged;
        isDead.OnValueChanged += OnDeathStateChanged;

        if (IsOwner && GameUIManager.Instance != null)
        {
            GameUIManager.Instance.UpdateHUDHealthDisplay(currentHealth.Value, maxHealth);
            GameUIManager.Instance.SetLocalPlayerTransform(transform);

            if (isDead.Value) GameUIManager.Instance.ShowDeathPanelAndSpectate();
        }

        if (isDead.Value)
        {
            DisablePlayerComponents();
        }
        else
        {
            EnablePlayerComponents();
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isDead.OnValueChanged -= OnDeathStateChanged;
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        if (IsOwner && GameUIManager.Instance != null)
        {
            GameUIManager.Instance.UpdateHUDHealthDisplay(newValue, maxHealth);
        }
    }

    private void OnDeathStateChanged(bool previousState, bool isNowDead)
    {
        if (isNowDead)
        {
            DisablePlayerComponents();
            if (IsOwner && GameUIManager.Instance != null && !GameUIManager.Instance.IsGameOver)
            {
                GameUIManager.Instance.ShowDeathPanelAndSpectate();
            }
        }
        else
        {
            EnablePlayerComponents();
            if (IsOwner && GameUIManager.Instance != null)
            {
                GameUIManager.Instance.ResumeFromSpectate();
                GameUIManager.Instance.SetLocalPlayerTransform(transform);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int damage) => TakeDamage(damage);

    public void TakeDamage(int damage)
    {
        if (!IsServer || isDead.Value) return;
        currentHealth.Value -= damage;
        if (currentHealth.Value <= 0) Die();
    }

    private void Die()
    {
        if (!IsServer) return;
        isDead.Value = true;

        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlayExplosionClientRpc(transform.position);
        }

        CheckForGameOver();
    }

    public void Revive(Vector3 spawnPosition)
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;
        isDead.Value = false;
        TeleportClientRpc(spawnPosition);
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 spawnPosition)
    {
        transform.position = spawnPosition;
        if (TryGetComponent(out Rigidbody2D rb)) rb.position = spawnPosition;
    }

    private void DisablePlayerComponents()
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        if (TryGetComponent(out NetworkMechController mover)) mover.enabled = false;
        if (TryGetComponent(out NetworkPlayerMechShooter shooter)) shooter.enabled = false;
    }

    private void EnablePlayerComponents()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        foreach (var c in GetComponentsInChildren<Collider2D>(true)) c.enabled = true;
        if (TryGetComponent(out NetworkMechController mover)) mover.enabled = true;
        if (TryGetComponent(out NetworkPlayerMechShooter shooter)) shooter.enabled = true;
    }

    private void CheckForGameOver()
    {
        if (!IsServer) return;
        bool allDead = true;
        foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (!player.isDead.Value) { allDead = false; break; }
        }
        if (allDead) GameUIManager.Instance.TriggerGameOverClientRpc();
    }

    public void ApplyHealthUpgrade(float multiplier)
    {
        if (!IsServer || isDead.Value) return;
        maxHealth = Mathf.RoundToInt(maxHealth * multiplier);
        currentHealth.Value = Mathf.RoundToInt(currentHealth.Value * multiplier);
    }

    public void RepairHealth(float percentage)
    {
        if (!IsServer || isDead.Value) return;
        int healAmount = Mathf.RoundToInt(maxHealth * percentage);
        currentHealth.Value = Mathf.Clamp(currentHealth.Value + healAmount, 0, maxHealth);
    }
}