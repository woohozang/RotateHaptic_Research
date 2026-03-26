using UnityEngine;
using Oculus.Interaction; // 👈 Grab 상태 확인을 위해 필수 추가

public class MoveRotateTP : MonoBehaviour
{
    [Header("References")]
    public Transform centerEyeAnchor; // OVRCameraRig의 CenterEyeAnchor
    public Grabbable grabbable;      // 👈 쇼핑카트 핸들의 Grabbable 컴포넌트 할당

    [Header("Settings")]
    public float speed = 1f; // 초당 이동 거리

    void Update()
    {
        // 1. 레퍼런스 체크
        if (centerEyeAnchor == null || grabbable == null) return;

        // 2. 🔥 핵심 조건: 양손(GrabPoints가 2개)으로 잡고 있을 때만 실행
        if (grabbable.GrabPoints.Count >= 2)
        {
            MoveForward();
        }
    }

    void MoveForward()
    {
        // Y축 제거 (수평 이동만)
        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;
        forward.Normalize();

        // 이동 적용
        transform.position += forward * speed * Time.deltaTime;
    }
}