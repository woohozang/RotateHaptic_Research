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

        // 1. yaw 변화량 측정
        float currentYaw = NormalizeAngle(cartRoot.eulerAngles.y);
        float deltaYaw = Mathf.DeltaAngle(_prevYaw, currentYaw);
        _prevYaw = currentYaw;

        // 2. 회전량 누적
        if (Mathf.Abs(deltaYaw) > deadZone)
        {
            _turnAccumulator += deltaYaw;
        }

        // 3. 목표 오프셋 계산
        float targetOffsetX = _turnAccumulator * angleToOffset;
        targetOffsetX = Mathf.Clamp(targetOffsetX, -maxOffsetX, maxOffsetX);

        // 4. spring-damper로 자연스럽게 따라감
        float displacement = _currentOffsetX - targetOffsetX;
        float force = (-springStrength * displacement) - (damping * _currentVelocityX);
        float acceleration = force / Mathf.Max(mass, 0.0001f);

        _currentVelocityX += acceleration * dt;
        _currentOffsetX += _currentVelocityX * dt;
        _currentOffsetX = Mathf.Clamp(_currentOffsetX, -maxOffsetX, maxOffsetX);

        // 5. 위치 반영
        Vector3 local = _initialLocalPos;
        local.x += _currentOffsetX;
        cargoObject.localPosition = local;

        Debug.Log($"cartRoot yaw = {cartRoot.eulerAngles.y}, cargo localX = {cargoObject.localPosition.x}");
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