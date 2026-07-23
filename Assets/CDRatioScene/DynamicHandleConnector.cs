using UnityEngine;

/// <summary>
/// 왼쪽 손잡이의 LeftPoint와 오른쪽 손잡이의 RightPoint 사이에
/// 연결 봉을 실시간으로 배치합니다.
///
/// - 왼손만 움직이면 왼쪽 끝이 LeftPoint를 따라갑니다.
/// - 오른손만 움직이면 오른쪽 끝이 RightPoint를 따라갑니다.
/// - 양손이 움직이면 양 끝을 모두 따라갑니다.
///
/// Unity 기본 Cylinder의 길이 방향인 Local Y축을 기준으로 합니다.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class DynamicHandleConnector : MonoBehaviour
{
    [Header("연결 지점")]
    [SerializeField]
    private Transform leftPoint;

    [SerializeField]
    private Transform rightPoint;

    [Header("연결 봉 Visual")]
    [Tooltip("SharedConnector 자식의 Cylinder Visual")]
    [SerializeField]
    private Transform connectorCylinder;

    [Tooltip("Unity 기본 Cylinder는 높이가 2이므로 기본값 2")]
    [Min(0.0001f)]
    [SerializeField]
    private float cylinderBaseLength = 2f;

    [Header("표현 설정")]
    [Tooltip("두 손잡이의 높이가 조금 달라도 연결 봉을 수평으로 유지합니다.")]
    [SerializeField]
    private bool keepHorizontal = true;

    [Tooltip("위치와 회전에 스무딩을 적용합니다. 0이면 즉시 따라갑니다.")]
    [Min(0f)]
    [SerializeField]
    private float followSmooth = 0f;

    [Header("길이 제한")]
    [Tooltip("연결 봉이 지나치게 짧아지는 것을 방지합니다.")]
    [Min(0f)]
    [SerializeField]
    private float minimumLength = 0.05f;

    [Tooltip("길이를 제한하지 않으려면 0으로 설정합니다.")]
    [Min(0f)]
    [SerializeField]
    private float maximumLength = 0f;

    private Vector3 _initialCylinderScale;

    private void Reset()
    {
        if (transform.childCount > 0)
        {
            connectorCylinder = transform.GetChild(0);
        }
    }

    private void Awake()
    {
        CacheInitialScale();
    }

    private void OnEnable()
    {
        CacheInitialScale();
        UpdateConnector(true);
    }

    private void LateUpdate()
    {
        UpdateConnector(!Application.isPlaying);
    }

    private void OnValidate()
    {
        cylinderBaseLength = Mathf.Max(0.0001f, cylinderBaseLength);
        minimumLength = Mathf.Max(0f, minimumLength);
        maximumLength = Mathf.Max(0f, maximumLength);

        CacheInitialScale();
        UpdateConnector(true);
    }

    private void CacheInitialScale()
    {
        if (connectorCylinder == null)
        {
            return;
        }

        /*
         * X/Z는 봉의 굵기이므로 초기값을 보존합니다.
         * Y는 두 Point 사이의 길이에 따라 매 프레임 변경됩니다.
         */
        _initialCylinderScale = connectorCylinder.localScale;
    }

    private void UpdateConnector(bool instant)
    {
        if (leftPoint == null ||
            rightPoint == null ||
            connectorCylinder == null)
        {
            return;
        }

        Vector3 leftPosition = leftPoint.position;
        Vector3 rightPosition = rightPoint.position;

        /*
         * 양쪽 손잡이가 미세하게 위아래로 어긋나도
         * 중앙 연결 봉은 수평으로 유지합니다.
         */
        if (keepHorizontal)
        {
            float averageY =
                (leftPosition.y + rightPosition.y) * 0.5f;

            leftPosition.y = averageY;
            rightPosition.y = averageY;
        }

        Vector3 connectorVector =
            rightPosition - leftPosition;

        float distance = connectorVector.magnitude;

        if (distance < 0.0001f)
        {
            return;
        }

        distance = Mathf.Max(distance, minimumLength);

        if (maximumLength > 0f)
        {
            distance = Mathf.Min(distance, maximumLength);
        }

        Vector3 direction =
            connectorVector.normalized;

        Vector3 targetPosition =
            (leftPosition + rightPosition) * 0.5f;

        /*
         * Unity 기본 Cylinder의 길이 방향인 Local Y축을
         * LeftPoint → RightPoint 방향으로 맞춥니다.
         */
        Quaternion targetRotation =
            Quaternion.FromToRotation(
                Vector3.up,
                direction
            );

        if (instant || followSmooth <= 0f)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }
        else
        {
            float lerpAmount =
                1f - Mathf.Exp(
                    -followSmooth * Time.deltaTime
                );

            transform.position =
                Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    lerpAmount
                );

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    lerpAmount
                );
        }

        /*
         * Unity 기본 Cylinder의 원래 높이는 2입니다.
         * 따라서 Scale Y = Point 간 거리 / 2
         */
        Vector3 cylinderScale =
            _initialCylinderScale;

        cylinderScale.y =
            distance / cylinderBaseLength;

        connectorCylinder.localScale =
            cylinderScale;
    }
}