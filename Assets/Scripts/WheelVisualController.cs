using UnityEngine;

public class WheelVisualController : MonoBehaviour
{
    [Header("카트 루트 참조")]
    public Transform cartRoot; // Rigidbody가 있는 루트 오브젝트

    [Header("바퀴 설정")]
    public float wheelRadius = 0.05f;
    public float steeringSensitivity = 15f;
    public float maxSteerAngle = 40f;
    public float steerLerpSpeed = 5f;

    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private float _currentRollAngle = 0f;
    private float _currentSteerAngle = 0f;

    void Start()
    {
        if (cartRoot != null)
        {
            _lastPosition = cartRoot.position;
            _lastRotation = cartRoot.rotation;
        }
    }

    void Update()
    {
        if (cartRoot == null) return;

        // --- 1. 전진/후진 구름 (X축) 계산 ---
        // 프레임당 실제 이동 거리 계산
        Vector3 worldDelta = cartRoot.position - _lastPosition;
        // 카트의 앞방향(Forward)으로 얼마나 이동했는지 투영
        float forwardDistance = Vector3.Dot(worldDelta, cartRoot.forward);

        // 이동 거리에 따른 회전각 계산 (Angle = Distance / Radius * Rad2Deg)
        float rollIncrement = (forwardDistance / wheelRadius) * Mathf.Rad2Deg;
        _currentRollAngle += rollIncrement;

        // --- 2. 좌우 조향 (Z축) 계산 ---
        // 프레임당 실제 회전량(Y축 각속도 대용) 계산
        Quaternion deltaRotation = cartRoot.rotation * Quaternion.Inverse(_lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        // Y축 회전 성분 추출
        float turnAmount = (axis.y > 0 ? angle : -angle) / Time.deltaTime;

        float targetSteer = turnAmount * steeringSensitivity * 0.01f; // 감도 조절
        targetSteer = Mathf.Clamp(targetSteer, -maxSteerAngle, maxSteerAngle);
        _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, targetSteer, Time.deltaTime * steerLerpSpeed);

        // --- 3. 최종 회전 적용 ---
        transform.localRotation = Quaternion.Euler(_currentRollAngle, 0f, _currentSteerAngle);

        // 다음 프레임을 위해 현재 상태 저장
        _lastPosition = cartRoot.position;
        _lastRotation = cartRoot.rotation;
    }
}