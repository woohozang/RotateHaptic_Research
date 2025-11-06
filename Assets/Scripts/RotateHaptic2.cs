using UnityEngine;

public class RotateHaptic2 : MonoBehaviour
{
    [Header("기본 햅틱 세기")]
    [Range(0f, 1f)] public float strongHaptic = 0.8f;
    [Range(0f, 1f)] public float weakHaptic = 0.2f;

    [Header("주파수 (0~1)")]
    [Range(0f, 1f)] public float strongFrequency = 0.9f;
    [Range(0f, 1f)] public float weakFrequency = 0.25f;

    [Header("전역 게인 / 최소 세기")]
    [Tooltip("최종 진폭에 곱해지는 전체 배율")]
    public float globalGain = 1.3f;
    [Tooltip("회전 중 최소 진폭(너무 약하면 0.05~0.12 추천)")]
    [Range(0f, 0.3f)] public float minAmpWhileActive = 0.08f;

    [Header("민감도")]
    [Tooltip("회전 변화량(도/프레임)이 이 값보다 커야 작동")]
    public float rotationThreshold = 0.08f;
    [Tooltip("컨트롤러 속도 데드존 (m/s)")]
    public float speedDeadzone = 0.03f;
    [Tooltip("회전 변화량이 이 값일 때 100% 스케일")]
    public float rotationToMax = 18f;

    [Header("양손 시너지(동시에 힘줄 때)")]
    [Tooltip("양손이 모두 유효하게 움직일 때 강도 곱")]
    public float bothHandsBoost = 1.25f;
    [Tooltip("양손 속도의 작은쪽/큰쪽 비율이 이 값 이상이면 '동시에'로 간주 (0.0~1.0)")]
    [Range(0f, 1f)] public float bothHandsSimilarity = 0.55f;

    [Header("무게 옵션")]
    public bool isHeavy = false;
    public float heavyMultiplier = 1.6f;

    [Header("관성(앞/뒤 밀기) 피드백")]
    [Tooltip("전후 가속도 → 추가 진폭 스케일")]
    public float inertiaGain = 0.25f;
    [Tooltip("전후 가속도 데드존(m/s^2)")]
    public float inertiaDeadzone = 0.2f;
    [Tooltip("관성 강할수록 주파수 가산")]
    public float inertiaFreqBoost = 0.15f;

    [Header("스무딩")]
    [Tooltip("진폭 보간 속도")]
    public float smooth = 10f;

    // 내부 상태
    private float previousYRotation;
    private bool vibrating;
    private Vector3 _prevCartVel;
    private float _ampL, _ampR;

    // 전후 가속 계산용 (카트 리지드바디 있으면 사용)
    private Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>() ?? GetComponent<Rigidbody>();
    }

    void Start()
    {
        previousYRotation = transform.eulerAngles.y;
        _prevCartVel = Vector3.zero;
    }

    void Update()
    {
        // 1) 회전량(도/프레임)
        float yNow = transform.eulerAngles.y;
        float delta = yNow - previousYRotation;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;

        // 2) 컨트롤러 수평 속도(기여도)
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        lv.y = 0f; rv.y = 0f;
        float lSpeed = lv.magnitude;
        float rSpeed = rv.magnitude;
        if (lSpeed < speedDeadzone) lSpeed = 0f;
        if (rSpeed < speedDeadzone) rSpeed = 0f;

        // 3) 관성(전후 가속도) 계산: 카트 전역 속도를 로컬 Z로 투영해서 프레임 차분
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 cartVel = Vector3.zero;
        if (_rb != null) cartVel = _rb.velocity;
        float prevVz = Vector3.Dot(transform.InverseTransformDirection(_prevCartVel), Vector3.forward);
        float currVz = Vector3.Dot(transform.InverseTransformDirection(cartVel), Vector3.forward);
        float accelZ = (currVz - prevVz) / dt;  // +: 전진 가속, -: 후진 가속
        _prevCartVel = cartVel;

        // 4) 무게/전역 게인 적용한 기본 강/약
        float strong = strongHaptic;
        float weak = weakHaptic;
        if (isHeavy) { strong *= heavyMultiplier; weak *= heavyMultiplier; }
        strong = Mathf.Clamp01(strong * globalGain);
        weak = Mathf.Clamp01(weak * globalGain);

        // 5) 회전 활성 조건
        if (Mathf.Abs(delta) > rotationThreshold)
        {
            float rotScale = Mathf.Clamp01(Mathf.Abs(delta) / Mathf.Max(1e-3f, rotationToMax));

            // 양손 기여도 비교
            float maxS = Mathf.Max(lSpeed, rSpeed);
            float minS = Mathf.Min(lSpeed, rSpeed);
            bool bothActive = maxS > 0f && (minS / (maxS + 1e-5f)) >= bothHandsSimilarity;

            // 관성으로 인한 추가 세기 (데드존 이후 선형)
            float inertia = Mathf.Max(0f, Mathf.Abs(accelZ) - inertiaDeadzone) * inertiaGain;

            // 최종 강/약 스케일
            float strongScaled = Mathf.Max(minAmpWhileActive, strong * rotScale + inertia);
            float weakScaled = Mathf.Max(minAmpWhileActive * 0.6f, weak * rotScale + inertia * 0.6f);

            // 양손이 충분히 비슷하게 힘주면 시너지 부스트
            if (bothActive)
            {
                strongScaled *= bothHandsBoost;
                weakScaled *= Mathf.Lerp(1f, bothHandsBoost, 0.5f);
            }

            strongScaled = Mathf.Clamp01(strongScaled);
            weakScaled = Mathf.Clamp01(weakScaled);

            // 관성이 강하면 주파수 약간 업
            float freqStrong = Mathf.Clamp01(strongFrequency + Mathf.Sign(accelZ) * inertiaFreqBoost);
            float freqWeak = Mathf.Clamp01(weakFrequency + Mathf.Sign(accelZ) * (inertiaFreqBoost * 0.5f));

            // 어느 손이 더 기여?
            bool leftDominant = lSpeed >= rSpeed;

            // 스무딩
            float tgtL = leftDominant ? strongScaled : weakScaled;
            float tgtR = leftDominant ? weakScaled : strongScaled;
            _ampL = Mathf.Lerp(_ampL, tgtL, smooth * Time.deltaTime);
            _ampR = Mathf.Lerp(_ampR, tgtR, smooth * Time.deltaTime);

            OVRInput.SetControllerVibration(freqWeak, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(freqStrong, _ampR, OVRInput.Controller.RTouch);
            if (leftDominant)
            {
                OVRInput.SetControllerVibration(freqStrong, _ampL, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(freqWeak, _ampR, OVRInput.Controller.RTouch);
            }

            vibrating = true;
        }
        else
        {
            if (vibrating)
            {
                _ampL = Mathf.Lerp(_ampL, 0f, smooth * Time.deltaTime);
                _ampR = Mathf.Lerp(_ampR, 0f, smooth * Time.deltaTime);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
                vibrating = false;
            }
        }

        previousYRotation = yNow;
    }

    void OnDisable()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}
