using Unity.Netcode;
using UnityEngine;

public abstract class HasHealth : NetworkBehaviour
{
    [SerializeField] protected int maxHealth = 100;

    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int MaxHealth => maxHealth;
    public bool IsDestroyed => currentHealth.Value <= 0;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            currentHealth.Value = maxHealth;

        if (IsClient)
        {
            GameObject healthBarObject = new GameObject("HealthBar", typeof(RectTransform));
            healthBarObject.transform.SetParent(transform, false);
            healthBarObject.AddComponent<HealthBarView>().Initialize(this);
        }
    }

    public virtual void TakeDamage(int damage)
    {
        if (!IsServer || IsDestroyed || damage <= 0) return;
        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
        if (IsDestroyed) OnHealthDepleted();
    }

    public virtual void Repair(int amount)
    {
        if (!IsServer || amount <= 0 || currentHealth.Value >= maxHealth) return;
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
        OnHealthRepaired();
    }

    public void SetMaxHealth(int newMaxHealth, bool fillHealth = true)
    {
        bool serverContext = IsServer || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
        if (!serverContext) return;
        maxHealth = Mathf.Max(1, newMaxHealth);
        if (fillHealth) currentHealth.Value = maxHealth;
        else currentHealth.Value = Mathf.Min(currentHealth.Value, maxHealth);
    }

    protected virtual void OnHealthDepleted() { }
    protected virtual void OnHealthRepaired() { }
}
