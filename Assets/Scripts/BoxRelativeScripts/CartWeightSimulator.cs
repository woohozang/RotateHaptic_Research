using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// 쇼핑카트 무게감 시뮬레이터 - 오프셋 강화 버전
/// </summary>
public class CartWeightSimulator : MonoBehaviour
{
    [Header("Visual Handle References")]
    [SerializeField] private Transform leftHandVisualHandle;
    [SerializeField] private Transform rightHandVisualHandle;

    [Header("Hand Transform References - 필수!")]
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;

    [Header("C/D Ratio Settings")]
    [SerializeField] private float leftHandCDRatio = 0.5f;
    [SerializeField] private float rightHandCDRatio = 1.0f;

    [Header("Dynamic C/D Adjustment")]
    [SerializeField] private bool useDynamicCDRatio = false; // ⭐ 일단 false로
    [SerializeField] private float minCDRatio = 0.3f;
    [SerializeField] private float maxCDRatio = 0.7f;
    [SerializeField] private float velocityThreshold = 1.5f;

    [Header("Smoothing")]
    [SerializeField] private float responsiveness = 5f; // ⭐ 새로운 파라미터
    [Range(0f, 1f)]
    [SerializeField] private float dampingFactor = 0.8f; // ⭐ 새로운 파라미터

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private bool showDebugSpheres = true; // ⭐ 새로운 옵션

    private bool isBothHandsGrabbing = false;

    // 왼손 추적
    private Vector3 leftHandStartPos;
    private Vector3 leftVisualStartPos;
    private Vector3 leftPreviousPos;
    private Vector3 leftCurrentVisualPos; // ⭐ 추가
    private Vector3 leftVelocity;

    // 오른손 추적
    private Vector3 rightHandStartPos;
    private Vector3 rightVisualStartPos;
    private Vector3 rightPreviousPos;
    private Vector3 rightCurrentVisualPos; // ⭐ 추가

    private void Update()
    {
        if (!isBothHandsGrabbing) return;
        if (leftHandTransform == null || rightHandTransform == null)
        {
            Debug.LogError("[CartWeight] Hand transforms not assigned!");
            return;
        }

        UpdateHandTracking();
    }

    private void UpdateHandTracking()
    {
        // 1. 현재 손 위치
        Vector3 leftCurrentPos = leftHandTransform.position;
        Vector3 rightCurrentPos = rightHandTransform.position;

        // 2. 속도 계산
        leftVelocity = (leftCurrentPos - leftPreviousPos) / Time.deltaTime;

        // 3. 시작점 대비 총 이동 거리
        Vector3 leftTotalMovement = leftCurrentPos - leftHandStartPos;
        Vector3 rightTotalMovement = rightCurrentPos - rightHandStartPos;

        // 4. C/D Ratio 계산
        float currentLeftCDRatio = leftHandCDRatio;
        if (useDynamicCDRatio)
        {
            currentLeftCDRatio = CalculateDynamicCDRatio(leftVelocity.magnitude);
        }

        // 5. 목표 시각적 위치 (C/D Ratio 적용)
        Vector3 leftTargetVisualPos = leftVisualStartPos + (leftTotalMovement * currentLeftCDRatio);
        Vector3 rightTargetVisualPos = rightVisualStartPos + (rightTotalMovement * rightHandCDRatio);

        // 6. 부드러운 이동 (Lerp 사용 - 더 예측 가능)
        float lerpSpeed = responsiveness * Time.deltaTime;

        leftCurrentVisualPos = Vector3.Lerp(leftCurrentVisualPos, leftTargetVisualPos, lerpSpeed);
        rightCurrentVisualPos = Vector3.Lerp(rightCurrentVisualPos, rightTargetVisualPos, lerpSpeed * 2f);

        // 7. 위치 적용
        if (leftHandVisualHandle != null)
        {
            leftHandVisualHandle.position = leftCurrentVisualPos;
        }

        if (rightHandVisualHandle != null)
        {
            rightHandVisualHandle.position = rightCurrentVisualPos;
        }

        // 8. 디버그 정보
        if (showDebugInfo)
        {
            float leftActualDistance = leftTotalMovement.magnitude;
            float leftVisualDistance = (leftCurrentVisualPos - leftVisualStartPos).magnitude;
            float leftOffset = Vector3.Distance(leftCurrentPos, leftCurrentVisualPos);

            Debug.Log($"<color=cyan>[Left Hand]</color> " +
                      $"실제: <color=yellow>{leftActualDistance:F3}m</color> | " +
                      $"시각: <color=green>{leftVisualDistance:F3}m</color> | " +
                      $"C/D: <color=orange>{currentLeftCDRatio:F2}</color> | " +
                      $"오프셋: <color=red>{leftOffset:F3}m</color> | " +
                      $"속도: {leftVelocity.magnitude:F2} m/s");
        }

        // 9. 이전 위치 저장
        leftPreviousPos = leftCurrentPos;
        rightPreviousPos = rightCurrentPos;
    }

    private float CalculateDynamicCDRatio(float velocity)
    {
        if (velocity < 0.3f)
            return maxCDRatio;
        else if (velocity > velocityThreshold)
            return minCDRatio;
        else
        {
            float t = (velocity - 0.3f) / (velocityThreshold - 0.3f);
            return Mathf.Lerp(maxCDRatio, minCDRatio, t);
        }
    }

    public void OnBothHandsGrabbed()
    {
        if (leftHandTransform == null || rightHandTransform == null)
        {
            Debug.LogError("[CartWeight] Cannot start - Hand transforms not assigned!");
            return;
        }

        isBothHandsGrabbing = true;

        // 시작 위치 기록
        leftHandStartPos = leftHandTransform.position;
        rightHandStartPos = rightHandTransform.position;

        // 시각적 핸들 시작 위치
        leftVisualStartPos = leftHandStartPos;
        rightVisualStartPos = rightHandStartPos;

        // 현재 위치 초기화
        leftCurrentVisualPos = leftVisualStartPos;
        rightCurrentVisualPos = rightVisualStartPos;

        // 이전 위치 초기화
        leftPreviousPos = leftHandStartPos;
        rightPreviousPos = rightHandStartPos;
        leftVelocity = Vector3.zero;

        // 시각적 핸들 위치 설정
        if (leftHandVisualHandle != null)
        {
            leftHandVisualHandle.position = leftVisualStartPos;
        }

        if (rightHandVisualHandle != null)
        {
            rightHandVisualHandle.position = rightVisualStartPos;
        }

        Debug.Log($"<color=lime>[CartWeight] ✓ 양손 잡기 성공 - 추적 시작</color>");
        Debug.Log($"왼손 시작: {leftHandStartPos}");
        Debug.Log($"오른손 시작: {rightHandStartPos}");
    }

    public void OnHandsReleased()
    {
        isBothHandsGrabbing = false;
        Debug.Log("<color=red>[CartWeight] 손 놓음 - 추적 중지</color>");
    }

    public void OnGrabBegin() => OnBothHandsGrabbed();
    public void OnGrabEnd() => OnHandsReleased();
    public void OnLeftHandGrab(bool grabbed) { }
    public void OnRightHandGrab(bool grabbed) { }

    private void OnDrawGizmos()
    {
        if (!showGizmos || !Application.isPlaying || !isBothHandsGrabbing) return;
        if (leftHandTransform == null || rightHandTransform == null) return;

        Vector3 leftRealPos = leftHandTransform.position;
        Vector3 rightRealPos = rightHandTransform.position;

        // === 왼손 시각화 ===

        // 실제 손 위치 (빨강, 크게)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(leftRealPos, 0.05f);
        Gizmos.DrawSphere(leftRealPos, 0.02f);

        // 시각적 핸들 (파랑, 크게)
        if (leftHandVisualHandle != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftHandVisualHandle.position, 0.045f);
            Gizmos.DrawSphere(leftHandVisualHandle.position, 0.018f);

            // 오프셋 라인 (노랑, 두껍게)
            float offset = Vector3.Distance(leftRealPos, leftHandVisualHandle.position);
            if (offset > 0.001f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(leftRealPos, leftHandVisualHandle.position);

                // 중간점에 작은 구체
                Vector3 midPoint = (leftRealPos + leftHandVisualHandle.position) / 2f;
                Gizmos.DrawSphere(midPoint, 0.015f);

#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.yellow;
                UnityEditor.Handles.Label(midPoint + Vector3.up * 0.05f, $"오프셋: {offset * 100f:F1}cm");
#endif
            }
        }

        // === 오른손 시각화 ===

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(rightRealPos, 0.05f);
        Gizmos.DrawSphere(rightRealPos, 0.02f);

        if (rightHandVisualHandle != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(rightHandVisualHandle.position, 0.045f);
            Gizmos.DrawSphere(rightHandVisualHandle.position, 0.018f);
        }

        // === 시작점 표시 ===

        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Gizmos.DrawWireSphere(leftHandStartPos, 0.03f);
        Gizmos.DrawWireSphere(rightHandStartPos, 0.03f);

        // === 이동 경로 ===

        Gizmos.color = new Color(1, 0.5f, 0, 0.3f);
        Gizmos.DrawLine(leftHandStartPos, leftRealPos);
        Gizmos.DrawLine(rightHandStartPos, rightRealPos);

        // === 거리 정보 표시 ===

#if UNITY_EDITOR
        float leftDistance = Vector3.Distance(leftHandStartPos, leftRealPos);
        float leftVisualDistance = Vector3.Distance(leftVisualStartPos, leftCurrentVisualPos);
        
        UnityEditor.Handles.color = Color.red;
        UnityEditor.Handles.Label(leftRealPos + Vector3.up * 0.15f, $"실제: {leftDistance:F2}m");
        
        UnityEditor.Handles.color = Color.blue;
        UnityEditor.Handles.Label(leftCurrentVisualPos + Vector3.up * 0.1f, $"시각: {leftVisualDistance:F2}m");
#endif
    }
}