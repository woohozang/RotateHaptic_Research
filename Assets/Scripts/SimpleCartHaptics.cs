using UnityEngine;

public class CartHapticController : MonoBehaviour
{
    [Header("Cart State")]
    public float currentAngularVelocity; // 현재 회전 속도 (Z축)
    public float currentSteeringAngle;   // 현재 꺾인 각도 (중앙 기준)

    [Header("Settings")]
    public float maxVelocity = 5.0f;
    public float maxAngle = 45.0f;

    void Update()
    {
        // 1. 주도하는 손 판별 (예: 오른쪽으로 돌리면 오른쪽 손이 주도)
        OVRInput.Controller leadingHand = (currentAngularVelocity > 0) ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        OVRInput.Controller opposingHand = (currentAngularVelocity > 0) ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

        // 2. 주도하는 손: 운동감 (속도 기반 고주파)
        // 빈 카트이므로 진폭은 최대 0.4 정도로 가볍게 설정
        float leadingFreq = 0.8f;
        float leadingAmp = Mathf.Clamp(Mathf.Abs(currentAngularVelocity) / maxVelocity, 0.05f, 0.4f);

        // 3. 반대 손: 저항감 (각도 기반 저주파)
        // 꺾인 각도가 클수록 묵직하게 버티는 느낌 제공 (건 진동 착시 대역)
        float anchorFreq = 0.25f;
        float anchorAmp = Mathf.Clamp(Mathf.Abs(currentSteeringAngle) / maxAngle, 0.1f, 0.6f);

        // 4. 메타퀘스트 컨트롤러에 신호 전송
        OVRInput.SetControllerVibration(leadingFreq, leadingAmp, leadingHand);
        OVRInput.SetControllerVibration(anchorFreq, anchorAmp, opposingHand);
    }
}