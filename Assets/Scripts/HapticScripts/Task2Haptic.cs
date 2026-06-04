using UnityEngine;
using Oculus.Interaction;

public class Task2Haptic : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;
    public Transform cartPivot;
    public Transform cargoObject;

    [Header("Anti-Shake")]
    public float speedDeadZone = 0.03f;
    public float yawDeadZone = 2.0f;
    public float biasDeadZone = 0.03f;

    [Header("Bias Mapping")]
    [Tooltip("cargo local x가 이 값에 가까워지면 최대 편향으로 간주")]
    public float maxOffsetX = 0.18f;

    [Tooltip("1=선형, 2=후반부 강함, 0.5=초반부터 강함")]
    public float biasCurvePower = 1.5f;

    [Header("Motion Mapping")]
    public float speedGateFull = 0.6f;
    public float yawGateFull = 90f;
    public float motionCurvePower = 1.6f;

    [Tooltip("회전이 핵심이면 yawWeight를 크게")]
    [Range(0f, 1f)] public float yawWeight = 0.7f;

    [Header("Haptic Intensity")]
    [Range(0f, 1f)] public float minStrongAmp = 0.08f;
    [Range(0f, 1f)] public float maxStrongAmp = 0.85f;

    [Tooltip("반대 손은 거의 안 느껴지게 0.03~0.1 권장")]
    [Range(0f, 0.2f)] public float weakSideMaxAmp = 0.08f;

    [Header("Output")]
    [Range(0f, 1f)] public float fixedFrequency = 0.18f;
    [Range(0.01f, 1f)] public float hapticSmoothing = 0.15f;

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

        UpdateHaptics();
    }

    private void UpdateHaptics()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || cartRoot == null || cartPivot == null || cargoObject == null)
            return;

        Vector3 velocity = (cartRoot.position - _lastPosition) / dt;
        float speed = velocity.magnitude;
        _lastPosition = cartRoot.position;

        float currentYaw = cartRoot.eulerAngles.y;
        float yawSpeed = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, currentYaw)) / dt;
        _lastYaw = currentYaw;

        // -1 = 왼쪽 최대, 0 = 중앙, 1 = 오른쪽 최대
        float localX = cartPivot.InverseTransformPoint(cargoObject.position).x;
        float bias = Mathf.Clamp(localX / Mathf.Max(maxOffsetX, 0.0001f), -1f, 1f);
        float absBias = Mathf.Abs(bias);

        if (absBias < biasDeadZone)
        {
            SmoothToZero();
            return;
        }

        // 0~1 연속 편향 강도
        float biasNorm = Mathf.InverseLerp(biasDeadZone, 1f, absBias);
        biasNorm = Mathf.Pow(Mathf.Clamp01(biasNorm), biasCurvePower);

        // 이동/회전 속도 0~1
        float speedNorm = Mathf.InverseLerp(speedDeadZone, speedGateFull, speed);
        speedNorm = Mathf.Pow(Mathf.Clamp01(speedNorm), motionCurvePower);

        float yawNorm = Mathf.InverseLerp(yawDeadZone, yawGateFull, yawSpeed);
        yawNorm = Mathf.Pow(Mathf.Clamp01(yawNorm), motionCurvePower);

        float motionNorm = Mathf.Clamp01(speedNorm * (1f - yawWeight) + yawNorm * yawWeight);

        if (motionNorm <= 0.001f)
        {
            SmoothToZero();
            return;
        }

        // 핵심: 편향이 커질수록 0.08 → 0.85로 연속 증가
        float strongAmp = Mathf.Lerp(minStrongAmp, maxStrongAmp, biasNorm) * motionNorm;
        float weakAmp = weakSideMaxAmp * motionNorm * 0.5f;

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

        _currentLeftAmp = Mathf.Lerp(_currentLeftAmp, targetLeftAmp, hapticSmoothing);
        _currentRightAmp = Mathf.Lerp(_currentRightAmp, targetRightAmp, hapticSmoothing);

        ApplyHaptics(_currentLeftAmp, _currentRightAmp);
    }

    private void SmoothToZero()
    {
        _currentLeftAmp = Mathf.Lerp(_currentLeftAmp, 0f, hapticSmoothing);
        _currentRightAmp = Mathf.Lerp(_currentRightAmp, 0f, hapticSmoothing);
        ApplyHaptics(_currentLeftAmp, _currentRightAmp);
    }

    private void ApplyHaptics(float leftAmp, float rightAmp)
    {
        OVRInput.SetControllerVibration(fixedFrequency, leftAmp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(fixedFrequency, rightAmp, OVRInput.Controller.RTouch);
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