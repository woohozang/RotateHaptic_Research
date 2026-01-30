using UnityEngine;
using Oculus.Interaction;

public class DynamicCargoInertiaTransformer : MonoBehaviour, ITransformer
{
    [Header("Base Settings")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformer;
    private ITransformer _base;
    private IGrabbable _grabbable;

    [Header("Virtual Cargo Physics (Watermelon)")]
    public float cargoSensitivity = 2.5f; // 원심력 반응 민감도
    public float cargoFriction = 0.92f;    // 굴러가는 마찰력 (Damping)

    [Header("C/D Ratio (Mass Perception)")]
    public float baseCD = 0.8f;           // 빈 카트일 때 회전 비율
    public float maxWeightPenalty = 0.4f; // 짐이 끝에 있을 때 깎일 비율

    [Header("Impulse Haptics (Wall Hit)")]
    [Range(0, 1)] public float thudAmplitude = 0.6f; // 부딪힐 때 진동 세기
    public float thudDuration = 0.15f;               // 진동 지속 시간
    private bool _hasHitWall = false;                // 중복 충돌 방지

    private float _cargoPos = 0f;  // -1(좌) ~ 1(우) 가상 위치
    private float _cargoVel = 0f;  // 가상 속도
    private Quaternion _lastBaseRot;
    private Quaternion _virtualRot;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
        _base = _baseTransformer as ITransformer;
        _base.Initialize(grabbable);
    }

    public void BeginTransform()
    {
        _base.BeginTransform();
        _virtualRot = _grabbable.Transform.rotation;
        _lastBaseRot = _grabbable.Transform.rotation;
        _cargoPos = 0f;
        _cargoVel = 0f;
    }

    public void UpdateTransform()
    {
        _base.UpdateTransform();
        Quaternion currentBaseRot = _grabbable.Transform.rotation;
        float dt = Time.deltaTime;

        // 1. 카트 회전 데이터로부터 각속도 추출
        Quaternion deltaRot = currentBaseRot * Quaternion.Inverse(_lastBaseRot);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        float angularVelocity = (angle * Mathf.Deg2Rad) / dt;

        // 2. 가상 수박 이동 계산 (원심력: F = m * v^2 / r 의 간략화 모델)
        // 카트가 회전하는 방향의 반대쪽으로 가속도가 붙음
        float accel = angularVelocity * cargoSensitivity;
        _cargoVel += accel * dt;
        _cargoVel *= cargoFriction;
        _cargoPos += _cargoVel * dt;

        // 3. 벽 충돌 처리 및 임펄스 햅틱
        CheckWallCollision();

        _cargoPos = Mathf.Clamp(_cargoPos, -1f, 1f);

        // 4. 비대칭 C/D Ratio 계산
        // 수박이 치우칠수록(abs(_cargoPos)가 클수록) 회전이 묵직해짐
        float dynamicCD = baseCD - (Mathf.Abs(_cargoPos) * maxWeightPenalty);

        // 5. 회전 보정 적용
        Quaternion scaledDelta = Quaternion.Slerp(Quaternion.identity, deltaRot, dynamicCD);
        _virtualRot = scaledDelta * _virtualRot;

        _grabbable.Transform.rotation = _virtualRot;
        _lastBaseRot = currentBaseRot;
    }

    private void CheckWallCollision()
    {
        // 왼쪽 또는 오른쪽 벽에 도달했을 때
        if (Mathf.Abs(_cargoPos) >= 1f)
        {
            if (!_hasHitWall && Mathf.Abs(_cargoVel) > 0.5f) // 일정 속도 이상일 때만
            {
                TriggerImpulse();
                _cargoVel = 0f; // 부딪히면 속도 정지
                _hasHitWall = true;
            }
        }
        else
        {
            _hasHitWall = false;
        }
    }

    private void TriggerImpulse()
    {
        // 수박이 부딪힌 쪽 컨트롤러에만 '쿵' 하는 진동 발생
        OVRInput.Controller target = (_cargoPos > 0) ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

        // 간단한 코루틴 대신 StartVibration 사용 (Interaction SDK 방식)
        OVRInput.SetControllerVibration(0.2f, thudAmplitude, target);
        Invoke(nameof(StopVibration), thudDuration);

        Debug.Log($"<color=yellow>[Cargo] 벽 충돌! 위치: {(_cargoPos > 0 ? "오른쪽" : "왼쪽")}</color>");
    }

    private void StopVibration()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.All);
    }

    public void EndTransform() => _base.EndTransform();

    // 외부 햅틱 스크립트에서 참조할 수 있도록 위치값 노출
    public float GetCargoPosition() => _cargoPos;
}