using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// [수정] 적재 질량(생수병 2/4/6개)에 비례하여 무게 저항 진동의 세기와 주파수를 조절합니다.
///   - 진폭: 적재 질량 정규화 값(NormalizedLoad)에 비례 → 2개: 0.333배, 4개: 0.667배, 6개: 1.0배
///   - 주파수: 무거울수록 저주파 → 2개: 0.30, 4개: 0.20, 6개: 0.10 (무거운 객체 = 저주파 연합)
/// 기존의 (40.5 - PosFollow) 휴리스틱을 질량 기반 정규화 이득으로 대체하여,
/// 논문 3.2절의 "감쇄 폭에 정비례하는 진폭" 설계와 일치시켰습니다.
/// </summary>
public class VibScript : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GrabCDScript _transformer;

    [Header("Anti-Shake Settings")]
    [Tooltip("이 거리(m) 미만의 떨림은 진동을 발생시키지 않습니다. (추천: 0.005 ~ 0.01)")]
    public float hapticDeadZone = 0.005f;
    [Tooltip("진동 값이 변화하는 부드러움 정도 (낮을수록 부드러움)")]
    public float hapticSmoothing = 0.15f;

    [Header("Speed Haptic Settings")]
    public float speedSensitivity = 0.5f;
    public float maxSpeedHaptic = 0.4f;

    [Header("Weight/Lag Haptic Settings")]
    [Tooltip("지연 거리(m)→진폭 변환 이득. 정상상태 지연(0.03~0.05m)에서 충분한 진폭이 나오도록 설정")]
    public float weightForceMultiplier = 12f;
    public float maxWeightHaptic = 0.7f;

    [Header("Mass-based Frequency Settings")]
    [Tooltip("적재 0일 때(가벼움)의 진동 주파수 (0~1)")]
    [Range(0f, 1f)] public float lightFrequency = 0.4f;
    [Tooltip("최대 적재(6개=9kg)일 때의 진동 주파수 (0~1). 무거울수록 저주파")]
    [Range(0f, 1f)] public float heavyFrequency = 0.1f;

    private float _smoothedWeightIntensity; // 부드러운 진동 전환을 위한 변수




    void Update()
    {
        if (_transformer == null) return;

        float lagDist = _transformer.GetCurrentLagDistance();

        // 1. 손을 뗐을 때 즉시 종료
        if (lagDist <= 0f)
        {
            _smoothedWeightIntensity = 0;
            StopHaptics();
            return;
        }

        // 2. [손떨림 방지] 데드존 적용
        float filteredLagDist = Mathf.Max(0, lagDist - hapticDeadZone);

        // 3. 속도 기반 공통 진동
        Vector3 lVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        float lSpeedHaptic = Mathf.Clamp(lVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);
        float rSpeedHaptic = Mathf.Clamp(rVel.magnitude * speedSensitivity, 0, maxSpeedHaptic);

        // 4. [핵심 수정] 질량 기반 무게 저항 진동 계산
        //    massGain: 생수병 2개=0.333, 4개=0.667, 6개=1.0 (적재 질량 / 최대 적재 질량)
        //    지연 거리(filteredLagDist)와 질량 이득의 곱 → C/D 감쇄 폭에 정비례하는 진폭
        float massGain = _transformer.NormalizedLoad;
        float targetWeightIntensity = massGain * filteredLagDist * weightForceMultiplier;
        targetWeightIntensity = Mathf.Clamp(targetWeightIntensity, 0, maxWeightHaptic);

        // 5. [부드러운 전환] 급격한 진동 변화 방지
        _smoothedWeightIntensity = Mathf.Lerp(_smoothedWeightIntensity, targetWeightIntensity, hapticSmoothing);

        // 6. [핵심 수정] 질량 기반 주파수: 무거울수록 저주파 (2개: 0.30 → 6개: 0.10)
        float vibFrequency = Mathf.Lerp(lightFrequency, heavyFrequency, massGain);

        // 7. 비대칭 할당 및 송출 — 무게가 치우친 손에만 무게 저항 진동 가산
        if (_transformer.CurrentTargetSide == GrabCDScript.TargetLagSide.Left)
        {
            TriggerHaptics(vibFrequency, lSpeedHaptic + _smoothedWeightIntensity, rSpeedHaptic);
        }
        else
        {
            TriggerHaptics(vibFrequency, lSpeedHaptic, rSpeedHaptic + _smoothedWeightIntensity);
        }
    }

    private void TriggerHaptics(float frequency, float leftAmp, float rightAmp)
    {
        OVRInput.SetControllerVibration(frequency, leftAmp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(frequency, rightAmp, OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}