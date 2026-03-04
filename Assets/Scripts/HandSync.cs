using UnityEngine;

public class HandSync : MonoBehaviour
{
    [Header("Target to Follow")]
    [Tooltip("BothHandle 하위의 GrabPoint를 연결하세요.")]
    public Transform TargetGrabPoint;

    void LateUpdate()
    {
        if (TargetGrabPoint == null) return;

        // 물리적 계층 구조와 상관없이, 이 모델을 GrabPoint 위치로 강제 이동시킵니다.
        transform.position = TargetGrabPoint.position;
        transform.rotation = TargetGrabPoint.rotation;
    }
}