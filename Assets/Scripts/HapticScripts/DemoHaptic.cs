using UnityEngine;
using Oculus.Interaction;

public class DemoHaptic : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;
    public Transform cartPivot;
    public Transform cargoObject;

    [Header("Grab Condition")]
    public bool requireTwoHands = false;

    [Header("Bias")]
    public float biasScale = 5f;
    public float centerDeadZone = 0.08f;

    [Header("Yaw Speed -> Haptics")]
    public float minYawSpeed = 8f;
    public float maxYawSpeed = 120f;
    public float responseCurve = 1.2f;

    [Header("Anti-Jitter Threshold")]
    public float yawDeltaThresholdDeg = 0.15f;   // 프레임당 이 각도 이하면 무시
    public float yawSpeedThreshold = 6f;         // deg/sec 이하면 햅틱 무시

    [Header("Amplitude")]
    [Range(0f, 1f)] public float strongMinAmp = 0.12f;
    [Range(0f, 1f)] public float strongMaxAmp = 0.85f;
    [Range(0f, 1f)] public float weakMinAmp = 0.03f;
    [Range(0f, 1f)] public float weakMaxAmp = 0.35f;

    [Header("Frequency")]
    [Range(0f, 1f)] public float strongFreq = 0.8f;
    [Range(0f, 1f)] public float weakFreq = 0.45f;

    [Header("Smoothing")]
    public float ampSmooth = 14f;

    private float _prevYaw;
    private float _leftAmp;
    private float _rightAmp;

    private void Start()
    {
        if (cartRoot != null)
            _prevYaw = NormalizeAngle(cartRoot.eulerAngles.y);
    }

    private void Update()
    {
        if (grabbable == null || cartRoot == null || cartPivot == null || cargoObject == null)
        {
            StopHaptics();
            return;
        }

        int grabCount = grabbable.SelectingPointsCount;
        bool isGrabbed = requireTwoHands ? (grabCount >= 2) : (grabCount > 0);

        if (!isGrabbed)
        {
            SmoothStop();
            ApplyHaptics(_leftAmp, _rightAmp);
            return;
        }

        float yawSpeedAbs = GetYawSpeedAbs();

        // 회전이 너무 작으면 진동 꺼서 떨림 방지
        if (yawSpeedAbs < yawSpeedThreshold)
        {
            SmoothStop();
            ApplyHaptics(_leftAmp, _rightAmp);
            return;
        }

        float speed01 = Mathf.InverseLerp(minYawSpeed, maxYawSpeed, yawSpeedAbs);
        speed01 = Mathf.Clamp01(speed01);
        speed01 = Mathf.Pow(speed01, responseCurve);

        float cargoLocalX = cartPivot.InverseTransformPoint(cargoObject.position).x;
        float rawBias = Mathf.Clamp(cargoLocalX * biasScale, -1f, 1f);

        float targetLeftAmp = 0f;
        float targetRightAmp = 0f;

        if (Mathf.Abs(rawBias) < centerDeadZone)
        {
            float amp = Mathf.Lerp(weakMinAmp, weakMaxAmp, speed01);
            targetLeftAmp = amp;
            targetRightAmp = amp;
        }
        else if (rawBias < 0f)
        {
            float bias01 = Mathf.InverseLerp(centerDeadZone, 1f, Mathf.Abs(rawBias));

            float strongAmp = Mathf.Lerp(strongMinAmp, strongMaxAmp, speed01) * Mathf.Lerp(0.6f, 1f, bias01);
            float weakAmp = Mathf.Lerp(weakMinAmp, weakMaxAmp, speed01) * Mathf.Lerp(0.5f, 1f, bias01);

            // 왼쪽으로 치우침 => 왼손 강, 오른손 약
            targetLeftAmp = strongAmp;
            targetRightAmp = weakAmp;
        }
        else
        {
            float bias01 = Mathf.InverseLerp(centerDeadZone, 1f, Mathf.Abs(rawBias));

            float strongAmp = Mathf.Lerp(strongMinAmp, strongMaxAmp, speed01) * Mathf.Lerp(0.6f, 1f, bias01);
            float weakAmp = Mathf.Lerp(weakMinAmp, weakMaxAmp, speed01) * Mathf.Lerp(0.5f, 1f, bias01);

            // 오른쪽으로 치우침 => 오른손 강, 왼손 약
            targetLeftAmp = weakAmp;
            targetRightAmp = strongAmp;
        }

        float lerpT = 1f - Mathf.Exp(-ampSmooth * Time.deltaTime);
        _leftAmp = Mathf.Lerp(_leftAmp, targetLeftAmp, lerpT);
        _rightAmp = Mathf.Lerp(_rightAmp, targetRightAmp, lerpT);

        ApplyHaptics(_leftAmp, _rightAmp);
    }

    private float GetYawSpeedAbs()
    {
        float currentYaw = NormalizeAngle(cartRoot.eulerAngles.y);
        float deltaYaw = Mathf.DeltaAngle(_prevYaw, currentYaw);
        _prevYaw = currentYaw;

        // 프레임당 아주 작은 회전은 노이즈로 보고 제거
        if (Mathf.Abs(deltaYaw) < yawDeltaThresholdDeg)
            deltaYaw = 0f;

        return Mathf.Abs(deltaYaw / Mathf.Max(Time.deltaTime, 0.0001f));
    }

    private void ApplyHaptics(float leftAmp, float rightAmp)
    {
        // 왼쪽이 더 무거운 상황이라면 (Amplitude가 더 높다면)
        float leftFreq = (leftAmp > rightAmp) ? strongFreq : weakFreq;
        float rightFreq = (rightAmp > leftAmp) ? strongFreq : weakFreq;

        OVRInput.SetControllerVibration(leftFreq, Mathf.Clamp01(leftAmp), OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(rightFreq, Mathf.Clamp01(rightAmp), OVRInput.Controller.RTouch);
        //OVRInput.SetControllerVibration(strongFreq, Mathf.Clamp01(leftAmp), OVRInput.Controller.LTouch);
        //OVRInput.SetControllerVibration(strongFreq, Mathf.Clamp01(rightAmp), OVRInput.Controller.RTouch);
    }

    private void SmoothStop()
    {
        float lerpT = 1f - Mathf.Exp(-ampSmooth * Time.deltaTime);
        _leftAmp = Mathf.Lerp(_leftAmp, 0f, lerpT);
        _rightAmp = Mathf.Lerp(_rightAmp, 0f, lerpT);
    }

    private void OnDisable()
    {
        StopHaptics();
    }

    private void OnDestroy()
    {
        StopHaptics();
    }

    private void StopHaptics()
    {
        _leftAmp = 0f;
        _rightAmp = 0f;
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}