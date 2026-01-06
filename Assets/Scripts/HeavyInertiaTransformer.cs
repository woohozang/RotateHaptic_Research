using UnityEngine;
using Oculus.Interaction;

public class HeavyInertiaTransformer : MonoBehaviour, ITransformer
{
    [Header("Base Transformer")]
    [SerializeField, Interface(typeof(ITransformer))]
    private UnityEngine.Object _baseTransformer;
    private ITransformer _base;

    private IGrabbable _grabbable;
    private Rigidbody _rigidbody;

    [Header("무게감 및 관성 설정")]
    public float posLerpSpeed = 5.0f; // 잡고 있을 때 따라오는 속도
    public float rotLerpSpeed = 4.0f;

    [Range(0, 10)]
    public float brakingDistance = 1.0f; // 제동 거리 조절 (Drag 값으로 활용)

    private Vector3 _smoothedPos;
    private Quaternion _smoothedRot;

    // 속도 계산용 변수
    private Vector3 _lastPos;
    private Quaternion _lastRot;
    private Vector3 _currentVelocity;
    private Vector3 _currentAngularVelocity;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
        _base = _baseTransformer as ITransformer;
        _base.Initialize(grabbable);

        _rigidbody = grabbable.Transform.GetComponent<Rigidbody>();

        // Rigidbody 설정 초기화
        if (_rigidbody != null)
        {
            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = true; // 잡고 있을 때는 물리 연산 정지
        }
    }

    public void BeginTransform()
    {
        _base.BeginTransform();
        if (_rigidbody != null) _rigidbody.isKinematic = true;

        _smoothedPos = _grabbable.Transform.position;
        _smoothedRot = _grabbable.Transform.rotation;
        _lastPos = _smoothedPos;
        _lastRot = _smoothedRot;
    }

    public void UpdateTransform()
    {
        _base.UpdateTransform();

        // 1. 목표 위치/회전 계산 (이전과 동일)
        Vector3 targetPos = _grabbable.Transform.position;
        Quaternion targetRot = _grabbable.Transform.rotation;

        _smoothedPos = Vector3.Lerp(_smoothedPos, targetPos, posLerpSpeed * Time.deltaTime);
        _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, rotLerpSpeed * Time.deltaTime);

        // 2. 현재 프레임의 속도 계산 (이동 거리 / 시간)
        if (Time.deltaTime > 0)
        {
            _currentVelocity = (_smoothedPos - _lastPos) / Time.deltaTime;

            // 회전 속도 계산
            Quaternion deltaRot = _smoothedRot * Quaternion.Inverse(_lastRot);
            deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
            _currentAngularVelocity = (axis * angle * Mathf.Deg2Rad) / Time.deltaTime;
        }

        _lastPos = _smoothedPos;
        _lastRot = _smoothedRot;

        // 3. 실제 적용
        _grabbable.Transform.position = _smoothedPos;
        _grabbable.Transform.rotation = _smoothedRot;
    }

    public void EndTransform()
    {
        _base.EndTransform();

        if (_rigidbody != null)
        {
            // 1. 물리 시뮬레이션 활성화
            _rigidbody.isKinematic = false;

            // 2. 계산된 속도와 회전력을 Rigidbody에 전달
            _rigidbody.velocity = _currentVelocity;
            _rigidbody.angularVelocity = _currentAngularVelocity;

            // 3. 제동 거리 설정 (Drag가 높을수록 빨리 멈춤)
            _rigidbody.drag = brakingDistance;
            _rigidbody.angularDrag = brakingDistance * 1.5f;
        }
    }
}