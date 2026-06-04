using UnityEngine;
using Oculus.Interaction;

public class SingleVibrationWeightHapticController : MonoBehaviour
{
    public enum WeightSide { Left, Right }
    public enum BottleCount { Two = 2, Four = 4, Six = 6 }

    [Header("References")]
    [SerializeField] private Grabbable _grabbable;

    [Header("Experiment Condition")]
    public WeightSide weightSide = WeightSide.Left;
    public BottleCount bottleCount = BottleCount.Two;

    [Header("Anti-Shake Settings")]
    public float speedDeadZone = 0.06f;
    [Range(0.01f, 1f)] public float hapticSmoothing = 0.18f;

    [Header("Speed Response Settings")]
    [Tooltip("이 속도부터 진동이 시작됩니다.")]
    public float speedGateStart = 0.06f;

    [Tooltip("이 속도 이상부터 무게 진동이 100% 반영됩니다.")]
    public float speedGateFull = 0.45f;

    [Tooltip("속도 변화 체감을 강화합니다. 1보다 크면 빠른 움직임에서 진동 차이가 커집니다.")]
    public float speedCurvePower = 1.8f;

    [Header("Speed Haptic Settings")]
    public float speedSensitivity = 0.9f;
    [Range(0f, 1f)] public float maxSpeedHaptic = 0.35f;

    [Header("Bottle Weight Haptic Settings")]
    [Range(0f, 1f)] public float bottle2Haptic = 0.12f;
    [Range(0f, 1f)] public float bottle4Haptic = 0.32f;
    [Range(0f, 1f)] public float bottle6Haptic = 0.55f;

    [Header("Asymmetric Haptic Settings")]
    [Range(0f, 2f)] public float strongSideMultiplier = 1.2f;
    [Range(0f, 1f)] public float weakSideMultiplier = 0.05f;

    [Header("Output Limits")]
    [Range(0f, 1f)] public float maxTotalHaptic = 0.9f;
    [Range(0f, 1f)] public float hapticFrequency = 0.18f;

    [Header("Weak Side")]
    [Range(0f, 1f)]
    public float weakSideConstantAmp = 0.08f;

    private float _smoothedLeftAmp;
    private float _smoothedRightAmp;

    void Update()
    {
        if (_grabbable == null || _grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            return;
        }

        float lSpeed = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch).magnitude;
        float rSpeed = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch).magnitude;

        float activeSpeed = Mathf.Max(lSpeed, rSpeed);

        if (activeSpeed < speedDeadZone)
        {
            SmoothToZero();
            return;
        }

        // 속도 게이트: 천천히 움직이면 약하게, 빠르게 움직이면 강하게
        float speedGate = Mathf.InverseLerp(speedGateStart, speedGateFull, activeSpeed);
        speedGate = Mathf.Pow(Mathf.Clamp01(speedGate), speedCurvePower);

        // 각 손 속도도 같은 방식으로 곡선화
        float lSpeedNorm = Mathf.InverseLerp(speedGateStart, speedGateFull, lSpeed);
        float rSpeedNorm = Mathf.InverseLerp(speedGateStart, speedGateFull, rSpeed);

        lSpeedNorm = Mathf.Pow(Mathf.Clamp01(lSpeedNorm), speedCurvePower);
        rSpeedNorm = Mathf.Pow(Mathf.Clamp01(rSpeedNorm), speedCurvePower);

        float lSpeedHaptic = Mathf.Clamp(lSpeedNorm * speedSensitivity, 0f, maxSpeedHaptic);
        float rSpeedHaptic = Mathf.Clamp(rSpeedNorm * speedSensitivity, 0f, maxSpeedHaptic);

        // 적재물 기본 진동도 속도 게이트를 통과해야 발생
        float weightHaptic = GetBottleWeightHaptic() * speedGate;

        float targetLeftAmp;
        float targetRightAmp;

        if (weightSide == WeightSide.Left)
        {
            targetLeftAmp =
                (weightHaptic * strongSideMultiplier)
                + lSpeedHaptic;

            targetRightAmp =
                weakSideConstantAmp;
        }
        else
        {
            targetLeftAmp =
                weakSideConstantAmp;

            targetRightAmp =
                (weightHaptic * strongSideMultiplier)
                + rSpeedHaptic;
        }

        targetLeftAmp = Mathf.Clamp(targetLeftAmp, 0f, maxTotalHaptic);
        targetRightAmp = Mathf.Clamp(targetRightAmp, 0f, maxTotalHaptic);

        _smoothedLeftAmp = Mathf.Lerp(_smoothedLeftAmp, targetLeftAmp, hapticSmoothing);
        _smoothedRightAmp = Mathf.Lerp(_smoothedRightAmp, targetRightAmp, hapticSmoothing);

        TriggerHaptics(_smoothedLeftAmp, _smoothedRightAmp);
    }

    private float GetBottleWeightHaptic()
    {
        switch (bottleCount)
        {
            case BottleCount.Two: return bottle2Haptic;
            case BottleCount.Four: return bottle4Haptic;
            case BottleCount.Six: return bottle6Haptic;
            default: return bottle2Haptic;
        }
    }

    private void SmoothToZero()
    {
        _smoothedLeftAmp = Mathf.Lerp(_smoothedLeftAmp, 0f, hapticSmoothing);
        _smoothedRightAmp = Mathf.Lerp(_smoothedRightAmp, 0f, hapticSmoothing);

        TriggerHaptics(_smoothedLeftAmp, _smoothedRightAmp);
    }

    private void TriggerHaptics(float leftAmp, float rightAmp)
    {
        OVRInput.SetControllerVibration(hapticFrequency, leftAmp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(hapticFrequency, rightAmp, OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        _smoothedLeftAmp = 0f;
        _smoothedRightAmp = 0f;

        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }

    private void OnDisable()
    {
        StopHaptics();
    }
}