using UnityEngine;
using Oculus.Interaction;

public class HapticWeightManager : MonoBehaviour
{
    [Header("References")]
    public DynamicWeightTwoGrabPlaneTransformer Transformer;

    [Header("Haptic Settings")]
    [Range(0, 1)] public float MaxAmplitude = 0.5f;
    public float DistanceGain = 5.0f; // 거리당 진동 세기 가중치

    void Update()
    {
        // 1. 현재 지연 거리(Visual Offset) 계산
        float currentLagDistance = Transformer.GetCurrentLagDistance();

        // 2. 진동 세기 결정
        float amplitude = Mathf.Min(currentLagDistance * DistanceGain, MaxAmplitude);

        // 3. [추가] 콘솔창에 현재 상태 출력
        // 상자 위치, 오프셋 거리, 최종 진동 세기를 한눈에 확인합니다.
        string targetSide = Transformer.CurrentTargetSide.ToString();
        Debug.Log($"<color=cyan>[HapticDebug]</color> <b>Target: {targetSide}</b> | " +
                  $"LagDist: {currentLagDistance:F4} | " +
                  $"Amp: {amplitude:F4}");

        // 4. 비대칭 진동 전달 로직
        if (Transformer.CurrentTargetSide == DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Left)
        {
            // 왼쪽 상자: 왼손(강), 오른손(약)
            float leftAmp = amplitude;
            float rightAmp = amplitude * 0.2f;

            TriggerHaptic(OVRInput.Controller.LTouch, leftAmp, 0.5f);
            TriggerHaptic(OVRInput.Controller.RTouch, rightAmp, 0.8f);

            // 상세 로그가 필요할 경우 추가
            // Debug.Log($"L_Hand(Heavy): {leftAmp:F2} / R_Hand(Light): {rightAmp:F2}");
        }
        else
        {
            // 오른쪽 상자: 오른손(강), 왼손(약)
            float leftAmp = amplitude * 0.2f;
            float rightAmp = amplitude;

            TriggerHaptic(OVRInput.Controller.RTouch, rightAmp, 0.5f);
            TriggerHaptic(OVRInput.Controller.LTouch, leftAmp, 0.8f);

            // Debug.Log($"R_Hand(Heavy): {rightAmp:F2} / L_Hand(Light): {leftAmp:F2}");
        }
    }

    private void TriggerHaptic(OVRInput.Controller controller, float amp, float freq)
    {
        // Meta Quest 컨트롤러 진동 실행 (진폭, 주파수 순)
        OVRInput.SetControllerVibration(freq, amp, controller);
    }

    private void OnDisable()
    {
        // 종료 시 진동 초기화
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}