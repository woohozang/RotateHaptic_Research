using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// TwoGrabPlaneTransformer의 결과를 받아와서 
/// 위치와 회전 모두에 감쇠(Damping)를 적용하는 래퍼 트랜스포머입니다.
/// </summary>
public class WeightRotateDamping : MonoBehaviour, ITransformer
{
    [Header("Base Transformer")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformer;
    private ITransformer _base;

    private IGrabbable _grabbable;

    [Header("무게감 설정 (감쇠 속도)")]
    [Tooltip("값이 작을수록 카트가 더 무겁게 느껴집니다 (추천: 1~5)")]
    public float posLerpSpeed = 2.0f;
    public float rotLerpSpeed = 1.5f;

    // 현재 부드럽게 이동 중인 상태를 저장
    private Vector3 _smoothedPos;
    private Quaternion _smoothedRot;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
        _base = _baseTransformer as ITransformer;
        _base.Initialize(grabbable);

        // 초기화 시 현재 위치/회전 저장
        _smoothedPos = _grabbable.Transform.position;
        _smoothedRot = _grabbable.Transform.rotation;
    }

    public void BeginTransform()
    {
        _base.BeginTransform();

        // 잡는 순간의 위치/회전으로 동기화하여 튀는 현상 방지
        _smoothedPos = _grabbable.Transform.position;
        _smoothedRot = _grabbable.Transform.rotation;
    }

    public void UpdateTransform()
    {
        // 1. 원본 Transformer(TwoGrabPlaneTransformer)가 
        //    "손의 움직임대로라면 가야 할 위치"를 먼저 계산하게 합니다.
        _base.UpdateTransform();

        // 2. 계산 직후의 값(Target)을 가져옵니다.
        Vector3 targetPos = _grabbable.Transform.position;
        Quaternion targetRot = _grabbable.Transform.rotation;

        // 3. [위치 보간] 카트가 위치를 따라가는 속도 조절
        _smoothedPos = Vector3.Lerp(
            _smoothedPos,
            targetPos,
            posLerpSpeed * Time.deltaTime
        );

        // 4. [회전 보간] 카트가 회전을 따라가는 속도 조절
        _smoothedRot = Quaternion.Slerp(
            _smoothedRot,
            targetRot,
            rotLerpSpeed * Time.deltaTime
        );

        // 5. 최종 결과물을 다시 Transform에 적용
        // 위치와 회전을 동시에 업데이트해야 축이 어긋나지 않습니다.
        _grabbable.Transform.position = _smoothedPos;
        _grabbable.Transform.rotation = _smoothedRot;
    }

    public void EndTransform()
    {
        _base.EndTransform();
    }
}