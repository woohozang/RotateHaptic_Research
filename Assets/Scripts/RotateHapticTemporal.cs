using UnityEngine;

public class RotateHapticTemporal : MonoBehaviour
{
    /*public enum VelocitySource { Auto, Rigidbody, TransformDelta }
    public VelocitySource velocitySource = VelocitySource.Auto;

    public enum ProjectAxis { ForwardZ, RightX, Custom }
    public ProjectAxis projectAxis = ProjectAxis.ForwardZ;
    public Vector3 customAxis = Vector3.forward; // projectAxis=Custom일 때 사용
    */
    [Header("기준 프레임 (앞/뒤 투영용)")]
    [Tooltip("전후 속도/가속을 투영할 기준. 비워두면 이 오브젝트(또는 Rigidbody) 기준.")]
    public Transform motionFrame;

    [Header("기본 세팅")]
    [Range(0, 1)] public float strongAmp = 0.9f;
    [Range(0, 1)] public float weakAmp = 0.35f;
    [Range(0, 1)] public float baseFreq = 0.35f;
    public float globalGain = 1.4f;
    [Range(0, 0.3f)] public float minAmpWhileActive = 0.10f;
    public float smooth = 12f;

    [Header("상태 감지 임계값 (낮춰서 시작)")]
    public float speedThresholdUp = 0.10f;   // m/s  (Idle→Moving)
    public float speedThresholdDown = 0.06f; // m/s  (Moving→Idle)
    public float brakeAccelThreshold = 0.35f; // m/s^2 (브레이크 감지)

    [Header("펄스 커브/지속")]
    public AnimationCurve startPulse;
    public AnimationCurve brakePulse;
    public AnimationCurve movingAmpCurve;
    public float pulseDuration = 0.18f;

    [Header("왼/오 손 기여도(수평 속도)")]
    public float handSpeedDeadzone = 0.02f;
    public float bothHandsBoost = 1.25f;
    [Range(0, 1)] public float bothHandsSimilarity = 0.55f;

    [Header("디버그")]
    public bool logEveryFrame = true;
    public bool testPulseOnA = true; // A 버튼으로 0.25초 강제 진동

    private enum Phase { Idle, StartPulse, Moving, BrakePulse }
    private Phase _phase = Phase.Idle;
    private float _phaseT;
    private Rigidbody _rb;
    private Transform _refTf;     // motion frame 최종 참조
    private Vector3 _prevPos, _prevVel;
    private float _ampL, _ampR;

    public enum VelocitySource { Auto, Rigidbody, TransformDelta }
    public VelocitySource velocitySource = VelocitySource.Auto;

    public enum ProjectAxis { ForwardZ, RightX, Custom }
    public ProjectAxis projectAxis = ProjectAxis.ForwardZ;
    public Vector3 customAxis = Vector3.forward; // projectAxis=Custom일 때 사용

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>() ?? GetComponent<Rigidbody>();
    }

    void Start()
    {
        _refTf = motionFrame ? motionFrame : (_rb ? _rb.transform : transform);
        _prevPos = _refTf.position;

        // 기본 커브
        if (startPulse == null || startPulse.length == 0)
            startPulse = new AnimationCurve(
                new Keyframe(0.00f, 0.0f, 0, 10),
                new Keyframe(0.05f, 1.0f, -3, -3),
                new Keyframe(pulseDuration, 0.0f, -10, 0)
            );
        if (brakePulse == null || brakePulse.length == 0)
            brakePulse = new AnimationCurve(
                new Keyframe(0.00f, 0.0f, 0, 10),
                new Keyframe(0.04f, 1.0f, -3, -3),
                new Keyframe(pulseDuration, 0.0f, -10, 0)
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
        if (testPulseOnA && OVRInput.GetDown(OVRInput.Button.One))
        {
            OVRInput.SetControllerVibration(0.5f, 1f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0.5f, 1f, OVRInput.Controller.RTouch);
            Invoke(nameof(StopAll), 0.25f);
        }

        // 1) 카트 전후 속도/가속 (motionFrame 기준)
        // ---- 1) 카트 전후 속도/가속 (motionFrame 기준) ----
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Transform refTf = _refTf; // (Start에서 설정된 기준 프레임)

        Vector3 worldVel;

        // 속도 소스 자동 판별: 리짓보디가 없거나 isKinematic이거나, 속도가 아주 작으면 Transform 델타로 대체
        bool useRb =
            (velocitySource == VelocitySource.Rigidbody) ||
            (velocitySource == VelocitySource.Auto && _rb != null && !_rb.isKinematic);

        if (useRb)
        {
            worldVel = _rb.velocity;
            // 리짓보디 속도가 사실상 0이면 델타로 보정
            if (worldVel.sqrMagnitude < 1e-6f)
                worldVel = (refTf.position - _prevPos) / dt;
        }
        else
        {
            worldVel = (refTf.position - _prevPos) / dt;
        }

        // 기준 축 선택
        Vector3 axisW = refTf.forward;
        if (projectAxis == ProjectAxis.RightX) axisW = refTf.right;
        else if (projectAxis == ProjectAxis.Custom)
        {
            // 사용자가 입력한 축을 월드 기준으로 회전
            axisW = (refTf.rotation * customAxis).normalized;
        }
        axisW.Normalize();

        float vZ_prev = Vector3.Dot(_prevVel, axisW);
        float vZ = Vector3.Dot(worldVel, axisW);
        float aZ = (vZ - vZ_prev) / dt;

        _prevPos = refTf.position;
        _prevVel = worldVel;


        // 2) FSM 전이
        switch (_phase)
        {
            case Phase.Idle:
                if (Mathf.Abs(vZ) >= speedThresholdUp) { _phase = Phase.StartPulse; _phaseT = 0f; }
                break;
            case Phase.StartPulse:
                _phaseT += dt;
                if (aZ <= -brakeAccelThreshold) { _phase = Phase.BrakePulse; _phaseT = 0f; break; }
                if (_phaseT >= pulseDuration) { _phase = Phase.Moving; _phaseT = 0f; }
                break;
            case Phase.Moving:
                if (aZ <= -brakeAccelThreshold) { _phase = Phase.BrakePulse; _phaseT = 0f; }
                else if (Mathf.Abs(vZ) <= speedThresholdDown) { _phase = Phase.Idle; _phaseT = 0f; }
                break;
            case Phase.BrakePulse:
                _phaseT += dt;
                if (_phaseT >= pulseDuration)
                { _phase = (Mathf.Abs(vZ) <= speedThresholdDown) ? Phase.Idle : Phase.Moving; _phaseT = 0f; }
                break;
        }

        // 3) 단계별 기본 진폭/주파수
        float baseAmp = 0f;
        float freq = baseFreq;

        if (_phase == Phase.StartPulse)
            baseAmp = startPulse.Evaluate(Mathf.Clamp01(_phaseT / pulseDuration));
        else if (_phase == Phase.BrakePulse)
        {
            baseAmp = brakePulse.Evaluate(Mathf.Clamp01(_phaseT / pulseDuration));
            freq = Mathf.Clamp01(baseFreq + 0.15f);
        }
        else if (_phase == Phase.Moving)
        {
            float vNorm = Mathf.Clamp01(Mathf.Abs(vZ) / (speedThresholdUp * 2.0f));
            baseAmp = movingAmpCurve.Evaluate(vNorm);
        }

        // 전역 게인/최소
        baseAmp = Mathf.Clamp01(baseAmp * globalGain);
        if (_phase != Phase.Idle) baseAmp = Mathf.Max(baseAmp, minAmpWhileActive);

        // 4) 왼/오 손 기여도
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        lv.y = 0f; rv.y = 0f;
        float lS = lv.magnitude; if (lS < handSpeedDeadzone) lS = 0f;
        float rS = rv.magnitude; if (rS < handSpeedDeadzone) rS = 0f;

        float maxS = Mathf.Max(lS, rS);
        float minS = Mathf.Min(lS, rS);
        bool bothActive = (maxS > 0f) && (minS / (maxS + 1e-5f) >= bothHandsSimilarity);
        if (bothActive) baseAmp *= bothHandsBoost;

        bool leftDominant = lS >= rS;
        float s = Mathf.Clamp01(strongAmp * baseAmp);
        float w = Mathf.Clamp01(weakAmp * baseAmp);

        float tgtL = leftDominant ? s : w;
        float tgtR = leftDominant ? w : s;

        _ampL = Mathf.Lerp(_ampL, tgtL, smooth * Time.deltaTime);
        _ampR = Mathf.Lerp(_ampR, tgtR, smooth * Time.deltaTime);

        // 5) 출력
        if (_phase == Phase.Idle)
        {
            _ampL = Mathf.Lerp(_ampL, 0f, smooth * Time.deltaTime);
            _ampR = Mathf.Lerp(_ampR, 0f, smooth * Time.deltaTime);
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
        }
        else
        {
            OVRInput.SetControllerVibration(freq, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(freq, _ampR, OVRInput.Controller.RTouch);
        }

        if (logEveryFrame)
            Debug.Log($"[HapticTemporal] phase={_phase} vZ={vZ:F3} aZ={aZ:F3} lS={lS:F2} rS={rS:F2} outL={_ampL:F2} outR={_ampR:F2}");
    }

    void StopAll()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }

    void OnDisable() => StopAll();
}
