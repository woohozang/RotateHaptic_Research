using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CargoInertiaDamper : MonoBehaviour
{
    [Header("Refs")]
    public Rigidbody cartRb;              // 카트 루트 Rigidbody
    public Transform cartSpace;           // 카트 기준 Transform(없으면 cartRb.transform)

    [Header("Heavy Feel")]
    public float followDrag = 6f;         // 카트 기준 상대속도 감쇠(클수록 무겁고 덜 굴러다님)
    public float maxDampForce = 80f;       // 감쇠력 상한
    public float slipThreshold = 0.15f;    // 이 이상 상대속도일 때만 감쇠 적용(미세 떨림 방지)

    [Header("Optional Downforce")]
    public float extraDownForce = 0f;      // 바닥에 더 눌러붙는 느낌(0~)

    Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (cartSpace == null && cartRb != null) cartSpace = cartRb.transform;
    }

    void FixedUpdate()
    {
        if (cartRb == null) return;

        // 수박의 카트 기준 상대 속도(=수박 - 카트)
        Vector3 relVel = _rb.velocity - cartRb.velocity;

        // 카트 로컬로 보고 좌/우/전후 감쇠를 안정적으로 제어할 수도 있음
        Vector3 relVelLocal = cartSpace != null ? cartSpace.InverseTransformDirection(relVel) : relVel;

        // 미세 떨림 제거
        if (relVelLocal.magnitude < slipThreshold) return;

        // 상대속도 반대 방향으로 감쇠력
        Vector3 dampForceWorld = -(cartSpace != null
            ? cartSpace.TransformDirection(relVelLocal.normalized)
            : relVel.normalized) * Mathf.Min(maxDampForce, relVelLocal.magnitude * followDrag);

        _rb.AddForce(dampForceWorld, ForceMode.Force);

        // 선택: 살짝 눌러붙게(점프/튕김 감소)
        if (extraDownForce > 0f)
            _rb.AddForce(Vector3.down * extraDownForce, ForceMode.Force);
    }
}
