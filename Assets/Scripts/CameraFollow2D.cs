using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    public static CameraFollow2D Instance { get; private set; }

    [Header("Follow Settings")]
    [SerializeField] private float smoothSpeed = 0.125f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10); // Keep Z at -10 for 2D

    private void Awake()
    {
        // Simple singleton so the player can easily find the camera
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Keeping this method so your existing player script doesn't break when it tries to call it!
    // We just forward the request straight to the UIManager now.
    public void SetTarget(Transform newTarget)
    {
        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.SetLocalPlayerTransform(newTarget);
        }
    }

    private void LateUpdate()
    {
        // Bail out if the UI manager isn't ready or doesn't have a target to look at yet
        if (GameUIManager.Instance == null || GameUIManager.Instance.CurrentCameraTarget == null) return;

        // The UIManager tracks exactly who you should be looking at (Yourself, or the spectated player)
        Transform currentTarget = GameUIManager.Instance.CurrentCameraTarget;

        // Smoothly interpolate from current position to player position
        Vector3 desiredPosition = currentTarget.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);

        transform.position = smoothedPosition;
    }
}