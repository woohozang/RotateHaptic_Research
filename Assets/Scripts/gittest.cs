using UnityEngine;

public class HeavyCartController : MonoBehaviour
{
    [Header("C/D Gain Settings")]
    public float minGain = 0.3f;      // 최소 회전 비율 (30%)
    public float sensitivity = 0.5f; // 저항 감도 (k)

    [Header("Virtual Cart State")]
    public Transform virtualCartHandle; // 가상 카트 핸들 오브젝트
    private float virtualRotationZ = 0f;

    void Update()
    {
        // 1. 현실 컨트롤러의 각속도 계산 (OVRInput 사용)
        // 여기서는 단순화를 위해 컨트롤러의 로컬 회전 변화량을 사용합니다.
        float realOmega = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch).z;

        // 2. 비선형 C/D Gain 계산
        // 속도가 빠를수록 gain이 minGain에 가까워짐
        float cdGain = minGain + (1f - minGain) * Mathf.Exp(-sensitivity * Mathf.Abs(realOmega));

        // 3. 가상 회전 적용
        float deltaRotation = realOmega * cdGain * Time.deltaTime * Mathf.Rad2Deg;
        virtualRotationZ += deltaRotation;

        // 가상 카트 핸들에 회전값 적용
        virtualCartHandle.localRotation = Quaternion.Euler(0, 0, virtualRotationZ);

        // 4. 비대칭 햅틱 피드백 (시각적 차이만큼 강화)
        // (현실 속도 - 가상 속도)의 차이가 클수록 사용자가 느끼는 '저항'이 크다고 판단
        float visualDiscrepancy = Mathf.Abs(realOmega * (1f - cdGain));
        ApplyAsymmetricHaptics(realOmega, visualDiscrepancy);
    }

    void ApplyAsymmetricHaptics(float omega, float discrepancy)
    {
        // 주도하는 손: 실제 움직임의 속도감 (고주파)
        float leadingAmp = Mathf.Clamp(Mathf.Abs(omega) * 0.2f, 0f, 0.4f);
        OVRInput.SetControllerVibration(0.8f, leadingAmp, OVRInput.Controller.RTouch);

        // 반대 손: 시각적 괴리(저항)에 비례한 묵직함 (저주파)
        // 카트가 손을 못 따라올수록 진동이 강해져서 "무겁다"는 피드백 제공
        float anchorAmp = Mathf.Clamp(discrepancy * 0.5f, 0.1f, 0.8f);
        OVRInput.SetControllerVibration(0.2f, anchorAmp, OVRInput.Controller.LTouch);
    }
}