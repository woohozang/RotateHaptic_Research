using UnityEngine;

public class RotateHapticLeadingHand : MonoBehaviour
{
    [Header("1. 운동감 (동기화/주도 손 전용)")]
    [Range(0f, 1f)] public float moveFrequency = 0.25f;
    [Range(0f, 1f)] public float moveAmplitude = 0.8f;

    [Header("2. 저항감 (한 손 조작 시 보조 손 전용)")]
    [Range(0f, 1f)] public float resistFrequency = 0.85f;
    [Range(0f, 1f)] public float resistAmplitude = 0.2f;

    [Header("3. 양손 협동(Synergy) 설정")]
    [Range(0.5f, 1f)] public float synergyThreshold = 0.7f;
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
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float currentYRotation = transform.eulerAngles.y;
        float deltaRotation = Mathf.DeltaAngle(previousYRotation, currentYRotation);

        // [추가] 각속도 계산 (deg/s)
        float angularSpeed = Mathf.Abs(deltaRotation) / dt;

        float finalMoveAmp = isHeavy ? moveAmplitude * heavyMultiplier : moveAmplitude;
        float finalResistAmp = isHeavy ? resistAmplitude * heavyMultiplier : resistAmplitude;

        if (Mathf.Abs(deltaRotation) > rotationThreshold)
        {
            Vector3 leftVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
            Vector3 rightVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
            float lSpeed = Mathf.Max(0, leftVel.magnitude - speedDeadzone);
            float rSpeed = Mathf.Max(0, rightVel.magnitude - speedDeadzone);

            float maxSpeed = Mathf.Max(lSpeed, rSpeed);
            float minSpeed = Mathf.Min(lSpeed, rSpeed);
            float speedRatio = (maxSpeed > 0) ? minSpeed / maxSpeed : 0;

            bool isSynergy = (lSpeed > 0 && rSpeed > 0) && (speedRatio >= synergyThreshold);

            float lFreq, lAmp, rFreq, rAmp;
            string leadingHand; // 로그용 변수

            if (isSynergy)
            {
                leadingHand = "<color=#00FFFF>BOTH (Synergy)</color>";
                lFreq = rFreq = moveFrequency;
                lAmp = rAmp = Mathf.Clamp01(finalMoveAmp * synergyBoost);
            }
            else
            {
                bool leftIsLeader = lSpeed >= rSpeed;
                leadingHand = leftIsLeader ? "<color=#55FF55>LEFT</color>" : "<color=#5555FF>RIGHT</color>";

                lFreq = leftIsLeader ? moveFrequency : resistFrequency;
                lAmp = leftIsLeader ? finalMoveAmp : finalResistAmp;
                rFreq = leftIsLeader ? resistFrequency : moveFrequency;
                rAmp = leftIsLeader ? finalResistAmp : finalMoveAmp;
            }

            // 햅틱 출력
            OVRInput.SetControllerVibration(lFreq, lAmp, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(rFreq, rAmp, OVRInput.Controller.RTouch);
            isVibrating = true;

            // [강화된 디버그 로그]
            // 각속도, 주도 손, 각 손의 진폭 및 주파수 통합 출력
            Debug.Log($"<b>[Haptic Active]</b> \n" +
                      $"▶ AngularSpeed: <color=yellow>{angularSpeed:F1}°/s</color> | Leader: {leadingHand} \n" +
                      $"▶ L-Hand: Amp({lAmp:F2}), Freq({lFreq:F2}) \n" +
                      $"▶ R-Hand: Amp({rAmp:F2}), Freq({rFreq:F2})");
        }
        else if (isVibrating)
        {
            StopHaptics();
            Debug.Log("<color=#FF5555><b>[Haptic Stopped]</b></color>");
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