using UnityEngine;
using Oculus.Interaction;

public class WeightHapticController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DynamicWeightTwoGrabPlaneTransformer _transformer;

    [Header("Anti-Shake Settings")]
    [Tooltip("이 거리(m) 미만의 떨림은 진동을 발생시키지 않습니다. (추천: 0.005 ~ 0.01)")]
    public float hapticDeadZone = 0.005f;
    [Tooltip("진동 값이 변화하는 부드러움 정도 (낮을수록 부드러움)")]
    public float hapticSmoothing = 0.15f;

    [Header("Speed Haptic Settings")]
    public float speedSensitivity = 0.5f;
    public float maxSpeedHaptic = 0.4f;

    [Header("Weight/Lag Haptic Settings")]
    public float weightForceMultiplier = 2.0f;
    public float maxWeightHaptic = 0.7f;

    private float _smoothedWeightIntensity; // 부드러운 진동 전환을 위한 변수

    void Update()
    {
        if (_transformer == null) return;

        float lagDist = _transformer.GetCurrentLagDistance();

        // 1. 손을 뗐을 때 즉시 종료
        if (lagDist <= 0f)
        {
            _smoothedWeightIntensity = 0;
            StopHaptics();
            return;
        }

        // 2. [손떨림 방지] 데드존 적용
        // 데드존보다 작으면 0으로 만들고, 크면 데드존만큼 뺀 값부터 시작하게 하여 급격한 진동 방지
        float filteredLagDist = Mathf.Max(0, lagDist - hapticDeadZone);

        // 3. 속도 기반 공통 진동
        Vector3 lVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        float lSpeedHaptic = Mathf.Clamp(lVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);
        float rSpeedHaptic = Mathf.Clamp(rVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);

        // 4. 무게 기반 저항 진동 계산
        float targetWeightIntensity = (40.5f - _transformer.LeftPosFollow) * filteredLagDist * weightForceMultiplier;
        targetWeightIntensity = Mathf.Clamp(targetWeightIntensity, 0, maxWeightHaptic);

        // 5. [부드러운 전환] 급격한 진동 변화 방지
        _smoothedWeightIntensity = Mathf.Lerp(_smoothedWeightIntensity, targetWeightIntensity, hapticSmoothing);

        // 6. 비대칭 할당 및 송출
        if (_transformer.CurrentTargetSide == DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Left)
        {
            TriggerHaptics(lSpeedHaptic + _smoothedWeightIntensity, rSpeedHaptic);
        }
        else
        {
            TriggerHaptics(lSpeedHaptic, rSpeedHaptic + _smoothedWeightIntensity);
        }
    }

    private void TriggerHaptics(float leftAmp, float rightAmp)
    {
        OVRInput.SetControllerVibration(0.1f, leftAmp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0.1f, rightAmp, OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}