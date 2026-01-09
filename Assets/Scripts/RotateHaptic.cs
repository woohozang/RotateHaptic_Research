using UnityEngine;

public class RotateHaptic : MonoBehaviour
{
    [Header("햅틱 세기 설정")]
    [Range(0f, 1f)] public float strongHaptic = 0.8f;
    [Range(0f, 1f)] public float weakHaptic = 0.2f;

    [Header("햅틱 주파수 (Frequency)")]
    [Range(0f, 1f)] public float strongFrequency = 0.9f;
    [Range(0f, 1f)] public float weakFrequency = 0.2f;

    [Header("민감도 설정")]
    public float rotationThreshold = 0.1f;

    [Header("무게 설정")]
    public bool isHeavy = false;
    public float heavyMultiplier = 2.0f;

    private float previousYRotation;
    private bool isVibrating = false;

    void Start()
    {
        previousYRotation = transform.eulerAngles.y;
    }

    void Update()
    {
        float currentYRotation = transform.eulerAngles.y;
        float deltaRotation = currentYRotation - previousYRotation;

        if (deltaRotation > 180f) { deltaRotation -= 360f; }
        if (deltaRotation < -180f) { deltaRotation += 360f; }

        // 무게 적용된 최종 진폭 계산
        float currentStrong = isHeavy ? strongHaptic * heavyMultiplier : strongHaptic;
        float currentWeak = isHeavy ? weakHaptic * heavyMultiplier : weakHaptic;

        currentStrong = Mathf.Clamp01(currentStrong);
        currentWeak = Mathf.Clamp01(currentWeak);

        if (Mathf.Abs(deltaRotation) > rotationThreshold)
        {
            float lFreq, lAmp, rFreq, rAmp;

            if (deltaRotation < 0) // 왼쪽 회전
            {
                lFreq = strongFrequency; lAmp = currentStrong;
                rFreq = weakFrequency; rAmp = currentWeak;
            }
            else // 오른쪽 회전
            {
                lFreq = weakFrequency; lAmp = currentWeak;
                rFreq = strongFrequency; rAmp = currentStrong;
            }

            // 실제 진동 출력
            OVRInput.SetControllerVibration(lFreq, lAmp, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(rFreq, rAmp, OVRInput.Controller.RTouch);

            // [추가] 실시간 데이터 로그
            Debug.Log($"[Haptics] L-Hand: Amp({lAmp:F2}), Freq({lFreq:F2}) | R-Hand: Amp({rAmp:F2}), Freq({rFreq:F2})");

            isVibrating = true;
        }
        else
        {
            if (isVibrating)
            {
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);

                // [추가] 정지 로그
                Debug.Log("[Haptics] STOPPED");
                isVibrating = false;
            }
        }

        previousYRotation = currentYRotation;
    }

    void OnDisable()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}