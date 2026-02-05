using UnityEngine;

public class HapticsBySpeeds : MonoBehaviour
{
    [Header("Active while grabbing")]
    public bool hapticsActive = false;

    [Header("Speed source")]
    [Tooltip("true면 선속도, false면 각속도(회전) 기반")]
    public bool useLinearVelocity = true;

    [Header("Dead Zone (micro jitter prevention)")]
    [Tooltip("이 값 이하의 속도는 0으로 간주(미세 떨림 제거)")]
    public float speedThreshold = 0.03f;

    [Header("Left hand (dynamic)")]
    public float leftSpeedMin = 0.05f;
    public float leftSpeedMax = 1.0f;

    [Range(0f, 1f)] public float leftAmpMin = 0.05f;
    [Range(0f, 1f)] public float leftAmpMax = 0.7f;
    [Range(0f, 1f)] public float leftFrequency = 0.7f;

    [Header("Right hand (weak constant buzz)")]
    [Range(0f, 1f)] public float rightAmp = 0.08f;
    [Range(0f, 1f)] public float rightFrequency = 0.3f;

    [Header("Smoothing")]
    [Range(0f, 30f)] public float smoothing = 12f;

    private float _leftAmpSmoothed = 0f;

    void Update()
    {
        if (!hapticsActive)
        {
            StopAllHaptics();
            return;
        }

        Vector3 vL = useLinearVelocity
            ? OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch)
            : OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.LTouch);

        Vector3 vR = useLinearVelocity
            ? OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch)
            : OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);

        float speedL = vL.magnitude;
        float speedR = vR.magnitude;

        // ------------------------------
        // Dead Zone 적용 (미세 떨림 제거)
        // ------------------------------
        if (speedL < speedThreshold) speedL = 0f;
        if (speedR < speedThreshold) speedR = 0f;

        // ------------------------------
        // LEFT : speed → amplitude
        // ------------------------------
        float leftAmpTarget = 0f;

        if (speedL > 0f)
        {
            float t = Mathf.InverseLerp(leftSpeedMin, leftSpeedMax, speedL);
            leftAmpTarget = Mathf.Lerp(leftAmpMin, leftAmpMax, t);
        }

        // 스무딩
        if (smoothing > 0f)
        {
            float a = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            _leftAmpSmoothed = Mathf.Lerp(_leftAmpSmoothed, leftAmpTarget, a);
        }
        else
        {
            _leftAmpSmoothed = leftAmpTarget;
        }

        // ------------------------------
        // RIGHT : 약한 고정 buzz (speedThreshold 이하이면 OFF)
        // ------------------------------
        float rightAmpFinal = speedR > 0f ? rightAmp : 0f;

        // ------------------------------
        // Apply
        // ------------------------------
        OVRInput.SetControllerVibration(leftFrequency, _leftAmpSmoothed, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(rightFrequency, rightAmpFinal, OVRInput.Controller.RTouch);
    }

    public void BeginHaptics()
    {
        hapticsActive = true;
        _leftAmpSmoothed = 0f;
    }

    public void EndHaptics()
    {
        hapticsActive = false;
        StopAllHaptics();
    }

    private void OnDisable()
    {
        StopAllHaptics();
    }

    private static void StopAllHaptics()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }
}
