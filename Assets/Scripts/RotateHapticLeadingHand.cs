using UnityEngine;

public class RotateHapticLeadingHand : MonoBehaviour
{
    [Header("1. 운동감 (동기화/주도 손 전용)")]
    [Tooltip("낮은 주파수로 묵집한 물리적 타격감을 줍니다.")]
    [Range(0f, 1f)] public float moveFrequency = 0.25f;
    [Range(0f, 1f)] public float moveAmplitude = 0.8f;

    [Header("2. 저항감 (한 손 조작 시 보조 손 전용)")]
    [Tooltip("높은 주파수로 팽팽한 저항감을 줍니다.")]
    [Range(0f, 1f)] public float resistFrequency = 0.85f;
    [Range(0f, 1f)] public float resistAmplitude = 0.2f;

    [Header("3. 양손 협동(Synergy) 설정")]
    [Tooltip("양손 속도 비율이 이 값 이상이면 동일한 진동을 느낍니다 (0~1)")]
    [Range(0.5f, 1f)] public float synergyThreshold = 0.7f;
    [Tooltip("양손으로 조작할 때 힘이 더 실리므로 진폭을 추가로 증폭합니다.")]
    public float synergyBoost = 1.2f;

    [Header("기본 설정")]
    public float rotationThreshold = 0.1f;
    public float speedDeadzone = 0.05f;
    public bool isHeavy = false;
    public float heavyMultiplier = 2.0f;

    private float previousYRotation;
    private bool isVibrating = false;

    void Start() => previousYRotation = transform.eulerAngles.y;

    void Update()
    {
        float currentYRotation = transform.eulerAngles.y;
        float deltaRotation = Mathf.DeltaAngle(previousYRotation, currentYRotation);

        // 기본 진폭 계산
        float finalMoveAmp = isHeavy ? moveAmplitude * heavyMultiplier : moveAmplitude;
        float finalResistAmp = isHeavy ? resistAmplitude * heavyMultiplier : resistAmplitude;

        if (Mathf.Abs(deltaRotation) > rotationThreshold)
        {
            // 컨트롤러 속도 측정
            Vector3 leftVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
            Vector3 rightVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
            float lSpeed = Mathf.Max(0, leftVel.magnitude - speedDeadzone);
            float rSpeed = Mathf.Max(0, rightVel.magnitude - speedDeadzone);

            // ★ 양손 협동(Synergy) 판별 로직
            // 두 손의 속도 중 작은 값을 큰 값으로 나누어 유사도를 구함
            float maxSpeed = Mathf.Max(lSpeed, rSpeed);
            float minSpeed = Mathf.Min(lSpeed, rSpeed);
            float speedRatio = (maxSpeed > 0) ? minSpeed / maxSpeed : 0;

            // 두 손이 모두 움직이고 있고, 속도 비율이 설정값 이상일 때 (협동 모드)
            bool isSynergy = (lSpeed > 0 && rSpeed > 0) && (speedRatio >= synergyThreshold);

            float lFreq, lAmp, rFreq, rAmp;

            if (isSynergy)
            {
                // 양손에 동일한 강력한 운동감 적용
                lFreq = rFreq = moveFrequency;
                lAmp = rAmp = Mathf.Clamp01(finalMoveAmp * synergyBoost);

                Debug.Log($"<color=#00FFFF>[Synergy Mode]</color> Ratio: {speedRatio:F2} | Unified Amp: {lAmp:F2}");
            }
            else
            {
                // 기존 비대칭 로직 (더 빠른 손이 Leader)
                bool leftIsLeader = lSpeed >= rSpeed;
                lFreq = leftIsLeader ? moveFrequency : resistFrequency;
                lAmp = leftIsLeader ? finalMoveAmp : finalResistAmp;
                rFreq = leftIsLeader ? resistFrequency : moveFrequency;
                rAmp = leftIsLeader ? finalResistAmp : finalMoveAmp;

                Debug.Log($"[Asymmetric] Leader: {(leftIsLeader ? "L" : "R")} | Ratio: {speedRatio:F2}");
            }

            // 햅틱 출력
            OVRInput.SetControllerVibration(lFreq, lAmp, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(rFreq, rAmp, OVRInput.Controller.RTouch);
            isVibrating = true;
        }
        else if (isVibrating)
        {
            StopHaptics();
        }

        previousYRotation = currentYRotation;
    }

    private void StopHaptics()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
        isVibrating = false;
    }
}