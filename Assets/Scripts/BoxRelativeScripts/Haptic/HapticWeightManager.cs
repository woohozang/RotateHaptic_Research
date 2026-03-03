using UnityEngine;
using Oculus.Interaction;

public class HapticWeightManager : MonoBehaviour
{
    [Header("References")]
    public DynamicWeightTwoGrabPlaneTransformer Transformer;
    public CartWeightController WeightController; // 사과 개수를 관리하는 스크립트

    [Header("Haptic Mapping")]
    [Tooltip("사과 1개당 증가할 진동 세기")]
    public float AmplitudePerApple = 0.05f;
    [Range(0, 1)] public float MaxAmplitude = 0.7f;
    [Range(0, 1)] public float BaseVibration = 0.05f; // 사과가 0개일 때의 기본 진동

    void Update()
    {
        // 1. [수정] 거리(LagDist) 대신 사과 개수로 진폭 계산
        // 이제 박스 스케일이 0.7이든 1.2든 진동 값에 영향을 주지 않습니다.
        int appleCount = WeightController.GetCurrentAppleCount();
        float calculatedAmp = BaseVibration + (appleCount * AmplitudePerApple);
        float finalAmplitude = Mathf.Clamp(calculatedAmp, 0, MaxAmplitude);

        // 2. 디버그 로그 출력 (수치 확인용)
        Debug.Log($"<color=yellow>[CountHaptic]</color> Apple: {appleCount} | Amp: {finalAmplitude:F4} | Target: {Transformer.CurrentTargetSide}");

        // 3. 비대칭 진동 전달 (상자가 있는 쪽은 100%, 반대쪽은 20% 세기)
        if (Transformer.CurrentTargetSide == DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Left)
        {
            SendHaptic(OVRInput.Controller.LTouch, finalAmplitude, 0.5f);      // 무거운 쪽
            SendHaptic(OVRInput.Controller.RTouch, finalAmplitude * 0.2f, 0.8f); // 가벼운 쪽
        }
        else
        {
            SendHaptic(OVRInput.Controller.RTouch, finalAmplitude, 0.5f);      // 무거운 쪽
            SendHaptic(OVRInput.Controller.LTouch, finalAmplitude * 0.2f, 0.8f); // 가벼운 쪽
        }
    }

    private void SendHaptic(OVRInput.Controller controller, float amp, float freq)
    {
        OVRInput.SetControllerVibration(freq, amp, controller);
    }

    private void OnDisable()
    {
        // 종료 시 진동 초기화
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}