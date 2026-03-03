using UnityEngine;
using Oculus.Interaction;

public class BoxSnapManager : MonoBehaviour
{
    [Header("Anchors")]
    public Transform LeftAnchor;
    public Transform RightAnchor;

    [Header("References")]
    public Grabbable grabbable;
    // [추가] 트랜스포머 참조 추가
    public DynamicWeightTwoGrabPlaneTransformer transformer;

    private Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        grabbable.WhenPointerEventRaised += HandlePointerEvent;
    }

    private void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Unselect)
        {
            SnapToClosestAnchor();
        }
    }

    private void SnapToClosestAnchor()
    {
        float distToLeft = Vector3.Distance(transform.position, LeftAnchor.position);
        float distToRight = Vector3.Distance(transform.position, RightAnchor.position);

        Transform targetAnchor = (distToLeft < distToRight) ? LeftAnchor : RightAnchor;

        // 1. 물리 정지 및 부모 설정
        _rb.isKinematic = true;
        _rb.velocity = Vector3.zero;
        transform.SetParent(targetAnchor, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // 2. [핵심] 트랜스포머의 타겟 손 방향을 실시간으로 변경
        if (transformer != null)
        {
            if (targetAnchor == LeftAnchor)
            {
                transformer.CurrentTargetSide = DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Left;
            }
            else
            {
                transformer.CurrentTargetSide = DynamicWeightTwoGrabPlaneTransformer.TargetLagSide.Right;
            }
            Debug.Log($"[SnapManager] 무게 중심이 {transformer.CurrentTargetSide}로 변경되었습니다.");
        }
    }

    private void OnDestroy()
    {
        grabbable.WhenPointerEventRaised -= HandlePointerEvent;
    }
}