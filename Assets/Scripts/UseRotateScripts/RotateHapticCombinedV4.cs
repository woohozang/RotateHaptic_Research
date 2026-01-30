using UnityEngine;
using Oculus.Interaction;
using System.Linq;

public class RotateHapticCombinedV4 : MonoBehaviour
{
    [Header("기준 프레임")]
    public Transform motionFrame;

    [Header("기본 햅틱 설정 (비대칭)")]
    [Tooltip("주도하는 손: 묵직한 운동감")]
    [Range(0, 1)] public float strongAmp = 0.8f;
    [Range(0, 1)] public float strongFreq = 0.2f;

    [Tooltip("보조하는 손: 팽팽한 저항감")]
    [Range(0, 1)] public float weakAmp = 0.4f;
    [Range(0, 1)] public float weakFreq = 0.9f;

    [Header("글로벌 제어")]
    public float globalGain = 1.4f;
    public float minAmpWhileActive = 0.1f;
    public float smooth = 12f;

    [Header("상태 감지 임계값")]
    public float speedThresholdUp = 0.10f;
    public float speedThresholdDown = 0.06f;
    public float brakeAccelThreshold = 0.35f;

    [Header("물리 법칙 커브")]
    public AnimationCurve startPulse;
    public AnimationCurve yawStartPulse;
    public AnimationCurve brakePulse;
    public AnimationCurve movingAmpCurve;
    public float pulseDuration = 0.18f;

    [Header("양손 및 회전 감도")]
    public float handSpeedDeadzone = 0.02f;
    public float bothHandsBoost = 1.25f;
    [Range(0, 1)] public float bothHandsSimilarity = 0.55f;
    public float yawThresholdDeg = 0.2f;
    public float yawGain = 0.02f;

    private enum Phase { Idle, Start, Moving, Brake }
    private Phase _phase = Phase.Idle;
    private float _phaseT;

    private Transform _frame;
    private Rigidbody _rb;
    private Vector3 _prevPos, _prevVel;
    private float _prevYaw;
    private float _ampL, _ampR;
    private float _freqL, _freqR; // [추가] 각 손의 주파수 저장 변수

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

        UpdatePhase(speedAbs, aZ, yawAbs, dt);

        // 3. 커브 기반 베이스 진폭 계산
        float baseAmp = CalculateBaseAmplitude(speedAbs, yawAbs);

        // 4. 비대칭 햅틱 분배 (진폭과 주파수를 모두 다르게 설정)
        DistributeHaptics(baseAmp, yawAbs, dt);
    }

    private void UpdatePhase(float speedAbs, float aZ, float yawAbs, float dt)
    {
        switch (_phase)
        {
            case Phase.Idle:
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
            baseAmp = (yawAbs > speedAbs * 10f) ? yawStartPulse.Evaluate(t) : startPulse.Evaluate(t);
        else if (_phase == Phase.Brake)
            baseAmp = brakePulse.Evaluate(t);
        else if (_phase == Phase.Moving)
        {
            float normSpeed = Mathf.Clamp01(speedAbs / (speedThresholdUp * 2.0f));
            baseAmp = movingAmpCurve.Evaluate(normSpeed);
            if (yawAbs > yawThresholdDeg) baseAmp = Mathf.Max(baseAmp, yawAbs * yawGain);
        }

        return Mathf.Clamp01(baseAmp * globalGain);
    }

    private void DistributeHaptics(float baseAmp, float yawAbs, float dt)
    {
        if (_phase != Phase.Idle && baseAmp < minAmpWhileActive) baseAmp = minAmpWhileActive;

        // 1. 컨트롤러 입력 속도 획득
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        float lS = (lv.magnitude < handSpeedDeadzone) ? 0f : lv.magnitude;
        float rS = (rv.magnitude < handSpeedDeadzone) ? 0f : rv.magnitude;
        float maxHandSpeed = Mathf.Max(lS, rS);

        float handSpeedFactor = Mathf.Clamp01(maxHandSpeed / 1.5f);
        baseAmp *= (1.0f + handSpeedFactor);

        // 2. 주도권에 따른 진폭 및 주파수 결정
        float targetLAmp, targetRAmp, targetLFreq, targetRFreq;
        float speedRatio = (maxHandSpeed > 0.01f) ? Mathf.Min(lS, rS) / maxHandSpeed : 1f;

        // 제동 상태일 때 주파수 오프셋 (기존 로직 유지)
        float brakeFreqOffset = (_phase == Phase.Brake) ? 0.15f : 0f;

        if (speedRatio >= bothHandsSimilarity)
        {
            // 양손 협동 모드: 둘 다 주도적인 햅틱 적용
            targetLAmp = targetRAmp = strongAmp * baseAmp * bothHandsBoost;
            targetLFreq = targetRFreq = strongFreq + brakeFreqOffset;
        }
        else
        {
            // 비대칭 모드: 더 빠른 손이 Leader
            bool leftDominant = (lS > rS);

            targetLAmp = leftDominant ? strongAmp * baseAmp : weakAmp * baseAmp;
            targetRAmp = leftDominant ? weakAmp * baseAmp : strongAmp * baseAmp;

            targetLFreq = (leftDominant ? strongFreq : weakFreq) + brakeFreqOffset;
            targetRFreq = (leftDominant ? weakFreq : strongFreq) + brakeFreqOffset;
        }

        // 3. 부드러운 전이 적용 (Idle 시 정지 로직 포함)
        if (_phase == Phase.Idle && yawAbs <= yawThresholdDeg)
        {
            _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);
            _freqL = _freqR = 0f;
        }
        else
        {
            _ampL = Mathf.Lerp(_ampL, targetLAmp, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, targetRAmp, smooth * dt);
            _freqL = targetLFreq; // 주파수는 즉각적으로 변하는 것이 질감 표현에 유리
            _freqR = targetRFreq;
        }

        // 4. 최종 출력 (좌우 독립적 전달)
        OVRInput.SetControllerVibration(_freqL, _ampL, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(_freqR, _ampR, OVRInput.Controller.RTouch);
    }

    private void InitializeCurves()
    {
        if (startPulse == null || startPulse.length == 0)
            startPulse = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(pulseDuration, 1f, 0f, 0f));
        if (yawStartPulse == null || yawStartPulse.length == 0)
            yawStartPulse = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 0.8f), new Keyframe(pulseDuration, 0f));
        if (brakePulse == null || brakePulse.length == 0)
            brakePulse = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.04f, 1f), new Keyframe(pulseDuration, 0f));
        if (movingAmpCurve == null || movingAmpCurve.length == 0)
            movingAmpCurve = AnimationCurve.Linear(0f, 0.18f, 1f, 0.55f);
    }

    private void StopHaptics()
    {
        _ampL = _ampR = 0f;
        _freqL = _freqR = 0f;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.All);
    }

    private void OnDisable() => StopHaptics();
}