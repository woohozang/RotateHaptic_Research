using UnityEngine;

public class CargoSwayByTurnAngle : MonoBehaviour
{
    [Header("References")]
    public Transform cartRoot;
    public Transform cargoObject;

    [Header("Turn Accumulation")]
    public float angleToOffset = 0.0025f;   // 회전 각도를 x 오프셋으로 바꾸는 비율
    public float maxOffsetX = 0.18f;        // 최대 좌우 이동
    public float deadZone = 0.2f;           // 너무 작은 회전 무시

    [Header("Spring Physics")]
    public float springStrength = 20f;      // 기존 18보다 살짝 빠르게 목표 위치로 이동
    public float damping = 6.5f;            // 기존 7보다 살짝 가볍게 감쇠
    public float mass = 0.85f;              // 기존 1보다 살짝 가벼운 무게감

    [Header("Bump Impact")]
    public float bumpForce = 0.09f;         // 기존 0.08보다 약간 반응성 증가

    [Header("Smoothing")]
    [Tooltip("목표 지점 자체가 변하는 속도. 낮을수록 무게추의 반응이 묵직하고 부드러워집니다.")]
    public float targetLerpSpeed = 6f;      // 기존 5보다 살짝 빠르게 반응

    private Vector3 _initialLocalPos;
    private float _prevYaw;

    // 누적 회전량
    private float _turnAccumulator;

    // 실제 이동 상태
    private float _currentOffsetX;
    private float _currentVelocityX;

    void Start()
    {
        if (cargoObject == null)
            cargoObject = transform;

        _initialLocalPos = cargoObject.localPosition;
        _prevYaw = NormalizeAngle(cartRoot.eulerAngles.y);
    }

    void LateUpdate()
    {
        if (cartRoot == null || cargoObject == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1. yaw 변화량 측정 (기존과 동일)
        float currentYaw = NormalizeAngle(cartRoot.eulerAngles.y);
        float deltaYaw = Mathf.DeltaAngle(_prevYaw, currentYaw);
        _prevYaw = currentYaw;

        // 2. 개선된 회전량 누적 (데드존 부드럽게 처리)
        if (Mathf.Abs(deltaYaw) > deadZone)
        {
            _turnAccumulator += deltaYaw;
        }
        // 핵심: 누적 회전량 자체를 제한->0709 추가
        float maxAccumAngle = maxOffsetX / Mathf.Max(0.0001f, angleToOffset);
        _turnAccumulator = Mathf.Clamp(_turnAccumulator, -maxAccumAngle, maxAccumAngle);

        // 3. 목표 오프셋 계산 및 보간
        float rawTargetX = _turnAccumulator * angleToOffset;
        rawTargetX = Mathf.Clamp(rawTargetX, -maxOffsetX, maxOffsetX);

        float effectiveTargetLerpSpeed = Mathf.Max(0.01f, targetLerpSpeed) * 1.1f;
        float smoothTargetX = Mathf.Lerp(_currentOffsetX, rawTargetX, dt * effectiveTargetLerpSpeed);

        // 4. spring-damper
        float displacement = _currentOffsetX - smoothTargetX;
        float effectiveSpringStrength = Mathf.Max(0.01f, springStrength) * 1.1f;
        float effectiveDamping = Mathf.Max(0f, damping) * 0.9f;
        float effectiveMass = Mathf.Max(0.0001f, mass) * 0.85f;
        float force = (-effectiveSpringStrength * displacement) - (effectiveDamping * _currentVelocityX);
        float acceleration = force / effectiveMass;

        _currentVelocityX += acceleration * dt;
        _currentOffsetX += _currentVelocityX * dt;
        _currentOffsetX = Mathf.Clamp(_currentOffsetX, -maxOffsetX, maxOffsetX);

        // 5. 위치 반영
        Vector3 local = _initialLocalPos;
        local.x += _currentOffsetX;
        cargoObject.localPosition = local;
    }

    public void AddBumpImpulse(bool fromLeft)
    {
        float dir = fromLeft ? 1f : -1f;
        _currentVelocityX += dir * bumpForce * 1.1f;
    }

    public void ResetCargoCenter()
    {
        _turnAccumulator = 0f;
        _currentOffsetX = 0f;
        _currentVelocityX = 0f;

        Vector3 local = _initialLocalPos;
        cargoObject.localPosition = local;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}



