using UnityEngine;

/// <summary>
/// 두 앵커 사이에 LineRenderer 기반 코일 스프링을 생성합니다.
///
/// End Anchor가 움직이는 Cube의 자식이라면
/// Cube가 내려올 때 두 Anchor 사이 거리가 줄어들면서
/// 스프링이 자동으로 압축됩니다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class SpringCoilVisual : MonoBehaviour
{
    [Header("스프링 연결점")]

    [Tooltip("고정된 아래쪽 Anchor")]
    [SerializeField]
    private Transform startAnchor;

    [Tooltip("움직이는 Cube에 붙어 있는 위쪽 Anchor")]
    [SerializeField]
    private Transform endAnchor;

    [Header("코일 형태")]

    [Min(1)]
    [SerializeField]
    private int coilCount = 8;

    [Min(4)]
    [SerializeField]
    private int pointsPerCoil = 14;

    [Min(0.001f)]
    [SerializeField]
    private float coilRadius = 0.035f;

    [Header("압축 표현")]

    [Tooltip("압축될 때 코일 반지름이 약간 넓어지는 배율")]
    [Min(1f)]
    [SerializeField]
    private float compressedRadiusMultiplier = 1.15f;

    [Tooltip("활성화될 때 현재 길이를 스프링의 원래 길이로 저장합니다.")]
    [SerializeField]
    private bool captureRestLengthOnEnable = true;

    [Tooltip("스프링이 압축되지 않았을 때의 길이")]
    [Min(0.0001f)]
    [SerializeField]
    private float restLength = 1f;

    [Header("와이어 설정")]

    [Min(0.001f)]
    [SerializeField]
    private float wireWidth = 0.008f;

    [SerializeField]
    private Material springMaterial;

    [Header("표현 설정")]

    [Tooltip("스프링 양 끝이 앵커 중심으로 모이게 합니다.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float endRadiusFalloff = 1f;

    [Header("디버그")]

    [SerializeField]
    private bool debugLog;

    [Min(0.05f)]
    [SerializeField]
    private float debugInterval = 0.25f;

    private LineRenderer _lineRenderer;
    private float _nextDebugTime;

    /// <summary>
    /// 0 = 원래 길이 또는 늘어난 상태
    /// 1 = 완전히 압축된 상태에 가까움
    /// </summary>
    public float Compression01 { get; private set; }

    /// <summary>
    /// 현재 두 Anchor 사이의 거리
    /// </summary>
    public float CurrentLength { get; private set; }

    private void OnEnable()
    {
        InitializeLineRenderer();

        if (captureRestLengthOnEnable)
        {
            CaptureCurrentLengthAsRest();
        }

        DrawSpring();
    }

    private void LateUpdate()
    {
        DrawSpring();
        PrintDebugLog();
    }

    private void OnValidate()
    {
        coilCount =
            Mathf.Max(1, coilCount);

        pointsPerCoil =
            Mathf.Max(4, pointsPerCoil);

        coilRadius =
            Mathf.Max(0.001f, coilRadius);

        compressedRadiusMultiplier =
            Mathf.Max(1f, compressedRadiusMultiplier);

        wireWidth =
            Mathf.Max(0.001f, wireWidth);

        restLength =
            Mathf.Max(0.0001f, restLength);

        InitializeLineRenderer();
        DrawSpring();
    }

    private void InitializeLineRenderer()
    {
        if (_lineRenderer == null)
        {
            _lineRenderer =
                GetComponent<LineRenderer>();
        }

        _lineRenderer.useWorldSpace = true;
        _lineRenderer.loop = false;

        _lineRenderer.startWidth = wireWidth;
        _lineRenderer.endWidth = wireWidth;

        _lineRenderer.numCornerVertices = 3;
        _lineRenderer.numCapVertices = 3;

        if (springMaterial != null)
        {
            _lineRenderer.sharedMaterial =
                springMaterial;
        }
    }

    private void DrawSpring()
    {
        if (_lineRenderer == null)
        {
            InitializeLineRenderer();
        }

        if (startAnchor == null ||
            endAnchor == null)
        {
            _lineRenderer.positionCount = 0;
            return;
        }

        Vector3 start =
            startAnchor.position;

        Vector3 end =
            endAnchor.position;

        Vector3 springVector =
            end - start;

        CurrentLength =
            springVector.magnitude;

        if (CurrentLength <= 0.0001f)
        {
            _lineRenderer.positionCount = 0;
            return;
        }

        /*
         * 현재 길이가 Rest Length보다 짧아질수록
         * Compression01이 증가합니다.
         */
        Compression01 =
            Mathf.Clamp01(
                1f -
                CurrentLength /
                Mathf.Max(restLength, 0.0001f)
            );

        float currentCoilRadius =
            coilRadius *
            Mathf.Lerp(
                1f,
                compressedRadiusMultiplier,
                Compression01
            );

        Vector3 springAxis =
            springVector /
            CurrentLength;

        Vector3 referenceAxis =
            Mathf.Abs(
                Vector3.Dot(
                    springAxis,
                    Vector3.up
                )
            ) > 0.95f
                ? Vector3.right
                : Vector3.up;

        Vector3 basisX =
            Vector3.Cross(
                springAxis,
                referenceAxis
            ).normalized;

        Vector3 basisY =
            Vector3.Cross(
                springAxis,
                basisX
            ).normalized;

        int pointCount =
            coilCount *
            pointsPerCoil +
            1;

        _lineRenderer.positionCount =
            pointCount;

        _lineRenderer.startWidth =
            wireWidth;

        _lineRenderer.endWidth =
            wireWidth;

        for (int i = 0; i < pointCount; i++)
        {
            float t =
                i /
                (float)(pointCount - 1);

            Vector3 centerPosition =
                Vector3.Lerp(
                    start,
                    end,
                    t
                );

            float coilAngle =
                t *
                coilCount *
                Mathf.PI *
                2f;

            float radiusEnvelope =
                Mathf.Sin(
                    t *
                    Mathf.PI
                );

            radiusEnvelope =
                Mathf.Lerp(
                    1f,
                    radiusEnvelope,
                    endRadiusFalloff
                );

            Vector3 radialOffset =
                basisX *
                Mathf.Cos(coilAngle) *
                currentCoilRadius *
                radiusEnvelope
                +
                basisY *
                Mathf.Sin(coilAngle) *
                currentCoilRadius *
                radiusEnvelope;

            _lineRenderer.SetPosition(
                i,
                centerPosition +
                radialOffset
            );
        }
    }

    /// <summary>
    /// 현재 Anchor 간 거리를 원래 스프링 길이로 저장합니다.
    /// </summary>
    [ContextMenu("현재 길이를 Rest Length로 저장")]
    public void CaptureCurrentLengthAsRest()
    {
        if (startAnchor == null ||
            endAnchor == null)
        {
            return;
        }

        restLength =
            Vector3.Distance(
                startAnchor.position,
                endAnchor.position
            );
    }

    private void PrintDebugLog()
    {
        if (!debugLog ||
            !Application.isPlaying ||
            Time.unscaledTime < _nextDebugTime)
        {
            return;
        }

        _nextDebugTime =
            Time.unscaledTime +
            debugInterval;

        Debug.Log(
            $"[Spring Visual] " +
            $"Length:{CurrentLength:F3} | " +
            $"Rest:{restLength:F3} | " +
            $"Compression:{Compression01:F2}",
            this
        );
    }
}