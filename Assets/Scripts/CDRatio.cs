using UnityEngine;
using Oculus.Interaction;

public class CDRatio : MonoBehaviour, ITransformer
{
    [Header("Base Transformer")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformer;
    private ITransformer _base;

    private IGrabbable _grabbable;

    [Header("C/D Ratio (무게감 설정)")]
    [Tooltip("1:0.6 비율 적용 (0.6이면 사용자가 10도 돌릴 때 6도만 회전)")]
    [Range(0.1f, 1.0f)]
    public float rotationCDRatio = 0.6f;

    [Tooltip("회전이 따라오는 부드러움 (값이 높을수록 즉각적)")]
    public float rotationSmoothing = 10f;

    private Quaternion _virtualRotation; // 실제 물체가 가질 '무거운' 회전값
    private Quaternion _lastBaseRotation; // 베이스 트랜스포머의 이전 프레임 회전값

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
        _base = _baseTransformer as ITransformer;
        _base.Initialize(grabbable);
    }

    public void BeginTransform()
    {
        _base.BeginTransform();

        // 시작 시점의 회전값을 동기화
        _virtualRotation = _grabbable.Transform.rotation;
        _lastBaseRotation = _grabbable.Transform.rotation;
    }

    public void UpdateTransform()
    {
        // 1. 기존의 잘 작동하던 베이스 로직 수행 (위치 이동 등)
        _base.UpdateTransform();

        // 2. 베이스 트랜스포머가 계산한 '이번 프레임의 원본 회전' 가져오기
        Quaternion currentBaseRot = _grabbable.Transform.rotation;

        // 3. [핵심] 이번 프레임에 손이 움직인 '회전 변화량(Delta)'만 추출
        // Delta = Current * Inverse(Last)
        Quaternion deltaRotation = currentBaseRot * Quaternion.Inverse(_lastBaseRotation);

        // 4. 변화량에 C/D Ratio(0.6) 적용
        // Quaternion.Slerp를 사용하여 회전각 자체를 줄임
        Quaternion scaledDelta = Quaternion.Slerp(Quaternion.identity, deltaRotation, rotationCDRatio);

        // 5. 줄어든 변화량을 가상 회전값에 누적
        Quaternion targetVirtualRot = scaledDelta * _virtualRotation;

        // 6. 부드러운 관성을 위해 Slerp로 최종 적용
        _virtualRotation = Quaternion.Slerp(_virtualRotation, targetVirtualRot, rotationSmoothing * Time.deltaTime);

        // 7. 결과를 트랜스폼에 최종 반영
        _grabbable.Transform.rotation = _virtualRotation;

        // 다음 프레임을 위해 원본 회전값 저장
        _lastBaseRotation = currentBaseRot;
    }

    public void EndTransform()
    {
        _base.EndTransform();
    }
}