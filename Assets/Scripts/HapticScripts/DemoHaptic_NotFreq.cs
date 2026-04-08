using UnityEngine;
using Oculus.Interaction;

public class PhysicalForceHaptics : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;    // 카트 전체
    public Transform cartPivot;   // 카트 중앙 바닥
    public Transform cargoObject; // 무게추

    [Header("Physical Mapping Settings")]
    [Tooltip("무게에 따른 기본 마찰 저항 계수")]
    public float weightFrictionScale = 0.5f;
    [Tooltip("이동 속도가 진동에 미치는 영향")]
    public float velocitySensitivity = 0.3f;
    [Tooltip("회전 관성이 진동에 미치는 영향")]
    public float rotationInertiaScale = 1.5f;

    [Header("Asymmetry Tuning")]
    [Tooltip("반대쪽 손의 진동을 얼마나 더 죽일 것인가? (0에 가까울수록 반대쪽 진동이 사라짐)")]
    [Range(0f, 1f)] public float weakSideRatio = 0.2f;

    [Header("Haptic Limits")]
    [Range(0f, 1f)] public float minForceAmp = 0.05f;
    [Range(0f, 1f)] public float maxForceAmp = 0.9f;
    [Tooltip("진동 질감 고정 (0.3~0.5 사이가 묵직함)")]
    public float fixedFrequency = 0.4f;

    private Vector3 _lastPosition;
    private float _lastYaw;
    private float _currentLeftAmp;
    private float _currentRightAmp;

    void Start()
    {
        if (cartRoot != null)
        {
            _lastPosition = cartRoot.position;
            _lastYaw = cartRoot.eulerAngles.y;
        }
    }

    void Update()
    {
        if (grabbable == null || grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            return;
        }

        CalculatePhysicalForce();
    }

    private void CalculatePhysicalForce()
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;

        // 1. 물리적 베이스 힘 측정 (기존 동일)
        Vector3 velocity = (cartRoot.position - _lastPosition) / dt;
        float speed = velocity.magnitude;
        _lastPosition = cartRoot.position;

        float currentYaw = cartRoot.eulerAngles.y;
        float deltaYaw = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, currentYaw)) / dt;
        _lastYaw = currentYaw;

        // 2. 무게 편향(Bias) 측정 (0~1 범위로 정규화)
        float localX = cartPivot.InverseTransformPoint(cargoObject.position).x;
        // bias: 왼쪽 -1, 중앙 0, 오른쪽 1
        float bias = Mathf.Clamp(localX * 5.5f, -1f, 1f);
        float absBias = Mathf.Abs(bias);

        // 3. 베이스 힘 계산
        float baseForce = (weightFrictionScale + (deltaYaw * rotationInertiaScale * 0.01f)) * (speed * velocitySensitivity);

        // 4. 🔥 [핵심 수정] 비대칭 감쇄 로직
        // 무게가 쏠린 쪽(Strong)은 기존보다 더 강하게(1.0 -> 2.0)
        // 반대쪽(Weak)은 설정한 비율까지 대폭 감쇄(1.0 -> 0.2)
        float strongFactor = Mathf.Lerp(1.0f, 2.0f, absBias);
        float weakFactor = Mathf.Lerp(1.0f, weakSideRatio, absBias);

        float targetLeftAmp, targetRightAmp;

        if (bias < 0) // 무게가 왼쪽일 때
        {
            targetLeftAmp = baseForce * strongFactor;
            targetRightAmp = baseForce * weakFactor;
        }
        else // 무게가 오른쪽일 때
        {
            targetLeftAmp = baseForce * weakFactor;
            targetRightAmp = baseForce * strongFactor;
        }

        // 5. 최소/최대 제한 및 보간 (기존 동일)
        // 💡 팁: 반대쪽 진동이 여전히 세다면 minForceAmp를 0.02 정도로 낮추세요.
        _currentLeftAmp = Mathf.Lerp(_currentLeftAmp, Mathf.Clamp(targetLeftAmp, minForceAmp, maxForceAmp), dt * 10f);
        _currentRightAmp = Mathf.Lerp(_currentRightAmp, Mathf.Clamp(targetRightAmp, minForceAmp, maxForceAmp), dt * 10f);

        ApplyHaptics(_currentLeftAmp, _currentRightAmp);
    }

    private void ApplyHaptics(float l, float r)
    {
        OVRInput.SetControllerVibration(fixedFrequency, l, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(fixedFrequency, r, OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        _currentLeftAmp = 0; _currentRightAmp = 0;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}