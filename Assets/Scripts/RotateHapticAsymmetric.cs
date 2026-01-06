using UnityEngine;
using Oculus.Interaction;

public class RotateHapticAsymmetric : MonoBehaviour
{
    [Header("Grabbable")]
    public Grabbable grabbable;

    [Header("Haptic Amplitude")]
    [Range(0f, 1f)] public float strongAmp = 0.8f;
    [Range(0f, 1f)] public float weakAmp = 0.3f;

    [Header("Haptic Frequency")]
    [Range(0f, 1f)] public float driveFreq = 0.75f;
    [Range(0f, 1f)] public float resistFreq = 0.35f;

    [Header("Threshold")]
    public float minYawSpeed = 0.05f;

    [Header("Smoothing")]
    public float smooth = 10f;

    float curL, curR;

    void Update()
    {
        if (grabbable == null || grabbable.SelectingPoints.Count == 0)
        {
            StopHaptics();
            return;
        }

        // ① 컨트롤러 속도 획득
        Vector3 vL = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 vR = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

        // ② 카트 기준 회전 접선 속도
        Vector3 center = transform.position;
        Vector3 up = Vector3.up;

        float sL = Vector3.Dot(Vector3.Cross(up, OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch) - center), vL);
        float sR = Vector3.Dot(Vector3.Cross(up, OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch) - center), vR);

        float absL = Mathf.Abs(sL);
        float absR = Mathf.Abs(sR);

        if (absL + absR < minYawSpeed)
        {
            SmoothToZero();
            return;
        }

        // ③ 주도 손 판단
        bool leftDominant = absL > absR;

        float targetL = leftDominant ? strongAmp : weakAmp;
        float targetR = leftDominant ? weakAmp : strongAmp;

        curL = Mathf.Lerp(curL, targetL, smooth * Time.deltaTime);
        curR = Mathf.Lerp(curR, targetR, smooth * Time.deltaTime);

        // ④ 햅틱 출력
        OVRInput.SetControllerVibration(
            leftDominant ? driveFreq : resistFreq,
            curL,
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            leftDominant ? resistFreq : driveFreq,
            curR,
            OVRInput.Controller.RTouch
        );
    }

    void SmoothToZero()
    {
        curL = Mathf.Lerp(curL, 0, smooth * Time.deltaTime);
        curR = Mathf.Lerp(curR, 0, smooth * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        OVRInput.SetControllerVibration(driveFreq, curL, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(driveFreq, curR, OVRInput.Controller.RTouch);
    }

    void StopHaptics()
    {
        curL = curR = 0;
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }

    void OnDisable() => StopHaptics();

}
