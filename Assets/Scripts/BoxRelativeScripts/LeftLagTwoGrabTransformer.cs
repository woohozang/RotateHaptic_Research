using UnityEngine;
using Oculus.Interaction;

public class LeftLagTwoGrabTransformer : MonoBehaviour, ITransformer
{
    [Header("Base Two-Hand Transformer")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformerObj;
    private ITransformer _baseTransformer;

    [Header("Lag (Left Hand Heavy Feel)")]
    [Tooltip("값이 낮을수록 더 무겁게(느리게) 따라갑니다.")]
    [Range(1f, 40f)]
    public float posFollow = 6f;

    [Range(1f, 40f)]
    public float rotFollow = 6f;

    [Header("Axis Constraints")]
    public bool lockY = true;      // 높이 고정
    public bool yawOnly = true;    // Y 회전만

    private IGrabbable _grabbable;
    private Vector3 _smoothedPos;
    private Quaternion _smoothedRot;
    private float _lockedY;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
        _baseTransformer = _baseTransformerObj as ITransformer;
        if (_baseTransformer == null)
        {
            Debug.LogError($"{nameof(LeftLagTwoGrabTransformer)}: Base Transformer is missing.");
            return;
        }
        _baseTransformer.Initialize(grabbable);
    }

    public void BeginTransform()
    {
        _baseTransformer.BeginTransform();

        _smoothedPos = _grabbable.Transform.position;
        _smoothedRot = _grabbable.Transform.rotation;
        _lockedY = _smoothedPos.y;
    }

    public void UpdateTransform()
    {
        // 1) base(two hand) 계산 먼저 수행 (targetTransform에 바로 반영됨)
        _baseTransformer.UpdateTransform();

        // 2) base가 만들어낸 결과를 읽어서 "지연"을 적용
        Vector3 targetPos = _grabbable.Transform.position;
        Quaternion targetRot = _grabbable.Transform.rotation;

        // 3) 선택(잡힘) 중에 왼손이 포함되었는지 체크
        bool leftInvolved = IsLeftHandInvolved(_grabbable);

        if (!leftInvolved)
        {
            // 오른손만 잡고 있다면 즉시 1:1
            _smoothedPos = targetPos;
            _smoothedRot = targetRot;
        }
        else
        {
            float aPos = 1f - Mathf.Exp(-posFollow * Time.deltaTime);
            float aRot = 1f - Mathf.Exp(-rotFollow * Time.deltaTime);

            _smoothedPos = Vector3.Lerp(_smoothedPos, targetPos, aPos);
            _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, aRot);
        }

        // 4) 축 제약 적용 (전진 Z, 회전 Y만 같은 느낌)
        if (lockY)
        {
            _smoothedPos.y = _lockedY;
        }

        if (yawOnly)
        {
            Vector3 e = _smoothedRot.eulerAngles;
            _smoothedRot = Quaternion.Euler(0f, e.y, 0f);
        }

        // 5) 최종 적용
        _grabbable.Transform.position = _smoothedPos;
        _grabbable.Transform.rotation = _smoothedRot;
    }

    public void EndTransform()
    {
        _baseTransformer.EndTransform();
    }

    /// <summary>
    /// 메타 XR Interaction SDK에서 현재 선택 중인 인터랙터들 중 왼손이 있는지 검사
    /// (버전마다 프로퍼티명이 달라서, Reflection으로 안전하게 접근)
    /// </summary>
    private bool IsLeftHandInvolved(IGrabbable grabbable)
    {
        // IInteractableView로 캐스팅
        var view = grabbable as IInteractableView;
        if (view == null) return false;

        // 1) InteractorsSelecting 또는 SelectingInteractors 중 존재하는 걸 찾아서 열거
        System.Collections.IEnumerable enumerable = null;

        var type = view.GetType();

        var prop1 = type.GetProperty("InteractorsSelecting");
        if (prop1 != null)
            enumerable = prop1.GetValue(view) as System.Collections.IEnumerable;

        if (enumerable == null)
        {
            var prop2 = type.GetProperty("SelectingInteractors");
            if (prop2 != null)
                enumerable = prop2.GetValue(view) as System.Collections.IEnumerable;
        }

        if (enumerable == null) return false;

        foreach (var it in enumerable)
        {
            if (it == null) continue;

            // Interactor를 MonoBehaviour로 캐스팅해서 이름/태그 기반 판별
            var mb = it as MonoBehaviour;
            if (mb == null) continue;

            string n = mb.name.ToLower();
            // 프로젝트에 따라 LeftHandAnchor / LeftController / left 등으로 들어감
            if (n.Contains("left"))
                return true;
        }

        return false;
    }
}
