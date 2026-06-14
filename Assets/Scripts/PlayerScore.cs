using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// This container packages all the info the UI needs to display a player correctly
public struct ScoreboardEntry
{
    public int PlayerNumber;
    public int Score;
    public bool IsLocalPlayer;
}

public class PlayerScore : NetworkBehaviour
{
    // NetworkVariable syncs the score across the entire network safely from the server
    public NetworkVariable<int> score = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        // When any client spawns or a score changes, update the UI scoreboard
        score.OnValueChanged += OnScoreChanged;
        UpdateGlobalScoreboard();
    }

    public override void OnNetworkDespawn()
    {
        score.OnValueChanged -= OnScoreChanged;

        // Recalculate scoreboard if a player leaves mid-match
        if (IsServer)
        {
            UpdateGlobalScoreboard();
        }
    }

    private void OnScoreChanged(int oldVal, int newVal)
    {
        UpdateGlobalScoreboard();
    }

    /// <summary>
    /// Gathers all active player scores in the match, pushes the local player to the top,
    /// and sends the formatted list to the GameUIManager.
    /// </summary>
    public static void UpdateGlobalScoreboard()
    {
        // Find all player score scripts currently active in the match
        PlayerScore[] players = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);

        // Sort them strictly by Client ID so "Player 1", "Player 2", etc., are always assigned accurately
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        List<ScoreboardEntry> formattedEntries = new List<ScoreboardEntry>();
        ScoreboardEntry? localPlayerEntry = null;

        for (int i = 0; i < players.Length; i++)
        {
            ScoreboardEntry entry = new ScoreboardEntry
            {
                PlayerNumber = i + 1, // Determines if they are Player 1, Player 2, etc.
                Score = players[i].score.Value,
                IsLocalPlayer = players[i].IsOwner
            };

            // If this is our local player looking at the screen, hold onto their data separately
            if (entry.IsLocalPlayer)
            {
                localPlayerEntry = entry;
            }
            else
            {
                formattedEntries.Add(entry); // Add everyone else to the standard list
            }
        }

        // If a local player exists on this machine, shove them into the absolute top slot (Index 0)
        if (localPlayerEntry.HasValue)
        {
            formattedEntries.Insert(0, localPlayerEntry.Value);
        }

        // Push the perfectly ordered list to the UI
        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.RefreshScoreboard(formattedEntries);
        }
    }
}