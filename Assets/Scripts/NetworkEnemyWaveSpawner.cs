using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkEnemyWaveSpawner : NetworkBehaviour
{
    [Header("Prefabs & Nodes")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Transform[] enemySpawnPoints = new Transform[8];

    [Header("Wave Progression Tuning")]
    [SerializeField] private int baseWaveQuota = 5;       // Enemies in Wave 1
    [SerializeField] private int quotaIncreasePerWave = 5;// How many more enemies join each new wave
    [SerializeField] private float spawnRate = 1.0f;       // Seconds between individual enemy spawns
    [SerializeField] private float delayBetweenWaves = 3.0f; // Intermission time

    // Tracking State (Server Only)
    private int currentWave = 0;
    private int totalEnemiesToSpawnThisWave;
    private int enemiesSpawnedSoFarThisWave;
    private float currentHealthMultiplier = 1.0f;

    private List<GameObject> activeEnemiesList = new List<GameObject>();

    // FIX: Declared exactly once here to prevent the duplicate compilation error
    private bool isIntermissionActive = false;

    private bool hasSpawningStarted = false;
    private float nextSpawnTime;

    public override void OnNetworkSpawn()
    {
        // Spawner remains completely idle on start until explicitly commanded by the host
    }

    /// <summary>
    /// Triggered authoritatively by the Server UI when the Host hits the "START FIREFIGHT" button.
    /// </summary>
    public void StartSpawningWaves()
    {
        if (!IsServer) return;
        if (hasSpawningStarted) return; // Prevent double-triggering

        hasSpawningStarted = true;
        Debug.Log("Firefight Match Started! Commencing Wave 1.");
        StartNextWave();
    }

    private void Update()
    {
        // If the host hasn't explicitly clicked "Start Match", do absolutely nothing!
        if (!IsServer || !hasSpawningStarted || isIntermissionActive) return;

        if (enemiesSpawnedSoFarThisWave < totalEnemiesToSpawnThisWave)
        {
            if (Time.time >= nextSpawnTime)
            {
                nextSpawnTime = Time.time + spawnRate;
                SpawnEnemyAuthoritative();
            }
        }
        else
        {
            activeEnemiesList.RemoveAll(item => item == null);

            if (activeEnemiesList.Count == 0)
            {
                StartCoroutine(IntermissionRoutine());
            }
        }
    }

    private void StartNextWave()
    {
        currentWave++;
        enemiesSpawnedSoFarThisWave = 0;
        totalEnemiesToSpawnThisWave = baseWaveQuota + ((currentWave - 1) * quotaIncreasePerWave);
        currentHealthMultiplier = 1.0f + ((currentWave - 1) * 0.15f);

        // Notify UI HUD
        GameUIManager uiManager = FindFirstObjectByType<GameUIManager>();
        if (uiManager != null)
        {
            uiManager.UpdateHUDWaveDisplay(currentWave);
        }

        Debug.Log($"--- WAVE {currentWave} STARTED! Total Enemies to Defeat: {totalEnemiesToSpawnThisWave} ---");
    }

    private void SpawnEnemyAuthoritative()
    {
        if (enemySpawnPoints.Length == 0 || enemyPrefab == null) return;

        Transform targetPoint = enemySpawnPoints[Random.Range(0, enemySpawnPoints.Length)];
        GameObject enemyInstance = Instantiate(enemyPrefab, targetPoint.position, targetPoint.rotation);

        EnemyHealth healthScript = enemyInstance.GetComponent<EnemyHealth>();
        if (healthScript != null)
        {
            int scaledHP = Mathf.RoundToInt(100 * currentHealthMultiplier);
            healthScript.currentHealth.Value = scaledHP;
        }

        enemyInstance.GetComponent<NetworkObject>().Spawn(true);
        activeEnemiesList.Add(enemyInstance);
        enemiesSpawnedSoFarThisWave++;
    }

    private IEnumerator IntermissionRoutine()
    {
        isIntermissionActive = true;
        Debug.Log($"Wave cleared! Intermission counting down...");

        RewardAllActivePlayers(1000);

        yield return new WaitForSeconds(delayBetweenWaves);

        isIntermissionActive = false;
        StartNextWave();
    }

    private void RewardAllActivePlayers(int pointsReward)
    {
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                PlayerScore playerStats = client.PlayerObject.GetComponent<PlayerScore>();
                if (playerStats != null)
                {
                    playerStats.score.Value += pointsReward;
                    Debug.Log($"Awarded {pointsReward} wave reward points to Client ID: {client.ClientId}");
                }
            }
        }
    }
}