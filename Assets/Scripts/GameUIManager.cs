using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameUIManager : NetworkBehaviour
{
    [Header("Main/Lobby Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject clientPanel;
    [SerializeField] private GameObject ingamePanel;

    [Header("Main Menu Inputs")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text generatedCodeText;

    [Header("In-Game HUD")]
    [SerializeField] private TMP_Text waveNumberText;
    [SerializeField] private TMP_Text healthStatusText;
    [SerializeField] private TMP_Text[] playerScoreTexts = new TMP_Text[4];

    [Header("Death & Spectator UI")]
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private TMP_Text spectateTargetText;
    [SerializeField] private GameObject spectateLeftBtn;
    [SerializeField] private GameObject spectateRightBtn;

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text[] gameOverScoreTexts = new TMP_Text[4];
    [SerializeField] private GameObject hostGameOverButtons;
    [SerializeField] private GameObject clientGameOverButtons;

    [Header("Scoreboard Styling")]
    [SerializeField] private Color localPlayerColor = Color.yellow;
    [SerializeField] private Color otherPlayerColor = Color.white;
    [SerializeField] private float localPlayerFontSize = 36f;
    [SerializeField] private float otherPlayerFontSize = 24f;

    [Header("Relay Core Settings")]
    [SerializeField] private int maxConnections = 4;
    private const string WebGLConnectionType = "wss";
    private string currentJoinCode = "";

    [Header("Upgrade UI")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private TMP_Text[] upgradeButtonTexts = new TMP_Text[3];
    private UpgradeType[] currentUpgrades = new UpgradeType[3];

    [HideInInspector] public Transform CurrentCameraTarget;
    [HideInInspector] public bool IsGameOver = false;
    [HideInInspector] public NetworkVariable<bool> matchStarted = new NetworkVariable<bool>(false);

    private List<PlayerHealth> spectatablePlayers = new List<PlayerHealth>();
    private int currentSpectateIndex = 0;
    private bool runRewardsGranted;

    public static GameUIManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (GetComponent<OffscreenTeammateIndicators>() == null)
            gameObject.AddComponent<OffscreenTeammateIndicators>();
    }

    private async void Start()
    {
        HideAllScoreTexts();
        ShowPanel(mainPanel);
        await InitializeUnityServices();
    }

    private async System.Threading.Tasks.Task InitializeUnityServices()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            SetStatus("Unity Services ready.");
        }
        catch (Exception e) { SetStatus("Services failed."); Debug.LogError(e); }
    }

    #region Lobby & Connecting
    public async void StartHost()
    {
        try
        {
            SetStatus("Creating Relay...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            if (generatedCodeText != null) generatedCodeText.text = $"JOIN CODE: {currentJoinCode}";

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = true;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, WebGLConnectionType));

            if (NetworkManager.Singleton.StartHost()) ShowPanel(hostPanel);
        }
        catch (Exception e) { SetStatus("Failed to host."); Debug.LogError(e); }
    }

    public async void StartClient()
    {
        try
        {
            if (string.IsNullOrEmpty(joinCodeInput.text)) return;
            SetStatus("Connecting...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCodeInput.text.Trim());

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = true;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, WebGLConnectionType));

            if (NetworkManager.Singleton.StartClient()) ShowPanel(clientPanel);
        }
        catch (Exception e) { SetStatus("Client failed."); Debug.LogError(e); }
    }

    public void ActionStartMatch()
    {
        if (!IsServer) return;

        matchStarted.Value = true;

        NetworkEnemyWaveSpawner spawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (spawner != null)
        {
            spawner.StartSpawningWaves();
            AlertClientsMatchStartedClientRpc();
        }
    }

    public void EndRunServer()
    {
        if (!IsServer || IsGameOver) return;

        IsGameOver = true;
        NetworkEnemyWaveSpawner spawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        int waveReached = spawner != null ? spawner.CurrentWave : 1;
        if (!runRewardsGranted)
        {
            runRewardsGranted = true;
            PlayerScore.AwardRunRewardsToPlayers(waveReached);
        }
        TriggerGameOverClientRpc();
    }

    [ClientRpc]
    private void AlertClientsMatchStartedClientRpc()
    {
        ShowPanel(ingamePanel);
    }
    #endregion

    #region Quitting & Utilities
    public void QuitApplication()
    {
        Application.Quit();
    }

    public void CopyJoinCodeToClipboard()
    {
        TextEditor te = new TextEditor();
        te.text = currentJoinCode;
        te.SelectAll();
        te.Copy();
        Debug.Log("Copied to clipboard: " + currentJoinCode);
    }

    public void DisconnectAndReturnToMenu()
    {
        NetworkManager.Singleton.Shutdown();
        Destroy(NetworkManager.Singleton.gameObject);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void RestartMatch()
    {
        if (!IsServer) return;

        IsGameOver = false;
        runRewardsGranted = false;

        NetworkEnemyWaveSpawner spawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (spawner != null) spawner.ResetSpawner();

        ProjectileBehavior[] allProjectiles = FindObjectsByType<ProjectileBehavior>(FindObjectsSortMode.None);
        foreach (var proj in allProjectiles)
        {
            if (proj.NetworkObject != null && proj.NetworkObject.IsSpawned)
            {
                proj.NetworkObject.Despawn(true);
            }
        }

        PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        NetworkPlayerSpawner pSpawner = FindFirstObjectByType<NetworkPlayerSpawner>();
        Transform[] spawns = pSpawner != null ? pSpawner.spawnPoints : new Transform[0];

        for (int i = 0; i < allPlayers.Length; i++)
        {
            Vector3 spawnPos = (spawns.Length > 0) ? spawns[i % spawns.Length].position : Vector3.zero;

            allPlayers[i].Revive(spawnPos);

            if (allPlayers[i].TryGetComponent(out PlayerScore ps))
            {
                ps.ResetScore();
            }

            if (allPlayers[i].TryGetComponent(out PlayerUpgrades pu))
            {
                pu.ResetUpgrades();
            }
        }

        RestartMatchClientRpc();
    }

    [ClientRpc]
    private void RestartMatchClientRpc()
    {
        IsGameOver = false;
        ShowPanel(ingamePanel);

        if (deathPanel) deathPanel.SetActive(false);

        PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (var p in allPlayers)
        {
            if (p.IsOwner) SetLocalPlayerTransform(p.transform);
        }
    }
    #endregion

    #region Spectator Mode
    public void SetLocalPlayerTransform(Transform playerTransform)
    {
        CurrentCameraTarget = playerTransform;
    }

    public void ShowDeathPanelAndSpectate()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (hostPanel != null) hostPanel.SetActive(false);
        if (clientPanel != null) clientPanel.SetActive(false);

        if (ingamePanel != null) ingamePanel.SetActive(false);

        deathPanel.SetActive(true);
        RefreshSpectatorList();
    }

    public void SpectateNext() => CycleSpectator(1);
    public void SpectatePrevious() => CycleSpectator(-1);

    private void CycleSpectator(int direction)
    {
        RefreshSpectatorList();
        if (spectatablePlayers.Count == 0) return;

        currentSpectateIndex = (currentSpectateIndex + direction + spectatablePlayers.Count) % spectatablePlayers.Count;
        UpdateSpectatorFocus();
    }

    private void RefreshSpectatorList()
    {
        spectatablePlayers.Clear();
        foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (!p.isDead.Value) spectatablePlayers.Add(p);
        }

        bool showArrows = spectatablePlayers.Count > 1;
        if (spectateLeftBtn != null) spectateLeftBtn.SetActive(showArrows);
        if (spectateRightBtn != null) spectateRightBtn.SetActive(showArrows);

        if (spectatablePlayers.Count > 0)
        {
            currentSpectateIndex = Mathf.Clamp(currentSpectateIndex, 0, spectatablePlayers.Count - 1);
            UpdateSpectatorFocus();
        }
        else
        {
            spectateTargetText.text = "ALL PLAYERS DEAD";
        }
    }

    private void UpdateSpectatorFocus()
    {
        if (spectatablePlayers.Count == 0) return;

        PlayerHealth targetPlayer = spectatablePlayers[currentSpectateIndex];
        CurrentCameraTarget = targetPlayer.transform;

        var allPlayers = new List<PlayerHealth>(FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None));
        allPlayers.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        int pNum = allPlayers.IndexOf(targetPlayer) + 1;

        spectateTargetText.text = $"SPECTATING: PLAYER {pNum}";
    }
    #endregion

    #region Game Over UI
    [ClientRpc]
    public void TriggerGameOverClientRpc()
    {
        IsGameOver = true;
        ShowPanel(gameOverPanel);
        deathPanel.SetActive(false);

        if (hostGameOverButtons) hostGameOverButtons.SetActive(IsServer);
        if (clientGameOverButtons) clientGameOverButtons.SetActive(!IsServer);

        List<ScoreboardEntry> finalScores = PlayerScore.GetGameOverScoreboard();
        for (int i = 0; i < gameOverScoreTexts.Length; i++)
        {
            if (i < finalScores.Count)
            {
                gameOverScoreTexts[i].gameObject.SetActive(true);
                gameOverScoreTexts[i].text = $"PLAYER {finalScores[i].PlayerNumber}: {finalScores[i].Score}";

                if (finalScores[i].IsLocalPlayer)
                {
                    gameOverScoreTexts[i].color = localPlayerColor;
                    gameOverScoreTexts[i].fontSize = localPlayerFontSize;
                }
                else
                {
                    gameOverScoreTexts[i].color = otherPlayerColor;
                    gameOverScoreTexts[i].fontSize = otherPlayerFontSize;
                }
            }
            else
            {
                gameOverScoreTexts[i].gameObject.SetActive(false);
            }
        }
    }
    #endregion

    #region Core Displays
    private void ShowPanel(GameObject targetPanel)
    {
        mainPanel.SetActive(targetPanel == mainPanel);
        hostPanel.SetActive(targetPanel == hostPanel);
        clientPanel.SetActive(targetPanel == clientPanel);
        ingamePanel.SetActive(targetPanel == ingamePanel);
        if (gameOverPanel) gameOverPanel.SetActive(targetPanel == gameOverPanel);
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    public void UpdateHUDWaveDisplay(int currentWave)
    {
        if (IsServer) UpdateHUDWaveDisplayClientRpc(currentWave);
        else if (waveNumberText != null) waveNumberText.text = $"WAVE: {currentWave}";
    }

    [ClientRpc]
    private void UpdateHUDWaveDisplayClientRpc(int currentWave)
    {
        if (waveNumberText != null) waveNumberText.text = $"WAVE: {currentWave}";
    }
    public void UpdateHUDHealthDisplay(int currentHP, int maxHP) => healthStatusText.text = $"HP: {currentHP} / {maxHP}";

    private void HideAllScoreTexts()
    {
        foreach (var txt in playerScoreTexts) if (txt != null) txt.gameObject.SetActive(false);
    }

    public void RefreshScoreboard(List<ScoreboardEntry> activePlayerScores)
    {
        if (IsGameOver) return;

        for (int i = 0; i < playerScoreTexts.Length; i++)
        {
            if (playerScoreTexts[i] == null) continue;

            if (i < activePlayerScores.Count)
            {
                playerScoreTexts[i].gameObject.SetActive(true);
                ScoreboardEntry entry = activePlayerScores[i];
                playerScoreTexts[i].text = $"PLAYER {entry.PlayerNumber}: {entry.Score}";

                if (entry.IsLocalPlayer)
                {
                    playerScoreTexts[i].color = localPlayerColor;
                    playerScoreTexts[i].fontSize = localPlayerFontSize;
                }
                else
                {
                    playerScoreTexts[i].color = otherPlayerColor;
                    playerScoreTexts[i].fontSize = otherPlayerFontSize;
                }
            }
            else
            {
                playerScoreTexts[i].gameObject.SetActive(false);
            }
        }
    }
    #endregion

    #region Upgrades
    [ClientRpc]
    public void TriggerUpgradePanelClientRpc()
    {
        if (IsGameOver) return;

        PlayerHealth localHealth = null;
        PlayerUpgrades localUpgrades = null;

        foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (p.IsOwner)
            {
                localHealth = p;
                localUpgrades = p.GetComponent<PlayerUpgrades>();
                break;
            }
        }

        if (localHealth != null && !localHealth.isDead.Value && localUpgrades != null)
        {
            GenerateAndShowUpgrades(localUpgrades);
        }
    }

    private void GenerateAndShowUpgrades(PlayerUpgrades localUpgrades)
    {
        // 1. Create a base upgrade pool excluding the Revive option
        List<UpgradeType> basePool = new List<UpgradeType> {
            UpgradeType.Damage, UpgradeType.FireRate, UpgradeType.HealthMax, UpgradeType.HealthRepair
        };

        if (localUpgrades.weaponLevel.Value < 5) basePool.Add(UpgradeType.AdditionalWeapon);

        // 2. Shuffle the base list thoroughly
        for (int i = 0; i < basePool.Count; i++)
        {
            UpgradeType temp = basePool[i];
            int randomIndex = UnityEngine.Random.Range(i, basePool.Count);
            basePool[i] = basePool[randomIndex];
            basePool[randomIndex] = temp;
        }

        // 3. Look for dead teammates
        PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        bool hasDeadTeammates = false;
        foreach (var p in allPlayers)
        {
            if (p.isDead.Value && p.OwnerClientId != localUpgrades.OwnerClientId)
            {
                hasDeadTeammates = true;
                break;
            }
        }

        List<UpgradeType> finalThree = new List<UpgradeType>();

        // 4. Implement Guaranteed Selection
        if (allPlayers.Length >= 2 && hasDeadTeammates)
        {
            // Pick 2 random items from standard options and manually insert the Revive card
            finalThree.Add(basePool[0]);
            finalThree.Add(basePool[1]);
            finalThree.Add(UpgradeType.ReviveTeammate);
        }
        else
        {
            // No dead teammates: pick standard 3 entries
            finalThree.Add(basePool[0]);
            finalThree.Add(basePool[1]);
            finalThree.Add(basePool[2]);
        }

        // 5. Shuffle the final three cards together so Revive isn't always sitting on the exact same button slot
        for (int i = 0; i < finalThree.Count; i++)
        {
            UpgradeType temp = finalThree[i];
            int randomIndex = UnityEngine.Random.Range(i, finalThree.Count);
            finalThree[i] = finalThree[randomIndex];
            finalThree[randomIndex] = temp;
        }

        // 6. Map options to button arrays
        for (int i = 0; i < 3; i++)
        {
            currentUpgrades[i] = finalThree[i];
            upgradeButtonTexts[i].text = GetUpgradeName(finalThree[i]);
        }

        upgradePanel.SetActive(true);
    }

    private string GetUpgradeName(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.AdditionalWeapon: return "ADDITIONAL WEAPONS (+1 BARREL)";
            case UpgradeType.Damage: return "DAMAGE UP (x1.2)";
            case UpgradeType.FireRate: return "FIRE RATE UP (x1.02)";
            case UpgradeType.HealthMax: return "MAX HEALTH UP (x1.1)";
            case UpgradeType.HealthRepair: return "REPAIR (HEAL 25%)";
            case UpgradeType.ReviveTeammate: return "REVIVE A TEAMMATE";
            default: return "UNKNOWN UPGRADE";
        }
    }

    public void SelectUpgradeButton(int buttonIndex)
    {
        upgradePanel.SetActive(false);

        foreach (var upgrades in FindObjectsByType<PlayerUpgrades>(FindObjectsSortMode.None))
        {
            if (upgrades.IsOwner)
            {
                upgrades.ApplyUpgradeServerRpc(currentUpgrades[buttonIndex], NetworkManager.Singleton.LocalClientId);
                break;
            }
        }
    }
    #endregion

    public void ResumeFromSpectate()
    {
        ShowPanel(ingamePanel);
        if (deathPanel != null) deathPanel.SetActive(false);
    }
}
