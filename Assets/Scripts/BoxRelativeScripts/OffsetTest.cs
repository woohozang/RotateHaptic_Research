using UnityEngine;
using Oculus.Interaction;

public class LeftHandOffsetTransformer: MonoBehaviour, ITransformer
{
    [Header("Base Transformer (GrabFreeTransformer 넣기)")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformer;
    private ITransformer _base;

    private IGrabbable _grabbable;

    [Header("Left offset (lag)")]
    [Tooltip("Pos가 따라오는 속도. 낮을수록 더 무겁고 더 늦게 따라옵니다.")]
    [Range(0.5f, 30f)]
    public float posFollow = 6f;

    [Tooltip("Rot이 따라오는 속도. 낮을수록 더 무겁고 더 늦게 따라옵니다.")]
    [Range(0.5f, 30f)]
    public float rotFollow = 6f;

    [Tooltip("너무 작은 흔들림/잡음에서 지터 방지용 데드존(미터)")]
    public float posDeadzone = 0.0003f;

    [Tooltip("너무 작은 회전 변화에서 지터 방지용 데드존(도)")]
    public float rotDeadzoneDeg = 0.15f;

    [Header("Allowed axes (카트 전용: Z 전진 + Y 회전)")]
    public bool allowMoveX = true;   // 필요 없으면 false
    public bool allowMoveY = false;  // 보통 false (높이 고정)
    public bool allowMoveZ = true;   // 전진축 +Z

    public bool allowRotX = false;
    public bool allowRotY = true;    // 회전축 Y만
    public bool allowRotZ = false;

    // 내부 상태
    private Vector3 _smoothedPos;
    private Quaternion _smoothedRot;

    private Vector3 _lastTargetPos;
    private Quaternion _lastTargetRot;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;

        _base = _baseTransformer as ITransformer;
        if (_base == null)
        {
            Debug.LogError($"{nameof(LeftHandOffsetTransformer)}: Base Transformer가 비어있거나 ITransformer가 아닙니다.");
            return;
        }

        _base.Initialize(grabbable);
    }

    public void BeginTransform()
    {
        if (_base == null || _grabbable == null) return;

        _base.BeginTransform();

        _smoothedPos = _grabbable.Transform.position;
        _smoothedRot = _grabbable.Transform.rotation;

        _lastTargetPos = _smoothedPos;
        _lastTargetRot = _smoothedRot;
    }

    public void UpdateTransform()
    {
        if (_base == null || _grabbable == null) return;

        // 1) 기본 GrabFreeTransformer가 목표 포즈 계산 (즉시)
        _base.UpdateTransform();

        Vector3 targetPos = _grabbable.Transform.position;
        Quaternion targetRot = _grabbable.Transform.rotation;

        // 2) 너무 미세한 변화는 무시(지터 방지)
        if ((targetPos - _lastTargetPos).sqrMagnitude < posDeadzone * posDeadzone)
            targetPos = _lastTargetPos;

        if (Quaternion.Angle(targetRot, _lastTargetRot) < rotDeadzoneDeg)
            targetRot = _lastTargetRot;

        // 3) Lerp/Slerp로 "늦게 따라오기"
        float dt = Time.deltaTime;
        float aPos = 1f - Mathf.Exp(-posFollow * dt);
        float aRot = 1f - Mathf.Exp(-rotFollow * dt);

        _smoothedPos = Vector3.Lerp(_smoothedPos, targetPos, aPos);
        _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, aRot);

        // 4) 카트 제약: 전진 Z + (필요시 X)만 이동 / Y회전만 허용
        Vector3 finalPos = ApplyMoveAxisMask(_grabbable.Transform.position, _smoothedPos);
        Quaternion finalRot = ApplyRotAxisMask(_grabbable.Transform.rotation, _smoothedRot);

        // 5) 최종 적용
        _grabbable.Transform.position = finalPos;
        _grabbable.Transform.rotation = finalRot;

        _lastTargetPos = targetPos;
        _lastTargetRot = targetRot;
    }

    public void EndTransform()
    {
        if (_base == null) return;
        _base.EndTransform();
    }

    private Vector3 ApplyMoveAxisMask(Vector3 current, Vector3 desired)
    {
        // current(현재 값) 기준으로 원하는 축만 desired로 교체
        if (!allowMoveX) desired.x = current.x;
        if (!allowMoveY) desired.y = current.y;
        if (!allowMoveZ) desired.z = current.z;
        return desired;
    }

    private Quaternion ApplyRotAxisMask(Quaternion current, Quaternion desired)
    {
        // 회전은 Euler로 마스킹 (Y만 허용 같은 경우에 가장 간단)
        Vector3 curE = current.eulerAngles;
        Vector3 desE = desired.eulerAngles;

        if (!allowRotX) desE.x = curE.x;
        if (!allowRotY) desE.y = curE.y;
        if (!allowRotZ) desE.z = curE.z;

        return Quaternion.Euler(desE);
    }
}
