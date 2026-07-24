using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class OffscreenTeammateIndicators : MonoBehaviour
{
    private readonly Dictionary<ulong, RectTransform> indicators = new Dictionary<ulong, RectTransform>();
    private RectTransform canvasRect;
    private Camera mainCamera;

    private void Awake()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null) canvasRect = canvas.GetComponent<RectTransform>();
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (canvasRect == null || mainCamera == null) return;

        HashSet<ulong> visiblePlayers = new HashSet<ulong>();
        foreach (PlayerHealth player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (player == null || player.IsDestroyed || player.IsOwner) continue;
            visiblePlayers.Add(player.OwnerClientId);

            Vector3 viewportPosition = mainCamera.WorldToViewportPoint(player.transform.position);
            bool offscreen = viewportPosition.z < 0f || viewportPosition.x < 0f || viewportPosition.x > 1f ||
                             viewportPosition.y < 0f || viewportPosition.y > 1f;

            if (!offscreen)
            {
                SetIndicatorVisible(player.OwnerClientId, false);
                continue;
            }

            RectTransform indicator = GetOrCreateIndicator(player.OwnerClientId);
            indicator.gameObject.SetActive(true);
            PositionIndicator(indicator, viewportPosition);
        }

        foreach (KeyValuePair<ulong, RectTransform> pair in indicators)
            if (!visiblePlayers.Contains(pair.Key)) pair.Value.gameObject.SetActive(false);
    }

    private RectTransform GetOrCreateIndicator(ulong clientId)
    {
        if (indicators.TryGetValue(clientId, out RectTransform existing)) return existing;

        GameObject indicatorObject = new GameObject($"TeammateIndicator_{clientId}", typeof(RectTransform));
        indicatorObject.transform.SetParent(canvasRect, false);
        RectTransform indicator = indicatorObject.GetComponent<RectTransform>();
        indicator.sizeDelta = new Vector2(64f, 64f);

        TextMeshProUGUI arrow = indicatorObject.AddComponent<TextMeshProUGUI>();
        arrow.text = "▲";
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.fontSize = 42f;
        arrow.color = new Color(0.2f, 0.85f, 1f, 0.95f);
        arrow.raycastTarget = false;

        indicators.Add(clientId, indicator);
        return indicator;
    }

    private void PositionIndicator(RectTransform indicator, Vector3 viewportPosition)
    {
        Vector2 direction = new Vector2(viewportPosition.x - 0.5f, viewportPosition.y - 0.5f);
        if (viewportPosition.z < 0f) direction = -direction;
        if (direction.sqrMagnitude < 0.001f) direction = Vector2.up;
        direction.Normalize();

        Rect rect = canvasRect.rect;
        float margin = 55f;
        float halfWidth = rect.width * 0.5f - margin;
        float halfHeight = rect.height * 0.5f - margin;
        float scale = Mathf.Min(halfWidth / Mathf.Abs(direction.x == 0f ? 0.0001f : direction.x),
                                halfHeight / Mathf.Abs(direction.y == 0f ? 0.0001f : direction.y));

        indicator.anchoredPosition = direction * scale;
        indicator.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
    }

    private void SetIndicatorVisible(ulong clientId, bool visible)
    {
        if (indicators.TryGetValue(clientId, out RectTransform indicator))
            indicator.gameObject.SetActive(visible);
    }
}
