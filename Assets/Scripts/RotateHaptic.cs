using UnityEngine;

public class RotateHaptic : MonoBehaviour
{
    [Header("햅틱 세기 설정")]
    [Tooltip("강한 진동의 세기 (0~1)")]
    [Range(0f, 1f)]
    public float strongHaptic = 0.8f;

    [Tooltip("약한 진동의 세기 (0~1)")]
    [Range(0f, 1f)]
    public float weakHaptic = 0.2f;

    [Header("햅틱 주파수 (Frequency)")]
    [Tooltip("강한 진동의 주파수 (0~1). 높을수록 '드득드득'합니다.")]
    [Range(0f, 1f)]
    public float strongFrequency = 0.9f;

    [Tooltip("약한 진동의 주파수 (0~1). 낮을수록 '드르르'합니다.")]
    [Range(0f, 1f)]
    public float weakFrequency = 0.2f;

    [Header("민감도 설정")]
    [Tooltip("이 값보다 크게 회전해야 햅틱이 작동합니다.")]
    public float rotationThreshold = 0.1f;

    [Header("무게 설정")]
    [Tooltip("이 항목을 체크하면 햅틱이 더 강해집니다.")]
    public bool isHeavy = false;

    [Tooltip("무거울 때 햅틱을 몇 배 증폭시킬지 설정합니다.")]
    public float heavyMultiplier = 2.0f;

    private float previousYRotation;
    private bool isVibrating = false;

    // 스크립트가 시작될 때 한 번 호출됩니다.
    void Start()
    {
        // 처음 회전 값을 저장합니다.
        previousYRotation = transform.eulerAngles.y;
    }

    // 매 프레임마다 호출됩니다.
    void Update()
    {
        float currentYRotation = transform.eulerAngles.y;
        // 이전 프레임과 현재 프레임의 회전 값 차이를 계산합니다.
        float deltaRotation = currentYRotation - previousYRotation;

        // 359도 -> 0도 처럼 값이 갑자기 점프하는 경우를 보정합니다.
        if (deltaRotation > 180f) { deltaRotation -= 360f; }
        if (deltaRotation < -180f) { deltaRotation += 360f; }

        // isHeavy가 true이면 햅틱 세기를 증폭시킵니다.
        float currentStrong = isHeavy ? strongHaptic * heavyMultiplier : strongHaptic;
        float currentWeak = isHeavy ? weakHaptic * heavyMultiplier : weakHaptic;
        // 1을 넘지 않도록 값을 제한합니다.
        currentStrong = Mathf.Clamp01(currentStrong);
        currentWeak = Mathf.Clamp01(currentWeak);

        // 회전 변화량이 민감도 값보다 클 때만 햅틱을 실행합니다.
        if (Mathf.Abs(deltaRotation) > rotationThreshold)
        {
            // 왼쪽으로 회전할 때 (값이 감소)
            if (deltaRotation < 0)
            {
                // 왼쪽 컨트롤러는 강하게, 오른쪽은 약하게
                OVRInput.SetControllerVibration(strongFrequency, strongHaptic, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(weakFrequency, weakHaptic, OVRInput.Controller.RTouch);
            }
            // 오른쪽으로 회전할 때 (값이 증가)
            else
            {
                // 왼쪽 컨트롤러는 약하게, 오른쪽은 강하게
                OVRInput.SetControllerVibration(weakFrequency, weakHaptic, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(strongFrequency, strongHaptic, OVRInput.Controller.RTouch);
            }
            isVibrating = true;
        }
        else
        {
            // 회전이 멈췄고, 이전에 진동이 울리고 있었다면 진동을 끕니다.
            if (isVibrating)
            {
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
                isVibrating = false;
            }
        }

        // 다음 프레임을 위해 현재 회전 값을 저장합니다.
        previousYRotation = currentYRotation;
    }

    // 오브젝트가 비활성화되거나 파괴될 때 진동을 확실히 끕니다.
    void OnDisable()
    {
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}