using UnityEngine;
using Oculus.Interaction;

public class PhysicalForceHaptics : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;    // 카트 전체
    public Transform cartPivot;   // 카트 중앙 바닥
    public Transform cargoObject; // 무게추

    [Header("Weight Position Mapping")]
    [Tooltip("Weight x 위치가 이 값에 도달하면 최대 편향으로 간주합니다.")]
    public float maxWeightOffsetX = 0.2f;
    [Tooltip("중앙 근처 무진동/저진동 데드존입니다.")]
    public float centerDeadZone = 0.015f;
    [Tooltip("1이면 선형에 가깝고, 낮을수록 초반부터 더 잘 느껴집니다.")]
    public float biasCurvePower = 0.9f;

    [Header("Controller Speed Mapping")]
    [Tooltip("이 속도 이하에서는 진동을 거의 내지 않습니다.")]
    public float controllerSpeedDeadZone = 0.03f;
    [Tooltip("이 컨트롤러 속도에서 최대 진동 비율에 도달합니다.")]
    public float controllerSpeedForMax = 1.1f;
    [Tooltip("컨트롤러 속도 외에 카트 이동 속도도 보조로 반영합니다.")]
    public float cartSpeedForMax = 0.7f;
    [Tooltip("제자리 회전에서도 피드백이 나오도록 yaw 속도를 보조로 반영합니다.")]
    public float yawSpeedForMax = 90f;
    [Range(0f, 1f)] public float yawContribution = 0.45f;

    [Header("Asymmetry Tuning")]
    [Tooltip("반대쪽 손의 진동 비율입니다. 낮을수록 치우친 쪽이 명확합니다.")]
    [Range(0f, 1f)] public float weakSideRatio = 0.18f;

    [Header("Haptic Limits")]
    [Range(0f, 1f)] public float minForceAmp = 0.03f;
    [Range(0f, 1f)] public float maxForceAmp = 0.85f;
    [Tooltip("진동 질감 고정. Quest 기준 0.2~0.35가 묵직하게 느껴집니다.")]
    public float fixedFrequency = 0.28f;
    [Range(0.01f, 1f)] public float hapticSmoothing = 0.18f;

    private Vector3 _lastPosition;
    private float _lastYaw;
    private float _currentLeftAmp;
    private float _currentRightAmp;

    void Start()
    {
        if (cartRoot != null)
        {
            _lastPosition = cartRoot.position;
            _lastYaw = cartRoot.eulerAngles.y;
        }
    }

    void Update()
    {
        if (grabbable == null || grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            return;
        }

        UpdateContinuousHaptics();
    }

    private void UpdateContinuousHaptics()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || cartRoot == null || cartPivot == null || cargoObject == null)
            return;

        float cartSpeed = (cartRoot.position - _lastPosition).magnitude / dt;
        _lastPosition = cartRoot.position;

        float currentYaw = cartRoot.eulerAngles.y;
        float yawSpeed = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, currentYaw)) / dt;
        _lastYaw = currentYaw;

        float localX = cartPivot.InverseTransformPoint(cargoObject.position).x;
        float effectiveMaxWeightOffsetX = maxWeightOffsetX > 0.001f ? maxWeightOffsetX : 0.2f;
        float bias = Mathf.Clamp(localX / effectiveMaxWeightOffsetX, -1f, 1f);
        float absBias = Mathf.Abs(bias);

        float biasNorm = Mathf.InverseLerp(Mathf.Clamp01(centerDeadZone), 1f, absBias);
        biasNorm = Mathf.Pow(Mathf.Clamp01(biasNorm), Mathf.Max(0.01f, biasCurvePower));

        float controllerSpeed = GetControllerSpeed();
        float controllerNorm = Mathf.InverseLerp(controllerSpeedDeadZone, Mathf.Max(controllerSpeedDeadZone + 0.01f, controllerSpeedForMax), controllerSpeed);
        float cartNorm = Mathf.InverseLerp(0f, Mathf.Max(0.01f, cartSpeedForMax), cartSpeed);
        float yawNorm = Mathf.InverseLerp(0f, Mathf.Max(0.01f, yawSpeedForMax), yawSpeed);

        float motionNorm = Mathf.Max(Mathf.Clamp01(controllerNorm), Mathf.Clamp01(cartNorm));
        float effectiveYawContribution = yawContribution > 0f ? yawContribution : 0.45f;
        motionNorm = Mathf.Clamp01(Mathf.Lerp(motionNorm, Mathf.Max(motionNorm, Mathf.Clamp01(yawNorm)), Mathf.Clamp01(effectiveYawContribution)));

        if (biasNorm <= 0.001f || motionNorm <= 0.001f)
        {
            SmoothToZero();
            return;
        }

        float strongAmp = Mathf.Lerp(minForceAmp, maxForceAmp, biasNorm) * motionNorm;
        float weakAmp = strongAmp * Mathf.Clamp01(weakSideRatio);

        float targetLeftAmp;
        float targetRightAmp;

        if (bias < 0f)
        {
            targetLeftAmp = strongAmp;
            targetRightAmp = weakAmp;
        }
        else
        {
            targetLeftAmp = weakAmp;
            targetRightAmp = strongAmp;
        }

        float smoothing = hapticSmoothing > 0f ? Mathf.Clamp01(hapticSmoothing) : 0.18f;
        _currentLeftAmp = Mathf.Lerp(_currentLeftAmp, targetLeftAmp, smoothing);
        _currentRightAmp = Mathf.Lerp(_currentRightAmp, targetRightAmp, smoothing);

        ApplyHaptics(_currentLeftAmp, _currentRightAmp);
    }

    private float GetControllerSpeed()
    {
        float leftSpeed = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch).magnitude;
        float rightSpeed = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch).magnitude;
        return Mathf.Max(leftSpeed, rightSpeed);
    }

    private void SmoothToZero()
    {
        float smoothing = hapticSmoothing > 0f ? Mathf.Clamp01(hapticSmoothing) : 0.18f;
        _currentLeftAmp = Mathf.Lerp(_currentLeftAmp, 0f, smoothing);
        _currentRightAmp = Mathf.Lerp(_currentRightAmp, 0f, smoothing);
        ApplyHaptics(_currentLeftAmp, _currentRightAmp);
    }

    private void ApplyHaptics(float l, float r)
    {
        OVRInput.SetControllerVibration(fixedFrequency, Mathf.Clamp01(l), OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(fixedFrequency, Mathf.Clamp01(r), OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        _currentLeftAmp = 0f;
        _currentRightAmp = 0f;
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }

    private void OnDisable()
    {
        StopHaptics();
    }
}

