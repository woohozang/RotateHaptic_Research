using UnityEngine;

/// <summary>
/// OneGrab Translate 레버의 Local Z 위치를 읽어
/// 원격 Cube를 위아래로 이동시킵니다.
///
/// 레버 Local Z Offset = 0
/// → Press01 = 0
/// → Cube가 TopPoint 위치
///
/// 레버 Local Z Offset = -0.1
/// → Press01 = 1
/// → Cube가 BottomPoint 위치
///
/// C/D Ratio가 적용된 레버 Transform을 읽으므로
/// C/D가 낮을수록 Cube가 상대적으로 느리게 하강합니다.
/// </summary>
[DefaultExecutionOrder(11000)]
public class RemoteCargoLeverController : MonoBehaviour
{
    [Header("레버 입력")]

    [Tooltip("OneGrabTranslateCDTransformer가 적용된 레버 Transform")]
    [SerializeField]
    private Transform lever;

    [Tooltip("초기 위치 기준으로 레버가 최대로 당겨지는 Local Z 값")]
    [SerializeField]
    private float minZOffset = -0.1f;

    [Tooltip("초기 위치 기준 중립 상태의 Local Z 값")]
    [SerializeField]
    private float maxZOffset = 0f;

    [Tooltip("이 범위 이내의 작은 Z 움직임은 무시합니다.")]
    [Min(0f)]
    [SerializeField]
    private float zDeadzone = 0.001f;

    [Header("원격 Cube")]

    [Tooltip("위아래로 움직일 Cube")]
    [SerializeField]
    private Transform cargo;

    [Tooltip("Press01이 0일 때 Cube 위치")]
    [SerializeField]
    private Transform topPoint;

    [Tooltip("Press01이 1일 때 Cube 위치")]
    [SerializeField]
    private Transform bottomPoint;

    [Header("Press 매핑")]

    [Tooltip("레버 위치와 Cube 위치를 즉시 일치시킵니다.")]
    [SerializeField]
    private bool directMapping = true;

    [Tooltip("Direct Mapping을 끈 경우 Cube 이동 속도")]
    [Min(0f)]
    [SerializeField]
    private float followSpeed = 1f;

    [Tooltip("레버 입력에 따른 Cube 이동 곡선")]
    [SerializeField]
    private AnimationCurve pressCurve =
        AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("초기화")]

    [Tooltip("시작할 때 현재 레버 위치에 맞춰 Cube 위치를 즉시 설정합니다.")]
    [SerializeField]
    private bool initializeCargoOnStart = true;

    [Header("디버그")]

    [SerializeField]
    private bool debugLog;

    [Min(0.05f)]
    [SerializeField]
    private float debugInterval = 0.2f;

    private float _neutralLeverLocalZ;
    private float _nextDebugTime;

    /// <summary>
    /// 초기 위치 기준 현재 레버의 Local Z 이동량입니다.
    /// 예: 0 ~ -0.1
    /// </summary>
    public float CurrentZOffset { get; private set; }

    /// <summary>
    /// 0 = 누르지 않음
    /// 1 = 최대로 누름
    /// </summary>
    public float Press01 { get; private set; }

    private void Reset()
    {
        lever = transform;
    }

    private void Start()
    {
        if (lever == null)
        {
            lever = transform;
        }

        _neutralLeverLocalZ = lever.localPosition.z;

        if (!ValidateReferences())
        {
            return;
        }

        CalculatePress();

        if (initializeCargoOnStart)
        {
            ApplyCargoPosition(true);
        }
    }

    /*
     * Grab Transformer와 C/D Transformer가 위치를 적용한 다음
     * 최종 레버 위치를 읽기 위해 LateUpdate에서 실행합니다.
     */
    private void LateUpdate()
    {
        if (!ValidateReferences())
        {
            return;
        }

        CalculatePress();
        ApplyCargoPosition(directMapping);
        PrintDebugLog();
    }

    /// <summary>
    /// 레버의 Local Z Offset을 0~1 Press 값으로 변환합니다.
    /// </summary>
    private void CalculatePress()
    {
        CurrentZOffset =
            lever.localPosition.z -
            _neutralLeverLocalZ;

        float minOffset =
            Mathf.Min(minZOffset, maxZOffset);

        float maxOffset =
            Mathf.Max(minZOffset, maxZOffset);

        float clampedOffset =
            Mathf.Clamp(
                CurrentZOffset,
                minOffset,
                maxOffset
            );

        /*
         * 기본 설정:
         *
         * maxZOffset = 0
         * minZOffset = -0.1
         *
         * Z = 0    → (0 - 0) / 0.1 = 0
         * Z = -0.1 → (0 - -0.1) / 0.1 = 1
         */
        float range =
            maxZOffset - minZOffset;

        if (Mathf.Abs(range) <= 0.0001f)
        {
            Press01 = 0f;
            return;
        }

        float rawPress =
            (maxZOffset - clampedOffset) /
            range;

        Press01 = Mathf.Clamp01(rawPress);

        if (Mathf.Abs(CurrentZOffset - maxZOffset) <= zDeadzone)
        {
            Press01 = 0f;
        }
    }

    /// <summary>
    /// Press01 값에 따라 Cube 위치를 설정합니다.
    /// </summary>
    private void ApplyCargoPosition(bool immediate)
    {
        float curvedPress =
            pressCurve.Evaluate(Press01);

        Vector3 targetPosition =
            Vector3.Lerp(
                topPoint.position,
                bottomPoint.position,
                curvedPress
            );

        if (immediate)
        {
            cargo.position = targetPosition;
            return;
        }

        cargo.position =
            Vector3.MoveTowards(
                cargo.position,
                targetPosition,
                followSpeed * Time.deltaTime
            );
    }

    private bool ValidateReferences()
    {
        bool valid =
            lever != null &&
            cargo != null &&
            topPoint != null &&
            bottomPoint != null;

        if (!valid && debugLog)
        {
            Debug.LogWarning(
                "[RemoteCargoLeverController] " +
                "Lever, Cargo, TopPoint, BottomPoint 참조를 확인하세요.",
                this
            );
        }

        return valid;
    }

    private void PrintDebugLog()
    {
        if (!debugLog ||
            Time.unscaledTime < _nextDebugTime)
        {
            return;
        }

        _nextDebugTime =
            Time.unscaledTime +
            debugInterval;

        Debug.Log(
            $"[Remote Cargo Press] " +
            $"Lever:{lever.name} | " +
            $"Z Offset:{CurrentZOffset:F3} | " +
            $"Press:{Press01:F2} | " +
            $"Cargo Y:{cargo.position.y:F3}",
            this
        );
    }

    /// <summary>
    /// 현재 레버 위치를 새로운 중립 Z값으로 저장합니다.
    /// </summary>
    [ContextMenu("현재 레버 위치를 중립값으로 저장")]
    public void SaveCurrentLeverPositionAsNeutral()
    {
        if (lever == null)
        {
            return;
        }

        _neutralLeverLocalZ =
            lever.localPosition.z;
    }
}