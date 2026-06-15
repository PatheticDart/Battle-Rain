using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct ScoreboardEntry
{
    public int PlayerNumber;
    public int Score;
    public bool IsLocalPlayer;
}

public class PlayerScore : NetworkBehaviour
{
    public NetworkVariable<int> score = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        score.OnValueChanged += OnScoreChanged;
        UpdateGlobalScoreboard();
    }

    public override void OnNetworkDespawn()
    {
        score.OnValueChanged -= OnScoreChanged;
        if (IsServer) UpdateGlobalScoreboard();
    }

    private void OnScoreChanged(int oldVal, int newVal)
    {
        UpdateGlobalScoreboard();
    }

    public static void UpdateGlobalScoreboard()
    {
        PlayerScore[] players = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        List<ScoreboardEntry> formattedEntries = new List<ScoreboardEntry>();
        ScoreboardEntry? localPlayerEntry = null;

        for (int i = 0; i < players.Length; i++)
        {
            ScoreboardEntry entry = new ScoreboardEntry
            {
                PlayerNumber = i + 1,
                Score = players[i].score.Value,
                IsLocalPlayer = players[i].IsOwner
            };

            if (entry.IsLocalPlayer) localPlayerEntry = entry;
            else formattedEntries.Add(entry);
        }

        if (localPlayerEntry.HasValue) formattedEntries.Insert(0, localPlayerEntry.Value);

        if (GameUIManager.Instance != null && !GameUIManager.Instance.IsGameOver)
        {
            GameUIManager.Instance.RefreshScoreboard(formattedEntries);
        }
    }

    // 👈 NEW: Sorts the list purely by highest score for the Game Over screen
    public static List<ScoreboardEntry> GetGameOverScoreboard()
    {
        PlayerScore[] players = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
        var orderedByClient = new List<PlayerScore>(players);
        orderedByClient.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        List<ScoreboardEntry> entries = new List<ScoreboardEntry>();
        for (int i = 0; i < orderedByClient.Count; i++)
        {
            entries.Add(new ScoreboardEntry
            {
                PlayerNumber = i + 1,
                Score = orderedByClient[i].score.Value,
                IsLocalPlayer = orderedByClient[i].IsOwner
            });
        }

        // Sort descending (Highest score first)
        entries.Sort((a, b) => b.Score.CompareTo(a.Score));
        return entries;
    }

    public void ResetScore()
    {
        if (IsServer) score.Value = 0;
    }
}