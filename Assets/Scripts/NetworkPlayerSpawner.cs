using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerSpawner : NetworkBehaviour
{
    [Header("Spawn Layout")]
    public Transform[] spawnPoints = new Transform[4]; // 👈 NEW: Made public!

    private int currentSpawnIndex = 0;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            SpawnPlayerForClient(client.ClientId);

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId) => SpawnPlayerForClient(clientId);

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (spawnPoints.Length == 0) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient))
        {
            NetworkObject playerNetObject = networkClient.PlayerObject;
            if (playerNetObject != null)
            {
                Transform selectedSpawn = spawnPoints[currentSpawnIndex];
                currentSpawnIndex = (currentSpawnIndex + 1) % spawnPoints.Length;

                playerNetObject.transform.position = selectedSpawn.position;
                playerNetObject.transform.rotation = selectedSpawn.rotation;
            }
        }
    }
}