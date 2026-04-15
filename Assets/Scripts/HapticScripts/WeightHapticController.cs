using UnityEngine;
using Oculus.Interaction;

public class WeightHapticController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DynamicWeightTwoGrabPlaneTransformer _transformer;

    [Header("Speed Haptic Settings")]
    public float speedSensitivity = 0.5f;
    public float maxSpeedHaptic = 0.4f;

    [Header("Weight/Lag Haptic Settings")]
    [Tooltip("LeftPosFollow가 낮을수록(무거울수록) 진동 증폭")]
    public float weightForceMultiplier = 2.0f;
    public float maxWeightHaptic = 0.7f;

    void Update()
    {
        if (_transformer == null) return;

        // 1. 양손의 속도 값 가져오기
        Vector3 lVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

        // 2. 속도 기반 공통 진동 (Friction)
        float lSpeedHaptic = Mathf.Clamp(lVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);
        float rSpeedHaptic = Mathf.Clamp(rVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);

        // 3. 무게/지연 기반 저항 진동 (Resistance)
        // GetCurrentLagDistance()는 무거운 쪽 손의 지연 거리를 반환함
        float lagDist = _transformer.GetCurrentLagDistance();

        // LeftPosFollow 값이 낮을수록(무거울수록) 더 강한 저항 진동 발생
        // 예: 40f(가벼움) -> 1배, 0.5f(무거움) -> 약 80배 가중
        float weightIntensity = (40.5f - _transformer.LeftPosFollow) * lagDist * weightForceMultiplier;
        weightIntensity = Mathf.Clamp(weightIntensity, 0, maxWeightHaptic);

        // 4. 비대칭 할당 (CurrentTargetSide에 따른 가중)
        if (_transformer.CurrentTargetSide == DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Left)
        {
            lSpeedHaptic += weightIntensity; // 왼쪽 손에 저항감 추가
        }
        else
        {
            rSpeedHaptic += weightIntensity; // 오른쪽 손에 저항감 추가
        }

        // 5. 실제 컨트롤러에 진동 송출
        TriggerHaptics(lSpeedHaptic, rSpeedHaptic);
    }

    private void TriggerHaptics(float leftAmp, float rightAmp)
    {
        // 양손 컨트롤러에 독립적인 진동 전달
        OVRInput.SetControllerVibration(0.1f, leftAmp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0.1f, rightAmp, OVRInput.Controller.RTouch);
    }
}