using UnityEngine;
using Oculus.Interaction;

public class PhysicalForceHaptics : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;
    public Transform cartPivot;
    public Transform cargoObject;

    [Header("Weight Position Mapping")]
    [Tooltip("Weight x 위치가 이 값에 도달하면 최대 편향으로 간주합니다.")]
    public float maxWeightOffsetX = 0.2f;

    [Tooltip("중앙 근처에서는 좌우 편향 진동은 줄이고, 기본 양손 진동만 제공합니다.")]
    public float centerDeadZone = 0.015f;

    [Tooltip("1이면 선형에 가깝고, 낮을수록 초반부터 더 잘 느껴집니다.")]
    public float biasCurvePower = 0.9f;

    [Header("Controller Speed Mapping")]
    [Tooltip("이 속도 이하에서는 움직임 기반 추가 진동을 거의 내지 않습니다.")]
    public float controllerSpeedDeadZone = 0.03f;

    [Tooltip("이 컨트롤러 속도에서 최대 진동 비율에 도달합니다.")]
    public float controllerSpeedForMax = 1.1f;

    [Tooltip("컨트롤러 속도 외에 카트 이동 속도도 보조로 반영합니다.")]
    public float cartSpeedForMax = 0.7f;

    [Tooltip("제자리 회전에서도 피드백이 나오도록 yaw 속도를 보조로 반영합니다.")]
    public float yawSpeedForMax = 90f;

    [Range(0f, 1f)]
    public float yawContribution = 0.45f;

    [Header("Default Center Haptics")]
    [Tooltip("무게추가 중앙에 있어도 양손에 기본 진동을 줄지 여부")]
    public bool useDefaultCenterHaptics = true;

    [Tooltip("양손에 항상 들어가는 기본 진동 세기입니다.")]
    [Range(0f, 1f)]
    public float centerDefaultAmp = 0.12f;

    [Tooltip("움직임이 있을 때 기본 진동에 추가되는 양입니다.")]
    [Range(0f, 1f)]
    public float centerMotionBoost = 0.06f;

    [Header("Asymmetry Tuning")]
    [Tooltip("반대쪽 손의 진동 비율입니다. 낮을수록 치우친 쪽이 명확합니다.")]
    [Range(0f, 1f)]
    public float weakSideRatio = 0.22f;

    [Header("Haptic Limits")]
    [Tooltip("편향이 있을 때 추가되는 최소 진동 세기")]
    [Range(0f, 1f)]
    public float minForceAmp = 0.08f;

    [Tooltip("편향이 있을 때 추가되는 최대 진동 세기")]
    [Range(0f, 1f)]
    public float maxForceAmp = 0.95f;

    [Tooltip("전체 진동 배율입니다. 전체적으로 약하면 1.2~1.5 사이로 올리세요.")]
    [Range(0.5f, 3f)]
    public float globalAmpMultiplier = 1.35f;

    [Tooltip("진동 질감 고정. Quest 기준 0.2~0.35가 묵직하게 느껴집니다.")]
    public float fixedFrequency = 0.30f;

    [Tooltip("값이 높을수록 진동 변화가 빠르게 따라옵니다.")]
    [Range(0.01f, 1f)]
    public float hapticSmoothing = 0.22f;

    private Vector3 _lastPosition;
    private float _lastYaw;

    private float _currentLeftAmp;
    private float _currentRightAmp;

    // 컨트롤러 속도 계산값
    private float _currentLeftControllerSpeed;
    private float _currentRightControllerSpeed;

    // =========================================================
    // 외부 로그 스크립트에서 읽을 수 있는 현재 진동값
    // =========================================================

    public float LeftHapticAmplitude
    {
        get { return _currentLeftAmp; }
    }

    public float RightHapticAmplitude
    {
        get { return _currentRightAmp; }
    }

    public float HapticFrequency
    {
        get { return fixedFrequency; }
    }

    private void Start()
    {
        ResetMotionReference();
    }

    private void Update()
    {
        if (grabbable == null ||
            grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            ResetMotionReference();
            return;
        }

        UpdateContinuousHaptics();
    }

    private void UpdateContinuousHaptics()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f ||
            cartRoot == null ||
            cartPivot == null ||
            cargoObject == null)
        {
            return;
        }

        // 1. 카트 이동 속도
        float cartSpeed =
            (cartRoot.position - _lastPosition).magnitude / dt;

        _lastPosition = cartRoot.position;

        // 2. 카트 회전 속도
        float currentYaw = cartRoot.eulerAngles.y;

        float yawSpeed =
            Mathf.Abs(
                Mathf.DeltaAngle(
                    _lastYaw,
                    currentYaw
                )
            ) / dt;

        _lastYaw = currentYaw;

        // 3. 무게추 좌우 편향 계산
        float localX =
            cartPivot.InverseTransformPoint(
                cargoObject.position
            ).x;

        float effectiveMaxWeightOffsetX =
            maxWeightOffsetX > 0.001f
                ? maxWeightOffsetX
                : 0.2f;

        float bias = Mathf.Clamp(
            localX / effectiveMaxWeightOffsetX,
            -1f,
            1f
        );

        float absBias = Mathf.Abs(bias);

        float biasNorm = Mathf.InverseLerp(
            Mathf.Clamp01(centerDeadZone),
            1f,
            absBias
        );

        biasNorm = Mathf.Pow(
            Mathf.Clamp01(biasNorm),
            Mathf.Max(0.01f, biasCurvePower)
        );

        // 4. 움직임 강도 계산
        float controllerSpeed = GetControllerSpeed();

        float controllerNorm = Mathf.InverseLerp(
            controllerSpeedDeadZone,
            Mathf.Max(
                controllerSpeedDeadZone + 0.01f,
                controllerSpeedForMax
            ),
            controllerSpeed
        );

        float cartNorm = Mathf.InverseLerp(
            0f,
            Mathf.Max(0.01f, cartSpeedForMax),
            cartSpeed
        );

        float yawNorm = Mathf.InverseLerp(
            0f,
            Mathf.Max(0.01f, yawSpeedForMax),
            yawSpeed
        );

        float motionNorm = Mathf.Max(
            Mathf.Clamp01(controllerNorm),
            Mathf.Clamp01(cartNorm)
        );

        float effectiveYawContribution =
            yawContribution > 0f
                ? yawContribution
                : 0.45f;

        motionNorm = Mathf.Clamp01(
            Mathf.Lerp(
                motionNorm,
                Mathf.Max(
                    motionNorm,
                    Mathf.Clamp01(yawNorm)
                ),
                Mathf.Clamp01(
                    effectiveYawContribution
                )
            )
        );

        // 5. 중앙 기본 진동
        float baseAmp = 0f;

        if (useDefaultCenterHaptics)
        {
            baseAmp = centerDefaultAmp;
            baseAmp += centerMotionBoost * motionNorm;
        }

        baseAmp = Mathf.Clamp01(baseAmp);

        // 6. 편향 추가 진동
        float additionalStrongAmp = 0f;
        float additionalWeakAmp = 0f;

        if (biasNorm > 0.001f)
        {
            additionalStrongAmp = Mathf.Lerp(
                minForceAmp,
                maxForceAmp,
                biasNorm
            );

            float effectiveMotion = Mathf.Lerp(
                0.35f,
                1f,
                motionNorm
            );

            additionalStrongAmp *= effectiveMotion;

            additionalWeakAmp =
                additionalStrongAmp *
                Mathf.Clamp01(weakSideRatio);
        }

        // 7. 좌우 목표 진동 결정
        float targetLeftAmp = baseAmp;
        float targetRightAmp = baseAmp;

        if (bias < -centerDeadZone)
        {
            targetLeftAmp += additionalStrongAmp;
            targetRightAmp += additionalWeakAmp;
        }
        else if (bias > centerDeadZone)
        {
            targetLeftAmp += additionalWeakAmp;
            targetRightAmp += additionalStrongAmp;
        }
        else
        {
            targetLeftAmp = baseAmp;
            targetRightAmp = baseAmp;
        }

        // 8. 전체 진동 세기 증폭
        targetLeftAmp *= globalAmpMultiplier;
        targetRightAmp *= globalAmpMultiplier;

        targetLeftAmp = Mathf.Clamp01(targetLeftAmp);
        targetRightAmp = Mathf.Clamp01(targetRightAmp);

        // 9. 진동 변화 부드럽게 적용
        float smoothing =
            hapticSmoothing > 0f
                ? Mathf.Clamp01(hapticSmoothing)
                : 0.22f;

        _currentLeftAmp = Mathf.Lerp(
            _currentLeftAmp,
            targetLeftAmp,
            smoothing
        );

        _currentRightAmp = Mathf.Lerp(
            _currentRightAmp,
            targetRightAmp,
            smoothing
        );

        ApplyHaptics(
            _currentLeftAmp,
            _currentRightAmp
        );
    }

    private float GetControllerSpeed()
    {
        _currentLeftControllerSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.LTouch
            ).magnitude;

        _currentRightControllerSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.RTouch
            ).magnitude;

        return Mathf.Max(
            _currentLeftControllerSpeed,
            _currentRightControllerSpeed
        );
    }

    private void ApplyHaptics(float left, float right)
    {
        OVRInput.SetControllerVibration(
            fixedFrequency,
            Mathf.Clamp01(left),
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            fixedFrequency,
            Mathf.Clamp01(right),
            OVRInput.Controller.RTouch
        );
    }

    private void StopHaptics()
    {
        _currentLeftAmp = 0f;
        _currentRightAmp = 0f;

        OVRInput.SetControllerVibration(
            0f,
            0f,
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            0f,
            0f,
            OVRInput.Controller.RTouch
        );
    }

    private void ResetMotionReference()
    {
        if (cartRoot != null)
        {
            _lastPosition = cartRoot.position;
            _lastYaw = cartRoot.eulerAngles.y;
        }
    }

    private void OnDisable()
    {
        StopHaptics();
    }

    private void OnApplicationQuit()
    {
        StopHaptics();
    }
}