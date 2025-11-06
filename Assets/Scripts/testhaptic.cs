using UnityEngine;

public class testhaptic : MonoBehaviour
{
    [Header("햅틱 세기 설정")]
    [Range(0f, 1f)] public float strongHaptic = 0.8f;
    [Range(0f, 1f)] public float weakHaptic = 0.2f;

    [Header("햅틱 주파수 (0~1)")]
    [Range(0f, 1f)] public float strongFrequency = 0.9f;
    [Range(0f, 1f)] public float weakFrequency = 0.25f;

    [Header("민감도")]
    [Tooltip("이 값(도/프레임)보다 크게 회전해야 햅틱 작동")]
    public float rotationThreshold = 0.1f;

    [Tooltip("컨트롤러 속도가 이 값보다 작으면 '움직이지 않음'으로 간주 (m/s)")]
    public float speedDeadzone = 0.03f;

    [Header("무게 옵션")]
    public bool isHeavy = false;
    public float heavyMultiplier = 2.0f;

    [Header("스케일링")]
    [Tooltip("회전 변화량이 이 값(도/프레임)일 때 세기 100% 스케일")]
    public float rotationToMax = 20f;

    private float previousYRotation;
    private bool isVibrating = false;

    void Start()
    {
        previousYRotation = transform.eulerAngles.y;
    }

    void Update()
    {
        // 1) 회전량 계산 (도)
        float currentYRotation = transform.eulerAngles.y;
        float delta = currentYRotation - previousYRotation;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;

        // 2) 현재 프레임의 컨트롤러 수평 속도 크기(=기여도) 가져오기
        //    OVRInput의 속도는 로컬 트래킹공간 기준이지만, "크기 비교"에는 그대로 사용 가능
        Vector3 lv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 rv = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        lv.y = 0f; rv.y = 0f; // 기여도는 수평(Yaw)에만 관련

        float lSpeed = lv.magnitude;
        float rSpeed = rv.magnitude;

        // 너무 느리면 0으로 취급(기여도 제거)
        if (lSpeed < speedDeadzone) lSpeed = 0f;
        if (rSpeed < speedDeadzone) rSpeed = 0f;

        // 3) 무게 옵션 적용
        float strong = isHeavy ? Mathf.Clamp01(strongHaptic * heavyMultiplier) : strongHaptic;
        float weak = isHeavy ? Mathf.Clamp01(weakHaptic * heavyMultiplier) : weakHaptic;

        // 4) 회전량이 임계 이상일 때만 작동 + 회전량 스케일 적용
        if (Mathf.Abs(delta) > rotationThreshold)
        {
            // 회전 강도 스케일 (도/프레임 → 0~1)
            float rotScale = Mathf.Clamp01(Mathf.Abs(delta) / Mathf.Max(1e-3f, rotationToMax));

            // 어느 손이 더 기여했는가?
            bool leftDominant = lSpeed >= rSpeed;

            // 기여도 차이가 크면(한 손만 주로 움직임) 강/약 명확히, 비슷하면 둘 다 중간 정도로
            float diff = Mathf.Abs(lSpeed - rSpeed);
            float sum = Mathf.Max(lSpeed + rSpeed, 1e-4f);
            float dominance = Mathf.Clamp01(diff / sum); // 0=비슷, 1=한 손만

            float strongScaled = strong * rotScale;
            float weakScaled = Mathf.Lerp(strongScaled * 0.6f, weak * rotScale, dominance);
            // dominance 낮으면 둘 다 어느정도 울리도록 완화

            if (leftDominant)
            {
                OVRInput.SetControllerVibration(strongFrequency, strongScaled, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(weakFrequency, weakScaled, OVRInput.Controller.RTouch);
            }
            else
            {
                OVRInput.SetControllerVibration(weakFrequency, weakScaled, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(strongFrequency, strongScaled, OVRInput.Controller.RTouch);
            }
            isVibrating = true;
        }
        else
        {
            if (isVibrating)
            {
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
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
