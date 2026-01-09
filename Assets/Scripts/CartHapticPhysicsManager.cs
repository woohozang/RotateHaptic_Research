using UnityEngine;
using Oculus.Interaction;

public class CartHapticPhysicsManager : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Rigidbody cartRb;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Physics Effort Curve")]
    public AnimationCurve effortCurve = new AnimationCurve(
        new Keyframe(0f, 0.6f),   // 시작 저항
        new Keyframe(0.2f, 0.2f),  // 관성 구간
        new Keyframe(1f, 1f)      // 고속/제동
    );

    [Header("Asymmetric Haptic Settings")]
    public float leadingIntensity = 0.5f;
    public float anchorIntensity = 0.2f;
    [Range(0.01f, 0.2f)] public float granularFrequency = 0.08f;

    [Header("Sensitivity & Deadzone")]
    public float movementDeadzone = 0.02f; // 더 낮게 조정
    public float hapticSmoothSpeed = 8f;

    private float _granularTimer = 0f;
    private Vector3 _lastPosition;
    private float _currentHapticIntensity = 0f;

    void Start()
    {
        _lastPosition = cartRb.position;
    }

    void Update()
    {
        bool isGrabbing = grabbable.SelectingPoints.Count > 0;

        if (isGrabbing)
        {
            HandleAsymmetricHaptics();
        }
        else
        {
            StopAllHaptics();
        }

        _lastPosition = cartRb.position;
    }

    void HandleAsymmetricHaptics()
    {
        // 1. 의사 속도(Pseudo-velocity) 계산 (Kinematic 대응)
        float dt = Time.deltaTime > 0 ? Time.deltaTime : 0.01f;
        float currentSpeed = (cartRb.position - _lastPosition).magnitude / dt;

        // 2. 데드존 체크
        if (currentSpeed < movementDeadzone)
        {
            _currentHapticIntensity = Mathf.Lerp(_currentHapticIntensity, 0f, dt * hapticSmoothSpeed);
        }
        else
        {
            // 3. 물리 법칙 커브 적용
            float normalizedSpeed = Mathf.Clamp01(currentSpeed / 3.0f);
            float effortMultiplier = effortCurve.Evaluate(normalizedSpeed);

            float targetIntensity = Mathf.Clamp01((currentSpeed - movementDeadzone) / 0.5f);
            _currentHapticIntensity = Mathf.Lerp(_currentHapticIntensity, targetIntensity * effortMultiplier, dt * hapticSmoothSpeed);
        }

        if (_currentHapticIntensity <= 0.01f)
        {
            StopAllHaptics();
            return;
        }

        // 4. 주도 손 판별 (단순화된 속도 비교 방식)
        Vector3 vL = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 vR = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

        OVRInput.Controller leader = (vL.magnitude > vR.magnitude) ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        OVRInput.Controller anchor = (leader == OVRInput.Controller.LTouch) ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

        ApplyCustomHaptics(leader, anchor, _currentHapticIntensity);
    }

    void ApplyCustomHaptics(OVRInput.Controller leader, OVRInput.Controller anchor, float finalAmp)
    {
        // 주도하는 손: 뚜두둑 (펄스)
        _granularTimer += Time.deltaTime;
        if (_granularTimer >= granularFrequency)
        {
            // OVRInput은 1프레임만 진동시키는 게 아니라 상태를 유지하므로, 
            // 펄스 직후에 바로 끄지 말고 다음 타이머까지 기다려야 합니다.
            OVRInput.SetControllerVibration(0.8f, leadingIntensity * finalAmp, leader);
            _granularTimer = 0f;
        }
        // else에서 0으로 만들지 않습니다! (진동이 툭툭 끊기는 원인)

        // 저항하는 손: 지이잉 (지속)
        OVRInput.SetControllerVibration(0.1f, anchorIntensity * finalAmp, anchor);
    }

    void StopAllHaptics()
    {
        _currentHapticIntensity = 0f;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}