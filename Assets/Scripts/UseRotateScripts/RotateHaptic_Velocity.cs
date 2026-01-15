using UnityEngine;

public class RotateHaptic_Velocity : MonoBehaviour
{
    [Header("주도하는 손 (Leading Hand) 설정")]
    [Tooltip("더 빠르게 움직이는 손: 묵직한 물리적 타격감")]
    public float leaderAmplitude = 0.8f;
    public float leaderFrequency = 0.2f;

    [Header("반대쪽 손 (Opposite Hand) 설정")]
    [Tooltip("느리게 움직이는 손: 팽팽한 저항감")]
    public float oppositeAmplitude = 0.4f;
    public float oppositeFrequency = 0.9f;

    [Header("민감도 및 데드존")]
    public float rotationThreshold = 0.1f;
    [Tooltip("미세한 손떨림으로 인한 주도권 역전 방지")]
    public float speedDeadzone = 0.05f;

    [Header("무게 설정")]
    public bool isHeavy = false;
    public float heavyMultiplier = 1.5f;

    private float previousYRotation;
    private bool isVibrating = false;

    void Start()
    {
        previousYRotation = transform.eulerAngles.y;
    }

    void Update()
    {
        float currentYRotation = transform.eulerAngles.y;
        float deltaRotation = Mathf.DeltaAngle(previousYRotation, currentYRotation);

        if (Mathf.Abs(deltaRotation) > rotationThreshold)
        {
            // 1. 각 컨트롤러의 속도(Velocity) 가져오기
            Vector3 leftVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
            Vector3 rightVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

            // 2. 속도 크기(Magnitude) 계산
            float lSpeed = leftVel.magnitude;
            float rSpeed = rightVel.magnitude;

            // 3. 주도하는 손 판별 (더 빠른 손이 Leader)
            bool leftIsLeader = lSpeed >= rSpeed;

            // 4. 최종 진폭/주파수 결정
            float lAmp, lFreq, rAmp, rFreq;

            if (leftIsLeader)
            {
                lAmp = leaderAmplitude; lFreq = leaderFrequency;
                rAmp = oppositeAmplitude; rFreq = oppositeFrequency;
            }
            else
            {
                lAmp = oppositeAmplitude; lFreq = oppositeFrequency;
                rAmp = leaderAmplitude; rFreq = leaderFrequency;
            }

            // 5. 무게 설정 적용
            if (isHeavy)
            {
                lAmp = Mathf.Clamp01(lAmp * heavyMultiplier);
                rAmp = Mathf.Clamp01(rAmp * heavyMultiplier);
            }

            // 6. 햅틱 출력
            OVRInput.SetControllerVibration(lFreq, lAmp, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(rFreq, rAmp, OVRInput.Controller.RTouch);

            // [데이터 로그] 어느 손이 주도하고 있는지 표시
            string leaderStr = leftIsLeader ? "<color=#00FF00>LEFT</color>" : "<color=#00FFFF>RIGHT</color>";
            Debug.Log($"[Haptics] Leader: {leaderStr} | L: {lAmp:F1}/{lFreq:F1} | R: {rAmp:F1}/{rFreq:F1}");

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
        Debug.Log("[Haptics] STOPPED");
        isVibrating = false;
    }

    void OnDisable() => StopHaptics();
}