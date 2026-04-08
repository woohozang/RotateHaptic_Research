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
    public float springStrength = 18f;      // 목표 위치로 가는 힘
    public float damping = 7f;              // 감쇠
    public float mass = 1f;                 // 무게감

    [Header("Bump Impact")]
    public float bumpForce = 0.08f;         // 방지턱 순간 충격량

    [Header("Smoothing")]
    [Tooltip("목표 지점 자체가 변하는 속도. 낮을수록 무게추의 반응이 묵직하고 부드러워집니다.")]
    public float targetLerpSpeed = 5f;

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

        // 2. 🔥 개선된 회전량 누적 (데드존 부드럽게 처리)
        // 하드하게 끊지 않고, 데드존을 뺀 나머지 값만 부드럽게 더해지도록 수정 가능
        if (Mathf.Abs(deltaYaw) > deadZone)
        {
            _turnAccumulator += deltaYaw;
        }

        // 3. 🔥 목표 오프셋 계산 및 보간 (핵심 수정)
        float rawTargetX = _turnAccumulator * angleToOffset;
        rawTargetX = Mathf.Clamp(rawTargetX, -maxOffsetX, maxOffsetX);

        // 💡 [추가된 로직] 목표 지점(Target) 자체를 Lerp로 부드럽게 이동시킴
        // 이렇게 하면 _turnAccumulator가 튀어도 스프링의 목적지가 서서히 움직입니다.
        float smoothTargetX = Mathf.Lerp(_currentOffsetX, rawTargetX, dt * targetLerpSpeed);

        // 4. spring-damper (목표값을 smoothTargetX로 변경)
        float displacement = _currentOffsetX - smoothTargetX;
        float force = (-springStrength * displacement) - (damping * _currentVelocityX);
        float acceleration = force / Mathf.Max(mass, 0.0001f);

        _currentVelocityX += acceleration * dt;
        _currentOffsetX += _currentVelocityX * dt;
        _currentOffsetX = Mathf.Clamp(_currentOffsetX, -maxOffsetX, maxOffsetX);

        // 5. 위치 반영 (기존과 동일)
        Vector3 local = _initialLocalPos;
        local.x += _currentOffsetX;
        cargoObject.localPosition = local;
    }

    public void AddBumpImpulse(bool fromLeft)
    {
        float dir = fromLeft ? 1f : -1f;
        _currentVelocityX += dir * bumpForce;
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