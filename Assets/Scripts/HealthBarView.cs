using UnityEngine;
using UnityEngine.UI;

public class HealthBarView : MonoBehaviour
{
    private HasHealth health;
    private Image fillImage;
    private Canvas localCanvas; // 👈 NEW: Cache the canvas to toggle it efficiently

    public void Initialize(HasHealth source)
    {
        health = source;
        CreateVisuals();
    }

    private void CreateVisuals()
    {
        localCanvas = gameObject.AddComponent<Canvas>(); // 👈 NEW: Store the reference
        localCanvas.renderMode = RenderMode.WorldSpace;
        localCanvas.worldCamera = Camera.main;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        RectTransform root = GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(100f, 10f);
        transform.localPosition = new Vector3(0f, GetHeightOffset(), 0f);
        transform.localScale = Vector3.one * 0.01f;

        Sprite pixel = CreatePixelSprite();
        Image background = CreateImage("Background", pixel, new Color(0f, 0f, 0f, 0.8f));
        SetRect(background.rectTransform, Vector2.zero, Vector2.one, Vector2.zero);

        fillImage = CreateImage("Fill", pixel, GetHealthColor());
        SetRect(fillImage.rectTransform, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.85f), Vector2.zero);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private void LateUpdate()
    {
        if (health == null || fillImage == null) return;

        if (Camera.main != null && localCanvas.worldCamera == null)
            localCanvas.worldCamera = Camera.main;

        fillImage.fillAmount = health.MaxHealth > 0
            ? Mathf.Clamp01((float)health.currentHealth.Value / health.MaxHealth)
            : 0f;
            
        // 👈 FIX: Disable the canvas component instead of the GameObject!
        if (localCanvas != null)
        {
            localCanvas.enabled = (health.currentHealth.Value > 0 || health is GuardianHealth);
        }
    }

    private float GetHeightOffset()
    {
        if (health is GuardianHealth) return 1.8f;
        if (health is PlayerHealth) return 1f;
        return 1f;
    }

    private Color GetHealthColor()
    {
        if (health is GuardianHealth) return new Color(1f, 0.75f, 0.1f);
        if (health is PlayerHealth) return new Color(0.2f, 0.8f, 1f);
        return new Color(1f, 0.2f, 0.2f);
    }

    private Image CreateImage(string objectName, Sprite sprite, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(transform, false);
        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offset)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offset;
        rect.offsetMax = offset;
    }

    private static Sprite CreatePixelSprite()
    {
        return Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
    }
}