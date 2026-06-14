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
using UnityEngine.UI;

public class GameUIManager : NetworkBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject clientPanel;
    [SerializeField] private GameObject ingamePanel;

    [Header("Main Menu Inputs/Outputs")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text statusText;

    [Header("Host Menu Display")]
    [SerializeField] private TMP_Text generatedCodeText;

    [Header("In-Game HUD Displays")]
    [SerializeField] private TMP_Text waveNumberText;
    [SerializeField] private TMP_Text healthStatusText;

    [Header("Player Scores")]
    // Array to hold your 4 player score text objects
    [SerializeField] private TMP_Text[] playerScoreTexts = new TMP_Text[4];

    [Header("Scoreboard Styling")]
    [SerializeField] private Color localPlayerColor = Color.yellow;
    [SerializeField] private Color otherPlayerColor = Color.white;
    [SerializeField] private float localPlayerFontSize = 36f;
    [SerializeField] private float otherPlayerFontSize = 24f;

    [Header("Relay Core Settings")]
    [SerializeField] private int maxConnections = 4;
    private const string WebGLConnectionType = "wss";

    // Singleton instance for easy access from other scripts
    public static GameUIManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            SetStatus("Unity Services ready.");
        }
        catch (Exception exception)
        {
            SetStatus("Unity Services failed to initialize.");
            Debug.LogError(exception);
        }
    }

    public async void StartHost()
    {
        try
        {
            SetStatus("Creating Relay Allocation...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            if (generatedCodeText != null)
            {
                generatedCodeText.text = $"JOIN CODE: {joinCode}";
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = true;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, WebGLConnectionType));

            if (NetworkManager.Singleton.StartHost())
            {
                SetStatus("Host started successfully.");
                ShowPanel(hostPanel);
            }
            else
            {
                SetStatus("Failed to start Host.");
            }
        }
        catch (Exception exception)
        {
            SetStatus("Failed to host match.");
            Debug.LogError(exception);
        }
    }

    public async void StartClient()
    {
        try
        {
            string joinCode = joinCodeInput.text.Trim();
            if (string.IsNullOrEmpty(joinCode))
            {
                SetStatus("Please enter a valid join code.");
                return;
            }

            SetStatus("Connecting to Relay...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = true;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, WebGLConnectionType));

            if (NetworkManager.Singleton.StartClient())
            {
                SetStatus("Client started.");
                ShowPanel(ingamePanel);
            }
            else
            {
                SetStatus("Failed to start Client.");
            }
        }
        catch (Exception exception)
        {
            SetStatus("Client failed. Check join code and Console.");
            Debug.LogError(exception);
        }
    }

    /// <summary>
    /// Assigned directly to the Host UI Button "START FIREFIGHT" via Inspector OnClick event.
    /// </summary>
    public void ActionStartMatch()
    {
        if (!IsServer) return;

        NetworkEnemyWaveSpawner spawner = FindFirstObjectByType<NetworkEnemyWaveSpawner>();
        if (spawner != null)
        {
            spawner.StartSpawningWaves();
            ShowPanel(ingamePanel); // Shift host UI from layout staging to gameplay layer
        }
        else
        {
            Debug.LogError("NetworkEnemyWaveSpawner could not be found in this scene!");
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            PlayerScore.UpdateGlobalScoreboard();
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            PlayerScore.UpdateGlobalScoreboard();
        }
    }

    private void ShowPanel(GameObject targetPanel)
    {
        mainPanel.SetActive(targetPanel == mainPanel);
        hostPanel.SetActive(targetPanel == hostPanel);
        clientPanel.SetActive(targetPanel == clientPanel);
        ingamePanel.SetActive(targetPanel == ingamePanel);
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    public void UpdateHUDWaveDisplay(int currentWave) => waveNumberText.text = $"WAVE: {currentWave}";
    public void UpdateHUDHealthDisplay(int currentHP, int maxHP) => healthStatusText.text = $"HP: {currentHP} / {maxHP}";

    private void HideAllScoreTexts()
    {
        foreach (var txt in playerScoreTexts)
        {
            if (txt != null) txt.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Now processes our structured list of entries to display the scoreboard
    /// </summary>
    public void RefreshScoreboard(List<ScoreboardEntry> activePlayerScores)
    {
        for (int i = 0; i < playerScoreTexts.Length; i++)
        {
            if (playerScoreTexts[i] == null) continue;

            if (i < activePlayerScores.Count)
            {
                playerScoreTexts[i].gameObject.SetActive(true);

                ScoreboardEntry entry = activePlayerScores[i];

                // Set the text formatting using the player's actual designated number (e.g., PLAYER 2)
                playerScoreTexts[i].text = $"PLAYER {entry.PlayerNumber}: {entry.Score}";

                // Highlight the text ONLY if the data container flags this as the local machine's player
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
}