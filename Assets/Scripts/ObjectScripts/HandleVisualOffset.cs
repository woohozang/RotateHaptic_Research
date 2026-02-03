using UnityEngine;

public class ControllerDrivenVisualSlip : MonoBehaviour
{
    [Header("References")]
    public CartLoadBias biasSource;      // 수박 좌/우 Bias
    public Transform trackingSpace;      // OVRCameraRig의 TrackingSpace(권장)
    public Transform handleVisual;       // Handle_Visual

    [Header("Slip Tuning (visual only)")]
    public float maxSlipX = 0.02f;       // 최대 좌우 슬립(미터) 0.01~0.03 추천
    public float maxYawDeg = 5f;         // 최대 비틀림(도) 2~8 추천
    public float speedToSlip = 0.25f;    // 속도->슬립 변환 스케일(튜닝용)
    public float speedDeadzone = 0.05f;  // 작은 떨림 무시 (m/s 정도 느낌)
    public float smooth = 12f;

    // 내부 상태
    Vector3 _initLocalPos;
    Quaternion _initLocalRot;

    Vector3 _prevL, _prevR;
    float _slipSmoothed; // -1..+1 비슷한 값으로 관리

    void Start()
    {
        if (!handleVisual) handleVisual = transform;
        _initLocalPos = handleVisual.localPosition;
        _initLocalRot = handleVisual.localRotation;

        // 초기 컨트롤러 포즈 샘플 (trackingSpace 기준 로컬로 맞춤)
        _prevL = GetControllerPosLocal(OVRInput.Controller.LTouch);
        _prevR = GetControllerPosLocal(OVRInput.Controller.RTouch);
    }

    void LateUpdate()
    {
        if (!biasSource || !handleVisual) return;

        // 1) Bias로 어느 손이 "무거운 쪽"인지 결정
        float b = biasSource.Bias;            // -1..+1
        float leftHeavy = Mathf.Clamp01(-b); // 왼쪽 무거움
        float rightHeavy = Mathf.Clamp01(b); // 오른쪽 무거움

        // 2) 컨트롤러 속도(차분)
        float dt = Mathf.Max(Time.deltaTime, 1e-5f);

        Vector3 curL = GetControllerPosLocal(OVRInput.Controller.LTouch);
        Vector3 curR = GetControllerPosLocal(OVRInput.Controller.RTouch);

        float vL = (curL - _prevL).magnitude / dt;
        float vR = (curR - _prevR).magnitude / dt;

        _prevL = curL;
        _prevR = curR;

        // 3) 떨림 제거(데드존)
        vL = Mathf.Max(0f, vL - speedDeadzone);
        vR = Mathf.Max(0f, vR - speedDeadzone);

        // 4) 손별 슬립 기여도 (속도 기반)
        // 무거운 손이 많이 움직일수록 슬립이 커지게
        float slipL = leftHeavy * (vL * speedToSlip);
        float slipR = rightHeavy * (vR * speedToSlip);

        // 5) 최종 슬립 방향값 만들기
        // 왼쪽이 무거우면(-) 방향으로, 오른쪽이 무거우면(+) 방향으로
        float rawSlip = Mathf.Clamp(slipR - slipL, -1f, 1f);

        // 6) 스무딩
        float a = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        _slipSmoothed = Mathf.Lerp(_slipSmoothed, rawSlip, a);

        // 7) 비주얼 오프셋 적용 (Handle_Visual만)
        Vector3 targetPos = _initLocalPos + new Vector3(_slipSmoothed * maxSlipX, 0f, 0f);

        // 비틀림은 슬립과 같은 방향으로(보기 좋게 부호는 취향대로 바꿔도 됨)
        Quaternion targetRot = _initLocalRot * Quaternion.Euler(0f, -_slipSmoothed * maxYawDeg, 0f);

        handleVisual.localPosition = Vector3.Lerp(handleVisual.localPosition, targetPos, a);
        handleVisual.localRotation = Quaternion.Slerp(handleVisual.localRotation, targetRot, a);
    }

    Vector3 GetControllerPosLocal(OVRInput.Controller c)
    {
        // OVRInput은 로컬 트래킹 좌표로 주는 경우가 많아서 trackingSpace 기준으로 맞추는 게 안전
        Vector3 p = OVRInput.GetLocalControllerPosition(c);

        if (trackingSpace) return p; // 이미 trackingSpace 로컬이라고 가정
        return p; // trackingSpace 못 구하면 그냥 로컬값 사용
    }
}
