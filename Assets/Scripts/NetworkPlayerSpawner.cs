using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerSpawner : NetworkBehaviour
{
    [Header("Spawn Layout")]
    [SerializeField] private Transform[] spawnPoints = new Transform[4];

    private int currentSpawnIndex = 0;

    public override void OnNetworkSpawn()
    {
        // Only the Server assigns spawn locations to incoming players
        if (!IsServer) return;

        // Register existing clients (e.g., the Host)
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayerForClient(client.ClientId);
        }

        // Listen for any future players connecting mid-game
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnPlayerForClient(clientId);
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points assigned to NetworkPlayerSpawner!");
            return;
        }

        // Safeguard to find the client's player network object
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient))
        {
            NetworkObject playerNetObject = networkClient.PlayerObject;

            if (playerNetObject != null)
            {
                // Select a spawn point sequentially
                Transform selectedSpawn = spawnPoints[currentSpawnIndex];
                currentSpawnIndex = (currentSpawnIndex + 1) % spawnPoints.Length;

                // Move the player to the selected spawn safely across the network
                playerNetObject.transform.position = selectedSpawn.position;
                playerNetObject.transform.rotation = selectedSpawn.rotation;
            }
        }
    }
}