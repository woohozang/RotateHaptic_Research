using UnityEngine;
using Oculus.Interaction;
using System.Linq; // SelectingPoints.Any() 사용을 위해 필수

/// <summary>
/// 6단계 상태 머신 + 컨트롤러 각속도 + 정지 떨림 방지가 통합된 최종 햅틱 스크립트
/// </summary>
public class RotateHapticCombinedV8 : MonoBehaviour
{
    public enum CartState { Idle, PushStart, Pushing, PushBrake, RotateStart, Rotating, RotateBrake }

    [Header("인터랙션 설정")]
    [SerializeField] private Grabbable _grabbable;
    public CartState currentState = CartState.Idle;

    [Header("1. 전진/후진 (Push) 커브")]
    public AnimationCurve pushStartCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f));
    public AnimationCurve pushingCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 0.6f);
    public AnimationCurve pushBrakeCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 1f), new Keyframe(0.2f, 0f));

    [Header("2. 회전 (Rotate) 커브")]
    public AnimationCurve rotateStartCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f));
    public AnimationCurve rotatingCurve = AnimationCurve.Linear(0f, 0.3f, 1f, 0.9f);
    public AnimationCurve rotateBrakeCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 1f), new Keyframe(0.2f, 0f));

    [Header("햅틱 세기 및 주파수")]
    [Range(0, 1)] public float strongAmpMult = 0.85f;  // 주도 손
    [Range(0, 1)] public float weakAmpMult = 0.25f;    // 보조 손
    [Range(0, 1)] public float hapticFrequency = 0.7f;
    public float globalGain = 1.3f;
    public float smooth = 15f;

    [Header("임계값 (정지 떨림 방지)")]
    public float speedThreshold = 0.08f;       // 전진 감지 최소 속도 (m/s)
    public float rotateThreshold = 4.0f;      // 회전 감지 최소 각속도 (deg/s)
    public float brakeThreshold = 0.4f;       // 제동 감지 감속도
    public float hapticActivationThreshold = 0.12f; // 최종 진폭이 이 값보다 낮으면 진동 차단

    [Header("양손 협동 (Synergy)")]
    public float bothHandsBoost = 1.25f;
    [Range(0f, 1f)] public float bimanualSimilarity = 0.55f;

    private Rigidbody _rb;
    private float _phaseT = 0f;
    private float _prevSpeed, _prevAngularSpeed;
    private float _ampL, _ampR;
    private float _prevYRotation;
    private bool _isVibrating;

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>() ?? GetComponent<Rigidbody>();
        if (_grabbable == null) _grabbable = GetComponent<Grabbable>();
    }

    void Start()
    {
        _prevYRotation = transform.eulerAngles.y;
    }

    void Update()
    {
        // 1. 잡기 상태 체크 (누군가 잡고 있는가?)
        if (_grabbable == null || !_grabbable.SelectingPoints.Any())
        {
            if (_isVibrating) StopHaptics();
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        _phaseT += dt;

        // 2. 물리 데이터 계산 (카트 기준)
        Vector3 localVel = transform.InverseTransformDirection(_rb ? _rb.velocity : Vector3.zero);
        float currentSpeed = localVel.z;
        float accelZ = (currentSpeed - _prevSpeed) / dt;

        float yNow = transform.eulerAngles.y;
        float cartAngSpeed = Mathf.DeltaAngle(_prevYRotation, yNow) / dt;
        float cartAngAccel = (Mathf.Abs(cartAngSpeed) - Mathf.Abs(_prevAngularSpeed)) / dt;

        // 3. 컨트롤러 데이터 계산 (사용자 입력 의도)
        Vector3 lAngVelV = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.LTouch);
        Vector3 rAngVelV = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
        float lAngSpeed = lAngVelV.magnitude * Mathf.Rad2Deg;
        float rAngSpeed = rAngVelV.magnitude * Mathf.Rad2Deg;
        float maxControllerAngSpeed = Mathf.Max(lAngSpeed, rAngSpeed);

        float lS = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch).magnitude;
        float rS = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch).magnitude;

        // 4. 상태 머신 관리
        UpdateCartState(currentSpeed, accelZ, cartAngSpeed, cartAngAccel);

        // 5. 커브 기반 베이스 진폭 계산 + 컨트롤러 속도 가중치 적용
        float baseAmp = CalculateCurveAmplitude(Mathf.Abs(currentSpeed), Mathf.Abs(cartAngSpeed));

        // 사용자가 컨트롤러를 빨리 돌릴수록(입력 의도가 강할수록) 진폭 강화
        float inputWeight = Mathf.Clamp01(maxControllerAngSpeed / 60f);
        float finalBaseAmp = baseAmp * (0.5f + inputWeight * 0.5f) * globalGain;

        // 6. 햅틱 출력 및 떨림 방지 가드
        if (finalBaseAmp > hapticActivationThreshold)
        {
            DistributeBimanualHaptics(finalBaseAmp, lAngSpeed, rAngSpeed, lS, rS, dt);
            _isVibrating = true;
        }
        else
        {
            StopSmoothly(dt);
        }

        // 데이터 백업
        _prevSpeed = currentSpeed;
        _prevAngularSpeed = cartAngSpeed;
        _prevYRotation = yNow;
    }

    private void UpdateCartState(float speed, float accel, float angSpeed, float angAccel)
    {
        float absS = Mathf.Abs(speed);
        float absAS = Mathf.Abs(angSpeed);
        CartState next = currentState;

        // 제동(Brake) 우선 순위
        if (absS > speedThreshold && accel < -brakeThreshold) next = CartState.PushBrake;
        else if (absAS > rotateThreshold && angAccel < -brakeThreshold * 5f) next = CartState.RotateBrake;
        // 회전(Rotate) 상태
        else if (absAS > rotateThreshold)
        {
            if (currentState != CartState.Rotating && currentState != CartState.RotateStart) next = CartState.RotateStart;
            else if (_phaseT > 0.2f) next = CartState.Rotating;
        }
        // 전진(Push) 상태
        else if (absS > speedThreshold)
        {
            if (currentState != CartState.Pushing && currentState != CartState.PushStart) next = CartState.PushStart;
            else if (_phaseT > 0.2f) next = CartState.Pushing;
        }
        else next = CartState.Idle;

        if (next != currentState) { currentState = next; _phaseT = 0; }
    }

    private float CalculateCurveAmplitude(float speed, float angSpeed)
    {
        float t = Mathf.Clamp01(_phaseT);
        switch (currentState)
        {
            case CartState.PushStart: return pushStartCurve.Evaluate(t);
            case CartState.Pushing: return pushingCurve.Evaluate(speed / 1.5f);
            case CartState.PushBrake: return pushBrakeCurve.Evaluate(t);
            case CartState.RotateStart: return rotateStartCurve.Evaluate(t);
            case CartState.Rotating: return rotatingCurve.Evaluate(angSpeed / 45f);
            case CartState.RotateBrake: return rotateBrakeCurve.Evaluate(t);
            default: return 0;
        }
    }

    private void DistributeBimanualHaptics(float baseAmp, float lAS, float rAS, float lS, float rS, float dt)
    {
        float maxAS = Mathf.Max(lAS, rAS);
        float similarity = (maxAS > 10f) ? (Mathf.Min(lAS, rAS) / maxAS) : 1f;

        float targetL, targetR;

        // 양손 협동 모드 (비슷한 속도로 움직일 때)
        if (maxAS > 20f && similarity >= bimanualSimilarity)
        {
            targetL = targetR = baseAmp * bothHandsBoost;
        }
        else
        {
            // 주도 손 판별 (각속도 + 선속도 종합)
            bool leftDominant = (lAS + lS * 10f) > (rAS + rS * 10f);
            targetL = (leftDominant ? strongAmpMult : weakAmpMult) * baseAmp;
            targetR = (leftDominant ? weakAmpMult : strongAmpMult) * baseAmp;
        }

        _ampL = Mathf.Lerp(_ampL, Mathf.Clamp01(targetL), smooth * dt);
        _ampR = Mathf.Lerp(_ampR, Mathf.Clamp01(targetR), smooth * dt);

        OVRInput.SetControllerVibration(hapticFrequency, _ampL, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(hapticFrequency, _ampR, OVRInput.Controller.RTouch);
    }

    private void StopSmoothly(float dt)
    {
        _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
        _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);
        OVRInput.SetControllerVibration(hapticFrequency, _ampL, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(hapticFrequency, _ampR, OVRInput.Controller.RTouch);
        if (_ampL < 0.01f) _isVibrating = false;
    }

    private void StopHaptics()
    {
        _ampL = _ampR = 0;
        currentState = CartState.Idle;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.All);
        _isVibrating = false;
    }

    void OnDisable() => StopHaptics();
}