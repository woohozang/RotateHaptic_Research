using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityInertialRoll : MonoBehaviour
{
    [Header("Inertia Settings")]
    public float inertiaForce = 15f; // 1은 너무 낮을 수 있으니 10~15 정도로 높여보세요!
    public float rollDamping = 0.95f;
    public float sphereRadius = 0.15f;

    [Header("Detach Settings")]
    public string shelfTag = "Shelf";

    private Rigidbody _rb;
    private Transform _cart;
    private Vector3 _lastCartPos;
    private float _lastCartYaw;
    private bool _isDetached = false;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity = true;

        _cart = transform.parent;
        if (_cart != null)
        {
            _lastCartPos = _cart.position;
            _lastCartYaw = _cart.eulerAngles.y;
        }
    }

    void FixedUpdate()
    {
        if (_isDetached || _cart == null) return;

        float dt = Time.fixedDeltaTime;

        // 1. 카트 변화량 계산
        Vector3 worldDelta = _cart.position - _lastCartPos;
        Vector3 localDelta = _cart.InverseTransformDirection(worldDelta);
        float currentYaw = _cart.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(_lastCartYaw, currentYaw);

        // 2. 관성력 적용
        Vector3 pushDirection = -_cart.forward * localDelta.z + -_cart.right * localDelta.x;
        _rb.AddForce(pushDirection * inertiaForce / dt, ForceMode.Acceleration);

        Vector3 centrifugalDir = _cart.right * yawDelta;
        _rb.AddForce(centrifugalDir * inertiaForce * 0.2f, ForceMode.Acceleration);

        // 3. 수동 회전 제어
        Vector3 relativeVel = _rb.velocity;
        float vZ = Vector3.Dot(relativeVel, _cart.forward);
        float vX = Vector3.Dot(relativeVel, _cart.right);

        transform.Rotate(Vector3.right, (vZ / sphereRadius) * Mathf.Rad2Deg * dt * 5f, Space.Self);
        transform.Rotate(Vector3.forward, -(vX / sphereRadius) * Mathf.Rad2Deg * dt * 5f, Space.Self);

        _lastCartPos = _cart.position;
        _lastCartYaw = currentYaw;
    }

    // 🔥 이 부분이 반드시 포함되어야 합니다!
    private void OnCollisionExit(Collision collision)
    {
        if (_isDetached) return;

        // 부딪혔던 물체의 태그가 설정한 태그와 같은지 확인
        if (collision.gameObject.CompareTag(shelfTag))
        {
            _isDetached = true;
            transform.SetParent(null); // 부모 관계 해제

            // 물리 설정 초기화
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.None;

            Debug.Log($"[실험] {gameObject.name}이 {shelfTag}에서 떨어졌습니다!");
        }
    }
}