using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// TwoGrab 결과를 부모 기준 로컬 좌표로 제한합니다.
///
/// Position
/// - X, Y: 초기 위치 고정
/// - Z: 초기 위치 기준 minZOffset ~ maxZOffset
///
/// Rotation
/// - X, Z: 초기 회전 고정
/// - Y: 초기 회전 기준 minYAngle ~ maxYAngle
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class TwoGrabLocalZConstraint : MonoBehaviour
{
    [Header("Grab")]
    [SerializeField]
    private Grabbable grabbable;

    [Header("Local Z 이동 범위")]
    [Tooltip("초기 위치 기준 최소 Z 이동량")]
    [SerializeField]
    private float minZOffset = -0.1f;

    [Tooltip("초기 위치 기준 최대 Z 이동량")]
    [SerializeField]
    private float maxZOffset = 0.1f;

    [Header("Local Y 회전 범위")]
    [Tooltip("초기 회전 기준 최소 Y 회전각")]
    [SerializeField]
    private float minYAngle = -30f;

    [Tooltip("초기 회전 기준 최대 Y 회전각")]
    [SerializeField]
    private float maxYAngle = 30f;

    [Header("양손 조건")]
    [Tooltip("두 손을 모두 잡아야 움직일 수 있습니다.")]
    [SerializeField]
    private bool requireTwoHands = true;

    [Tooltip("두 손 조건을 만족하지 않으면 마지막 유효 위치와 회전을 유지합니다.")]
    [SerializeField]
    private bool lockWhenNotTwoHanded = true;

    [Header("디버그")]
    [SerializeField]
    private bool debugLog;

    private Vector3 _neutralLocalPosition;
    private Quaternion _neutralLocalRotation;

    private Vector3 _lastValidLocalPosition;
    private Quaternion _lastValidLocalRotation;

    /// <summary>
    /// 초기 위치를 기준으로 한 현재 로컬 Z 이동량
    /// </summary>
    public float LocalZOffset { get; private set; }

    /// <summary>
    /// 초기 회전을 기준으로 한 현재 로컬 Y 회전각
    /// </summary>
    public float LocalYAngle { get; private set; }

    /// <summary>
    /// Z 이동 범위 정규화 값
    /// minZOffset = 0, maxZOffset = 1
    /// </summary>
    public float NormalizedZ { get; private set; }

    public int GrabCount =>
        grabbable != null
            ? grabbable.SelectingPointsCount
            : 0;

    private void Reset()
    {
        grabbable = GetComponent<Grabbable>();
    }

    private void Awake()
    {
        if (grabbable == null)
        {
            grabbable = GetComponent<Grabbable>();
        }

        SaveNeutralTransform();
    }

    private void LateUpdate()
    {
        bool isTwoHanded =
            grabbable != null &&
            grabbable.SelectingPointsCount >= 2;

        if (requireTwoHands && !isTwoHanded)
        {
            if (lockWhenNotTwoHanded)
            {
                transform.localPosition =
                    _lastValidLocalPosition;

                transform.localRotation =
                    _lastValidLocalRotation;
            }

            UpdateOutputs();
            return;
        }

        ApplyPositionConstraint();
        ApplyRotationConstraint();

        _lastValidLocalPosition =
            transform.localPosition;

        _lastValidLocalRotation =
            transform.localRotation;

        UpdateOutputs();

        if (debugLog)
        {
            Debug.Log(
                $"[TwoGrab Constraint] " +
                $"Grab:{GrabCount} | " +
                $"Z:{LocalZOffset:F3} | " +
                $"Y Angle:{LocalYAngle:F1}°"
            );
        }
    }

    /// <summary>
    /// X/Y는 초기 위치로 고정하고,
    /// Z만 지정 범위에서 이동하도록 제한합니다.
    /// </summary>
    private void ApplyPositionConstraint()
    {
        Vector3 currentPosition =
            transform.localPosition;

        float currentZOffset =
            currentPosition.z -
            _neutralLocalPosition.z;

        float clampedZOffset =
            Mathf.Clamp(
                currentZOffset,
                minZOffset,
                maxZOffset
            );

        transform.localPosition =
            new Vector3(
                _neutralLocalPosition.x,
                _neutralLocalPosition.y,
                _neutralLocalPosition.z +
                clampedZOffset
            );
    }

    /// <summary>
    /// 초기 회전 기준 상대 회전을 구한 뒤,
    /// X/Z 회전은 제거하고 Y축만 -30~30도로 제한합니다.
    /// </summary>
    private void ApplyRotationConstraint()
    {
        // 초기 회전을 기준으로 한 상대 회전
        Quaternion relativeRotation =
            Quaternion.Inverse(_neutralLocalRotation) *
            transform.localRotation;

        // 0~360도를 -180~180도 값으로 변환
        float relativeYAngle =
            Mathf.DeltaAngle(
                0f,
                relativeRotation.eulerAngles.y
            );

        float clampedYAngle =
            Mathf.Clamp(
                relativeYAngle,
                minYAngle,
                maxYAngle
            );

        // X = 0, Z = 0
        // Y만 제한된 각도 적용
        transform.localRotation =
            _neutralLocalRotation *
            Quaternion.Euler(
                0f,
                clampedYAngle,
                0f
            );
    }

    private void UpdateOutputs()
    {
        LocalZOffset =
            transform.localPosition.z -
            _neutralLocalPosition.z;

        NormalizedZ =
            Mathf.InverseLerp(
                minZOffset,
                maxZOffset,
                LocalZOffset
            );

        Quaternion relativeRotation =
            Quaternion.Inverse(_neutralLocalRotation) *
            transform.localRotation;

        LocalYAngle =
            Mathf.DeltaAngle(
                0f,
                relativeRotation.eulerAngles.y
            );
    }

    [ContextMenu("현재 Transform을 중립값으로 저장")]
    public void SaveNeutralTransform()
    {
        _neutralLocalPosition =
            transform.localPosition;

        _neutralLocalRotation =
            transform.localRotation;

        _lastValidLocalPosition =
            _neutralLocalPosition;

        _lastValidLocalRotation =
            _neutralLocalRotation;

        UpdateOutputs();
    }

    [ContextMenu("중립 위치로 복귀")]
    public void ResetToNeutral()
    {
        transform.localPosition =
            _neutralLocalPosition;

        transform.localRotation =
            _neutralLocalRotation;

        _lastValidLocalPosition =
            _neutralLocalPosition;

        _lastValidLocalRotation =
            _neutralLocalRotation;

        UpdateOutputs();
    }
}