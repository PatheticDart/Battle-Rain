using UnityEngine;

public class PlayerAnimationEvents : MonoBehaviour
{
    // This exact name will show up in your Animation Event dropdown!
    public void TriggerFootstep()
    {
        if (NetworkEffectManager.Instance != null)
        {
            NetworkEffectManager.Instance.PlayFootstepLocal(transform.position);
        }
    }
}