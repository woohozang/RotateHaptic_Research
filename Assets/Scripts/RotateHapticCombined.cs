using UnityEngine;

public class RotateHapticCombined : MonoBehaviour
{
    [Header("기준 프레임 (앞/뒤 투영용)")]
    public Transform motionFrame; // 비우면 cart 자체 transform 기준

    [Header("기본 세팅")]
    [Range(0, 1)] public float strongAmp = 0.9f;
    [Range(0, 1)] public float weakAmp = 0.35f;
    [Range(0, 1)] public float baseFreq = 0.35f;
    public float globalGain = 1.4f;
    public float minAmpWhileActive = 0.1f;
    public float smooth = 12f;

    [Header("상태 감지 임계값")]
    public float speedThresholdUp = 0.10f;
    public float speedThresholdDown = 0.06f;
    public float brakeAccelThreshold = 0.35f;

    [Header("펄스 커브 / 지속시간")]
    public AnimationCurve startPulse;
    public AnimationCurve brakePulse;
    public AnimationCurve movingAmpCurve;
    public float pulseDuration = 0.18f;

    [Header("양손 기여 (수평 속도)")]
    public float handSpeedDeadzone = 0.02f;
    public float bothHandsBoost = 1.25f;
    [Range(0, 1)] public float bothHandsSimilarity = 0.55f;

    [Header("Y축 회전 기여(제자리 토크)")]
    [Tooltip("이 값(도) 이상 회전했을 때만 회전 기여 사용")]
    public float yawThresholdDeg = 0.2f;
    [Tooltip("회전량(도) * 이 값 = 최소 추가 진폭")]
    public float yawGain = 0.02f;

    [Header("디버그")]
    public bool logEveryFrame = false;
    public bool testPulseOnA = false;

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

        // 기본 커브 세팅 (비어 있으면 자동 생성)
        if (startPulse == null || startPulse.length == 0)
            startPulse = new AnimationCurve(
                new Keyframe(0.00f, 0.0f),
                new Keyframe(0.05f, 1.0f),
                new Keyframe(pulseDuration, 0.0f)
            );
        if (brakePulse == null || brakePulse.length == 0)
            brakePulse = new AnimationCurve(
                new Keyframe(0.00f, 0.0f),
                new Keyframe(0.04f, 1.0f),
                new Keyframe(pulseDuration, 0.0f)
            );
        if (movingAmpCurve == null || movingAmpCurve.length == 0)
            movingAmpCurve = new AnimationCurve(
                new Keyframe(0.00f, 0.18f),
                new Keyframe(0.35f, 0.35f),
                new Keyframe(1.00f, 0.55f)
            );
    }

    void Update()
    {
        // A 버튼 테스트용
        if (testPulseOnA && OVRInput.GetDown(OVRInput.Button.One))
        {
            OVRInput.SetControllerVibration(0.5f, 1f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0.5f, 1f, OVRInput.Controller.RTouch);
            Invoke(nameof(StopHaptics), 0.25f);
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // ---------- 1) 전/후 속도 & 가속 ----------
        Vector3 vel = _rb && !_rb.isKinematic
            ? _rb.velocity
            : (_frame.position - _prevPos) / dt;

        Vector3 fwd = _frame.forward;
        float vZPrev = Vector3.Dot(_prevVel, fwd);
        float vZ = Vector3.Dot(vel, fwd);
        float aZ = (vZ - vZPrev) / dt;
        float speedAbs = Mathf.Abs(vZ);

        _prevPos = _frame.position;
        _prevVel = vel;

        // ---------- 2) Yaw(회전량) ----------
        float yaw = _frame.eulerAngles.y;
        float deltaYaw = yaw - _prevYaw;
        if (deltaYaw > 180f) deltaYaw -= 360f;
        if (deltaYaw < -180f) deltaYaw += 360f;
        float yawAbs = Mathf.Abs(deltaYaw);
        _prevYaw = yaw;

        // ---------- 3) 상태 전이 (Idle/Start/Moving/Brake) ----------
        switch (_phase)
        {
            case Phase.Idle:
                if (speedAbs >= speedThresholdUp)
                {
                    _phase = Phase.Start;
                    _phaseT = 0f;
                }
                break;

            case Phase.Start:
                _phaseT += dt;
                if (aZ <= -brakeAccelThreshold)
                {
                    _phase = Phase.Brake;
                    _phaseT = 0f;
                }
                else if (_phaseT >= pulseDuration)
                {
                    _phase = Phase.Moving;
                    _phaseT = 0f;
                }
                break;

            case Phase.Moving:
                if (aZ <= -brakeAccelThreshold)
                {
                    _phase = Phase.Brake;
                    _phaseT = 0f;
                }
                else if (speedAbs <= speedThresholdDown)
                {
                    _phase = Phase.Idle;
                    _phaseT = 0f;
                }
                break;

            case Phase.Brake:
                _phaseT += dt;
                if (_phaseT >= pulseDuration)
                {
                    _phase = speedAbs <= speedThresholdDown ? Phase.Idle : Phase.Moving;
                    _phaseT = 0f;
                }
                break;
        }

        // ---------- 4) 기본 Amp (번역 + 관성) ----------
        float baseAmp = 0f;
        float freq = baseFreq;

        if (_phase == Phase.Start)
        {
            baseAmp = startPulse.Evaluate(Mathf.Clamp01(_phaseT / pulseDuration));
        }
        else if (_phase == Phase.Brake)
        {
            baseAmp = brakePulse.Evaluate(Mathf.Clamp01(_phaseT / pulseDuration));
            freq = Mathf.Clamp01(baseFreq + 0.15f); // 브레이크일 때 조금 더 높은 주파수
        }
        else if (_phase == Phase.Moving)
        {
            float normSpeed = Mathf.Clamp01(speedAbs / (speedThresholdUp * 2.0f));
            baseAmp = movingAmpCurve.Evaluate(normSpeed);
        }

        // ---------- 5) Yaw 기반 추가 세기 (제자리 회전도 느껴지게) ----------
        if (yawAbs > yawThresholdDeg)
        {
            float yawFactor = Mathf.Clamp01(yawAbs * yawGain);
            // 이동이 거의 없어도 회전만으로 최소한의 진동이 나오도록 보완
            baseAmp = Mathf.Max(baseAmp, yawFactor);
        }

        // ---------- 6) 전역 게인 / 최소값 ----------
        baseAmp = Mathf.Clamp01(baseAmp * globalGain);
        if (_phase != Phase.Idle && baseAmp < minAmpWhileActive)
            baseAmp = minAmpWhileActive;

        // ---------- 7) 컨트롤러 속도 → 어느 손이 더 "힘 쓰는지" ----------
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        lv.y = 0f;
        rv.y = 0f;

        float lS = lv.magnitude;
        float rS = rv.magnitude;
        if (lS < handSpeedDeadzone) lS = 0f;
        if (rS < handSpeedDeadzone) rS = 0f;

        // 양손이 비슷한 속도로 움직이면 boost
        float maxS = Mathf.Max(lS, rS);
        float minS = Mathf.Min(lS, rS);
        bool bothActive = maxS > 0f && (minS / (maxS + 1e-5f) >= bothHandsSimilarity);
        if (bothActive) baseAmp *= bothHandsBoost;

        // ★ 핵심: 어느 손이 주도하는지 결정
        bool leftDominant;

        if (yawAbs > yawThresholdDeg && (lS > 0f || rS > 0f))
        {
            // 회전 중일 때는 '더 많이 움직이는 손' = 주도 손
            // → 왼손으로 돌리면 왼손 속도가 더 크니까 왼손이 strong
            leftDominant = (lS >= rS);
        }
        else
        {
            // 회전이 거의 없을 땐 그냥 더 빠른 손에 강하게
            leftDominant = (lS >= rS);
        }

        // ---------- 8) 좌/우 진폭 계산 ----------
        float s = Mathf.Clamp01(strongAmp * baseAmp);
        float w = Mathf.Clamp01(weakAmp * baseAmp);

        float targetL = leftDominant ? s : w;
        float targetR = leftDominant ? w : s;

        // ---------- 9) 스무딩 & 출력 ----------
        if (_phase == Phase.Idle && yawAbs <= yawThresholdDeg)
        {
            _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
        }
        else
        {
            _ampL = Mathf.Lerp(_ampL, targetL, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, targetR, smooth * dt);
            OVRInput.SetControllerVibration(freq, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(freq, _ampR, OVRInput.Controller.RTouch);
        }

        if (logEveryFrame)
        {
            Debug.Log(
                $"[Temporal] phase={_phase} vZ={vZ:F3} aZ={aZ:F3} yaw={deltaYaw:F2} " +
                $"lS={lS:F2} rS={rS:F2} L={_ampL:F2} R={_ampR:F2}"
            );
        }
    }

    void StopHaptics()
    {
        _ampL = _ampR = 0f;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }

    void OnDisable() => StopHaptics();
}
