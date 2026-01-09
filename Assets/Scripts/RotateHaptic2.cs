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
    [Tooltip("회전 중 최소 진폭")]
    [Range(0f, 0.3f)] public float minAmpWhileActive = 0.12f;

    [Header("민감도")]
    [Tooltip("회전 변화량(도/프레임) 데드존")]
    public float rotationThreshold = 0.05f;
    [Tooltip("컨트롤러 속도 데드존 (m/s)")]
    public float speedDeadzone = 0.03f;
    [Tooltip("최대 진폭에 도달하는 회전 속도 (도/초). 30~60 추천")]
    public float rotationToMaxSpeed = 45f;

    [Header("양손 시너지")]
    public float bothHandsBoost = 1.25f;
    [Range(0f, 1f)] public float bothHandsSimilarity = 0.55f;

    [Header("무게 옵션")]
    public bool isHeavy = false;
    public float heavyMultiplier = 1.6f;

    [Header("관성(앞/뒤 밀기) 피드백")]
    public float inertiaGain = 0.4f;
    public float inertiaDeadzone = 0.15f;
    public float inertiaFreqBoost = 0.15f;

    [Header("스무딩")]
    public float smooth = 12f;

    private float previousYRotation;
    private bool vibrating;
    private Vector3 _prevCartVel;
    private float _ampL, _ampR;
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
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // 1) 회전 데이터 계산 (초당 회전 속도 산출)
        float yNow = transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(previousYRotation, yNow);
        float angularSpeed = delta / dt; // 초당 몇 도 도는가?

        // 2) 컨트롤러 속도
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        lv.y = 0f; rv.y = 0f;
        float lSpeed = (lv.magnitude < speedDeadzone) ? 0f : lv.magnitude;
        float rSpeed = (rv.magnitude < speedDeadzone) ? 0f : rv.magnitude;

        // 3) 전후 가속도(관성) 계산
        Vector3 cartVel = _rb != null ? _rb.velocity : Vector3.zero;
        float prevVz = Vector3.Dot(transform.InverseTransformDirection(_prevCartVel), Vector3.forward);
        float currVz = Vector3.Dot(transform.InverseTransformDirection(cartVel), Vector3.forward);
        float accelZ = (currVz - prevVz) / dt;
        _prevCartVel = cartVel;

        // 4) 회전 활성 조건 (데드존 체크)
        if (Mathf.Abs(delta) > rotationThreshold || angularSpeed > 2.0f)
        {
            // [수정] 초당 회전 속도를 기준으로 스케일링
            float rotScale = Mathf.Clamp01(angularSpeed / rotationToMaxSpeed);

            // 무게 및 전역 게인 적용
            float baseStrong = strongHaptic * (isHeavy ? heavyMultiplier : 1f) * globalGain;
            float baseWeak = weakHaptic * (isHeavy ? heavyMultiplier : 1f) * globalGain;

            // 관성(가속도) 기여분 계산
            float inertiaEffort = Mathf.Max(0f, Mathf.Abs(accelZ) - inertiaDeadzone) * inertiaGain;

            // 최종 강/약 진폭 결정
            float targetStrong = Mathf.Max(minAmpWhileActive, (baseStrong * rotScale) + inertiaEffort);
            float targetWeak = Mathf.Max(minAmpWhileActive * 0.5f, (baseWeak * rotScale) + (inertiaEffort * 0.5f));

            // 양손 시너지 부스트
            float maxS = Mathf.Max(lSpeed, rSpeed);
            float minS = Mathf.Min(lSpeed, rSpeed);
            if (maxS > 0.1f && (minS / maxS) >= bothHandsSimilarity)
            {
                targetStrong *= bothHandsBoost;
                targetWeak *= bothHandsBoost;
            }

            // 5) 주도 손 판별 및 출력값 결정
            bool leftDominant = lSpeed >= rSpeed;
            float finalAmpL = Mathf.Clamp01(leftDominant ? targetStrong : targetWeak);
            float finalAmpR = Mathf.Clamp01(leftDominant ? targetWeak : targetStrong);

            // 주파수 결정 (가속 시 주파수 증가)
            float finalFreqL = Mathf.Clamp01((leftDominant ? strongFrequency : weakFrequency) + (Mathf.Abs(accelZ) * inertiaFreqBoost));
            float finalFreqR = Mathf.Clamp01((leftDominant ? weakFrequency : strongFrequency) + (Mathf.Abs(accelZ) * inertiaFreqBoost));

            // 스무딩 처리
            _ampL = Mathf.Lerp(_ampL, finalAmpL, smooth * dt);
            _ampR = Mathf.Lerp(_ampR, finalAmpR, smooth * dt);

            // 햅틱 출력
            OVRInput.SetControllerVibration(finalFreqL, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(finalFreqR, _ampR, OVRInput.Controller.RTouch);

            // [데이터 확인용 로그]
            Debug.Log($"<color=#00FF00>[Active]</color> AngularSpeed: {angularSpeed:F1}°/s | L-Hand: Amp({_ampL:F2}), Freq({finalFreqL:F2}) | R-Hand: Amp({_ampR:F2}), Freq({finalFreqR:F2})");

            vibrating = true;
        }
        else
        {
            if (vibrating)
            {
                _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
                _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);

                if (_ampL < 0.01f)
                {
                    Debug.Log("<color=#FF0000>[Stopped]</color>");
                    vibrating = false;
                }
            }
        }

        previousYRotation = yNow;
    }

    void OnDisable() => OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
}