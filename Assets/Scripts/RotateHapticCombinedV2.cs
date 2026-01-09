using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// 전진/후진 및 제자리 회전에 물리 법칙(관성, 마찰력)을 적용한 햅틱 시뮬레이터
/// </summary>
public class RotateHapticCombinedV2 : MonoBehaviour
{
    [Header("기준 프레임")]
    public Transform motionFrame;

    [Header("기본 햅틱 설정")]
    [Range(0, 1)] public float strongAmp = 0.9f;   // 주도하는 손 (운동감)
    [Range(0, 1)] public float weakAmp = 0.35f;    // 버티는 손 (무게감)
    [Range(0, 1)] public float baseFreq = 0.35f;
    public float globalGain = 1.4f;
    public float minAmpWhileActive = 0.1f;
    public float smooth = 12f;

    [Header("상태 감지 임계값")]
    public float speedThresholdUp = 0.10f;
    public float speedThresholdDown = 0.06f;
    public float brakeAccelThreshold = 0.35f;

    [Header("물리 법칙 커브 (X:시간, Y:강도)")]
    public AnimationCurve startPulse;       // 출발 시 정지 마찰력
    public AnimationCurve yawStartPulse;    // 제자리 회전 시작 시 저항 (추가)
    public AnimationCurve brakePulse;       // 제동 시 충격량
    public AnimationCurve movingAmpCurve;   // 주행 중 속도 비례 진동
    public float pulseDuration = 0.18f;

    [Header("양손 및 회전 감도")]
    public float handSpeedDeadzone = 0.02f;
    public float bothHandsBoost = 1.25f;
    [Range(0, 1)] public float bothHandsSimilarity = 0.55f;
    public float yawThresholdDeg = 0.2f;    // 미세 회전 방지 데드존
    public float yawGain = 0.02f;           // 지속 회전 시 기본 진폭

    private enum Phase { Idle, Start, Moving, Brake }
    private Phase _phase = Phase.Idle;
    private float _phaseT;

    private Transform _frame;
    private Rigidbody _rb;
    private Vector3 _prevPos, _prevVel;
    private float _prevYaw;
    private float _ampL, _ampR;

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>() ?? GetComponent<Rigidbody>();
    }

    void Start()
    {
        _frame = motionFrame ? motionFrame : (_rb ? _rb.transform : transform);
        _prevPos = _frame.position;
        _prevYaw = _frame.eulerAngles.y;

        InitializeCurves();
    }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // 1. 물리 데이터 계산 (전진 속도 및 회전량)
        Vector3 vel = _rb && !_rb.isKinematic ? _rb.velocity : (_frame.position - _prevPos) / dt;
        Vector3 fwd = _frame.forward;
        float vZPrev = Vector3.Dot(_prevVel, fwd);
        float vZ = Vector3.Dot(vel, fwd);
        float aZ = (vZ - vZPrev) / dt;
        float speedAbs = Mathf.Abs(vZ);

        float yaw = _frame.eulerAngles.y;
        float deltaYaw = Mathf.DeltaAngle(_prevYaw, yaw);
        float yawAbs = Mathf.Abs(deltaYaw);

        _prevPos = _frame.position;
        _prevVel = vel;
        _prevYaw = yaw;

        // 2. 상태 머신 관리 (제자리 회전 조건 추가)
        UpdatePhase(speedAbs, aZ, yawAbs, dt);

        // 3. 커브 기반 베이스 진폭 계산 (Evaluation)
        float baseAmp = CalculateBaseAmplitude(speedAbs, yawAbs);
        float freq = (_phase == Phase.Brake) ? baseFreq + 0.15f : baseFreq;

        // 4. 비대칭 햅틱 분배 (Leading vs Anchor)
        DistributeHaptics(baseAmp, freq, yawAbs, dt);
    }

    private void UpdatePhase(float speedAbs, float aZ, float yawAbs, float dt)
    {
        switch (_phase)
        {
            case Phase.Idle:
                // 전진하거나 제자리에서 확 돌릴 때 Start 상태로 진입
                if (speedAbs >= speedThresholdUp || yawAbs >= yawThresholdDeg * 3f)
                {
                    _phase = Phase.Start;
                    _phaseT = 0f;
                }
                break;

            case Phase.Start:
                _phaseT += dt;
                if (aZ <= -brakeAccelThreshold) { _phase = Phase.Brake; _phaseT = 0f; }
                else if (_phaseT >= pulseDuration) { _phase = Phase.Moving; _phaseT = 0f; }
                break;

            case Phase.Moving:
                if (aZ <= -brakeAccelThreshold) { _phase = Phase.Brake; _phaseT = 0f; }
                else if (speedAbs <= speedThresholdDown && yawAbs <= yawThresholdDeg) { _phase = Phase.Idle; _phaseT = 0f; }
                break;

            case Phase.Brake:
                _phaseT += dt;
                if (_phaseT >= pulseDuration)
                {
                    _phase = (speedAbs <= speedThresholdDown) ? Phase.Idle : Phase.Moving;
                    _phaseT = 0f;
                }
                break;
        }
    }

    private float CalculateBaseAmplitude(float speedAbs, float yawAbs)
    {
        float baseAmp = 0f;
        float t = Mathf.Clamp01(_phaseT / pulseDuration);

        if (_phase == Phase.Start)
        {
            // 전진 시작이면 startPulse, 회전 위주면 yawStartPulse 사용
            baseAmp = (yawAbs > speedAbs * 10f) ? yawStartPulse.Evaluate(t) : startPulse.Evaluate(t);
        }
        else if (_phase == Phase.Brake)
        {
            baseAmp = brakePulse.Evaluate(t);
        }
        else if (_phase == Phase.Moving)
        {
            float normSpeed = Mathf.Clamp01(speedAbs / (speedThresholdUp * 2.0f));
            baseAmp = movingAmpCurve.Evaluate(normSpeed);
            // 제자리 회전 중이면 회전량에 따른 추가 진폭 반영
            if (yawAbs > yawThresholdDeg) baseAmp = Mathf.Max(baseAmp, yawAbs * yawGain);
        }

        return Mathf.Clamp01(baseAmp * globalGain);
    }

    private void DistributeHaptics(float baseAmp, float freq, float yawAbs, float dt)
    {
        if (_phase != Phase.Idle && baseAmp < minAmpWhileActive) baseAmp = minAmpWhileActive;

        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        float lS = (lv.magnitude < handSpeedDeadzone) ? 0f : lv.magnitude;
        float rS = (rv.magnitude < handSpeedDeadzone) ? 0f : rv.magnitude;

        // 양손 협동 보너스
        if (Mathf.Max(lS, rS) > 0f && (Mathf.Min(lS, rS) / (Mathf.Max(lS, rS) + 1e-5f) >= bothHandsSimilarity))
            baseAmp *= bothHandsBoost;

        // 주도 손 판별 (더 빨리 움직이는 손에 strongAmp 부여)
        bool leftDominant = (lS >= rS);

        float targetL = leftDominant ? strongAmp * baseAmp : weakAmp * baseAmp;
        float targetR = leftDominant ? weakAmp * baseAmp : strongAmp * baseAmp;

        if (_phase == Phase.Idle && yawAbs <= yawThresholdDeg)
        {
            _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);
        }
        else
        {
            _ampL = Mathf.Lerp(_ampL, targetL, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, targetR, smooth * dt);
        }

        OVRInput.SetControllerVibration(freq, _ampL, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(freq, _ampR, OVRInput.Controller.RTouch);
    }

    private void InitializeCurves()
    {
        // 1. 출발 펄스: EaseIn 효과를 위해 수동으로 Keyframe 정의
        if (startPulse == null || startPulse.length == 0)
        {
            // 0초에 0에서 시작하여 pulseDuration에 1이 되는 곡선
            startPulse = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2f), // 시작점 (시간, 값, inTangent, outTangent)
                new Keyframe(pulseDuration, 1f, 0f, 0f) // 끝점
            );
        }

        // 2. 제자리 회전 펄스
        if (yawStartPulse == null || yawStartPulse.length == 0)
        {
            yawStartPulse = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.05f, 0.8f),
                new Keyframe(pulseDuration, 0f)
            );
        }

        // 3. 제동 펄스
        if (brakePulse == null || brakePulse.length == 0)
        {
            brakePulse = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.04f, 1f),
                new Keyframe(pulseDuration, 0f)
            );
        }

        // 4. 주행 진폭 커브: 이건 유니티 표준인 Linear를 사용합니다.
        if (movingAmpCurve == null || movingAmpCurve.length == 0)
        {
            movingAmpCurve = AnimationCurve.Linear(0f, 0.18f, 1f, 0.55f);
        }
    }

    private void StopHaptics()
    {
        _ampL = _ampR = 0f;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }

    private void OnDisable() => StopHaptics();
}