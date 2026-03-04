using UnityEngine;

public class GhostHand : MonoBehaviour
{
    [Header("Tracking Target")]
    [Tooltip("OVRCameraRig 하위의 실제 HandAnchor를 연결하세요.")]
    public Transform PhysicalHandAnchor;

    void LateUpdate()
    {
        if (PhysicalHandAnchor == null) return;

        // 지연 없이 실제 컨트롤러의 월드 좌표를 그대로 복사합니다.
        transform.position = PhysicalHandAnchor.position;
        transform.rotation = PhysicalHandAnchor.rotation;
    }
}