using UnityEngine;
using Oculus.Interaction;
using System.Linq;

public class RotateHaptic2 : MonoBehaviour
{
    [Header("인터랙션 설정")]
    [SerializeField] private Grabbable _grabbable;

    [Header("진동 감도 (정지 떨림 방지)")]
    [Tooltip("손떨림을 무시할 최소 초당 회전 각도 (추천: 2.0~5.0)")]
    public float angularSpeedDeadzone = 3.0f;
    [Tooltip("진동이 시작되는 최소 임계값 (0.1 이하 추천)")]
    public float hapticActivationThreshold = 0.15f;
    [Tooltip("값이 작을수록 정지 상태가 정적임 (1.0~3.0)")]
    public float inputCurveExponent = 2.0f;

    [Header("기본 햅틱 세기")]
    [Range(0f, 1f)] public float strongHaptic = 0.8f;
    [Range(0f, 1f)] public float weakHaptic = 0.2f;
    public float globalGain = 1.3f;

    [Header("주파수 및 스무딩")]
    [Range(0f, 1f)] public float strongFrequency = 0.8f;
    public float smooth = 15f;
    public float angularAccelGain = 0.04f;

    private float _prevYRotation;
    private float _prevAngularSpeed;
    private float _ampL, _ampR;
    private bool _vibrating;

    void Awake() => _grabbable = _grabbable ?? GetComponent<Grabbable>();

    void Update()
    {
        // 1. 잡기 상태 체크
        if (_grabbable == null || !_grabbable.SelectingPoints.Any())
        {
            if (_vibrating) StopHaptics();
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // 2. 물리 데이터 계산 및 노이즈 필터링
        float yNow = transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(_prevYRotation, yNow);
        float rawAngularSpeed = Mathf.Abs(delta / dt);

        // [필터 1] 초당 회전 속도가 데드존 이하면 0으로 간주
        float filteredSpeed = (rawAngularSpeed < angularSpeedDeadzone) ? 0f : rawAngularSpeed;

        float rawAccel = Mathf.Abs(filteredSpeed - _prevAngularSpeed) / dt;
        _prevAngularSpeed = filteredSpeed;

        // 3. 컨트롤러 실제 이동 속도 (손의 의도 확인)
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        float maxHandSpeed = Mathf.Max(lv.magnitude, rv.magnitude);

        // 4. 햅틱 강도 계산
        float speedFactor = Mathf.Clamp01(filteredSpeed / 45f);
        float accelFactor = Mathf.Clamp01(rawAccel * angularAccelGain);

        // [필터 2] 지수적 스케일링 (작은 값은 더 작게, 큰 값은 확실하게)
        // 예: 0.1의 입력을 2제곱하면 0.01이 되어 거의 느껴지지 않음
        float combinedFactor = Mathf.Max(speedFactor, accelFactor);
        combinedFactor = Mathf.Pow(combinedFactor, inputCurveExponent);

        // 5. 최종 활성화 조건
        // 일정 수준(hapticActivationThreshold) 이상의 '의도'가 보일 때만 햅틱 작동
        if (combinedFactor > hapticActivationThreshold || maxHandSpeed > 0.15f)
        {
            float finalAmpBase = combinedFactor * globalGain;

            // 주도 손 판별 (손의 속도 기반)
            bool leftLeading = lv.magnitude >= rv.magnitude;
            float targetL = (leftLeading ? strongHaptic : weakHaptic) * finalAmpBase;
            float targetR = (leftLeading ? weakHaptic : strongHaptic) * finalAmpBase;

            _ampL = Mathf.Lerp(_ampL, Mathf.Clamp01(targetL), smooth * dt);
            _ampR = Mathf.Lerp(_ampR, Mathf.Clamp01(targetR), smooth * dt);

            OVRInput.SetControllerVibration(strongFrequency, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(strongFrequency, _ampR, OVRInput.Controller.RTouch);
            _vibrating = true;
        }
        else
        {
            StopSmoothly(dt);
        }

        _prevYRotation = yNow;
    }

    private void StopSmoothly(float dt)
    {
        _ampL = Mathf.Lerp(_ampL, 0f, smooth * dt);
        _ampR = Mathf.Lerp(_ampR, 0f, smooth * dt);

        if (_ampL < 0.01f && _ampR < 0.01f)
        {
            StopHaptics();
        }
        else
        {
            OVRInput.SetControllerVibration(strongFrequency, _ampL, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(strongFrequency, _ampR, OVRInput.Controller.RTouch);
        }
    }

    private void StopHaptics()
    {
        _ampL = _ampR = 0f;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.All);
        _vibrating = false;
    }
}