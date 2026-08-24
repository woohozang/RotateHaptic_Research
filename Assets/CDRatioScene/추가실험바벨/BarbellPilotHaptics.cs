using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Barbell Pilot용 비대칭 진동 제어.
///
/// - 진동 진폭은 ConditionController에서 직접 전달
/// - 두 손으로 잡았을 때만 진동
/// - 양손의 실제 수직 이동 속도에 따라 전체 진동 강도를 소폭 보정
/// - 좌우 모두 동일한 Speed Gain을 적용하므로
///   좌우 진동 차이는 유지됨
/// </summary>
public class BarbellPilotHaptics : MonoBehaviour
{
    [Header("Grab Reference")]
    [SerializeField]
    private Grabbable _grabbable;


    [Header("Haptic Frequency")]
    [Tooltip("OVRInput 진동 주파수")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _frequency = 0.5f;


    [Header("Vertical Speed Compensation")]

    [Tooltip("이 수직 속도 이하에서는 최소 Speed Gain 사용")]
    [SerializeField]
    private float _speedStart = 0.03f;

    [Tooltip("이 수직 속도 이상에서는 최대 Speed Gain 사용")]
    [SerializeField]
    private float _speedFull = 0.45f;

    [Tooltip("매우 느리게 움직일 때 적용되는 진동 배율")]
    [Range(0.5f, 1.5f)]
    [SerializeField]
    private float _minSpeedGain = 0.90f;

    [Tooltip("빠르게 움직일 때 적용되는 진동 배율")]
    [Range(0.5f, 1.5f)]
    [SerializeField]
    private float _maxSpeedGain = 1.15f;

    [Tooltip("속도값 안정화")]
    [SerializeField]
    private float _speedSmoothing = 10f;


    [Header("Haptic Smoothing")]

    [Tooltip("진동 진폭 변화의 부드러움")]
    [SerializeField]
    private float _hapticSmoothing = 20f;


    [Header("Debug")]
    [SerializeField]
    private bool _showDebugLog = false;


    // ============================================
    // 현재 Condition에서 설정된 기준 진폭
    // ============================================

    private float _leftBaseAmplitude = 0.20f;
    private float _rightBaseAmplitude = 0.20f;

    private bool _feedbackEnabled = false;


    // 실제 출력되는 현재 진폭
    private float _currentLeftAmplitude = 0f;
    private float _currentRightAmplitude = 0f;


    // 속도 계산
    private Vector3 _previousHandMidpoint;
    private float _smoothedVerticalSpeed = 0f;

    private bool _wasTwoHandGrab = false;


    // ============================================
    // 외부 접근용
    // ============================================

    public float CurrentLeftBaseAmplitude
        => _leftBaseAmplitude;

    public float CurrentRightBaseAmplitude
        => _rightBaseAmplitude;

    public float CurrentVerticalSpeed
        => _smoothedVerticalSpeed;


    // ============================================
    // Condition Controller에서 호출
    // ============================================

    public void SetFeedback(
        bool enabled,
        float leftAmplitude,
        float rightAmplitude)
    {
        _feedbackEnabled = enabled;

        _leftBaseAmplitude =
            Mathf.Clamp01(leftAmplitude);

        _rightBaseAmplitude =
            Mathf.Clamp01(rightAmplitude);


        if (_showDebugLog)
        {
            Debug.Log(
                $"[Barbell Haptics] " +
                $"Enabled={_feedbackEnabled}, " +
                $"Base Amp L={_leftBaseAmplitude:F2}, " +
                $"R={_rightBaseAmplitude:F2}"
            );
        }


        // 진동이 OFF로 바뀌었으면 즉시 목표값은 0
        if (!_feedbackEnabled)
        {
            _smoothedVerticalSpeed = 0f;
        }
    }


    private void Update()
    {
        // ============================================
        // 1. Two-Hand Grab 확인
        // ============================================

        bool twoHandGrab =
            _grabbable != null &&
            _grabbable.GrabPoints != null &&
            _grabbable.GrabPoints.Count >= 2;


        if (!twoHandGrab)
        {
            _wasTwoHandGrab = false;
            _smoothedVerticalSpeed = 0f;

            SmoothHapticsTo(
                0f,
                0f
            );

            ApplyVibration();

            return;
        }


        // ============================================
        // 2. 실제 양손 중앙 위치
        // ============================================

        Pose hand0 =
            _grabbable.GrabPoints[0];

        Pose hand1 =
            _grabbable.GrabPoints[1];


        Vector3 currentMidpoint =
            (hand0.position +
             hand1.position) * 0.5f;


        // 처음 두 손을 잡은 프레임에서는
        // 속도 Spike 방지
        if (!_wasTwoHandGrab)
        {
            _previousHandMidpoint =
                currentMidpoint;

            _wasTwoHandGrab = true;

            _smoothedVerticalSpeed = 0f;
        }


        // ============================================
        // 3. 수직 이동 속도 계산
        //
        // 들어올리기 / 내리기 모두 Abs 처리
        // ============================================

        float rawVerticalSpeed =
            Mathf.Abs(
                currentMidpoint.y -
                _previousHandMidpoint.y
            )
            /
            Mathf.Max(
                Time.deltaTime,
                0.0001f
            );


        _previousHandMidpoint =
            currentMidpoint;


        // ============================================
        // 4. Speed Smoothing
        // ============================================

        float speedLerp =
            1f -
            Mathf.Exp(
                -_speedSmoothing *
                Time.deltaTime
            );


        _smoothedVerticalSpeed =
            Mathf.Lerp(
                _smoothedVerticalSpeed,
                rawVerticalSpeed,
                speedLerp
            );


        // ============================================
        // 5. Speed Gain
        // ============================================

        float normalizedSpeed =
            Mathf.InverseLerp(
                _speedStart,
                _speedFull,
                _smoothedVerticalSpeed
            );


        float speedGain =
            Mathf.Lerp(
                _minSpeedGain,
                _maxSpeedGain,
                normalizedSpeed
            );


        // ============================================
        // 6. 목표 진동값
        // ============================================

        float targetLeft = 0f;
        float targetRight = 0f;


        if (_feedbackEnabled)
        {
            targetLeft =
                Mathf.Clamp01(
                    _leftBaseAmplitude *
                    speedGain
                );


            targetRight =
                Mathf.Clamp01(
                    _rightBaseAmplitude *
                    speedGain
                );
        }


        // ============================================
        // 7. 진동 Smoothing
        // ============================================

        SmoothHapticsTo(
            targetLeft,
            targetRight
        );


        // ============================================
        // 8. 실제 Controller 출력
        // ============================================

        ApplyVibration();
    }


    private void SmoothHapticsTo(
        float targetLeft,
        float targetRight)
    {
        float hapticLerp =
            1f -
            Mathf.Exp(
                -_hapticSmoothing *
                Time.deltaTime
            );


        _currentLeftAmplitude =
            Mathf.Lerp(
                _currentLeftAmplitude,
                targetLeft,
                hapticLerp
            );


        _currentRightAmplitude =
            Mathf.Lerp(
                _currentRightAmplitude,
                targetRight,
                hapticLerp
            );
    }


    private void ApplyVibration()
    {
        OVRInput.SetControllerVibration(
            _frequency,
            _currentLeftAmplitude,
            OVRInput.Controller.LTouch
        );


        OVRInput.SetControllerVibration(
            _frequency,
            _currentRightAmplitude,
            OVRInput.Controller.RTouch
        );
    }


    public void StopImmediately()
    {
        _currentLeftAmplitude = 0f;
        _currentRightAmplitude = 0f;

        _smoothedVerticalSpeed = 0f;


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


    private void OnDisable()
    {
        StopImmediately();
    }


    private void OnDestroy()
    {
        StopImmediately();
    }
}