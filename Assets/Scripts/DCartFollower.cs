using UnityEngine;
using Oculus.Interaction;

public class DCartFollower : MonoBehaviour
{
    [Header("References")]
    public Transform cart;
    public Grabbable grabbable;

    [Header("Settings")]
    public float moveForce = 5f;
    public float maxOffset = 0.3f;
    public float damping = 0.9f;
    public float returnForce = 2f;
    public float inputThreshold = 0.05f;

    private Rigidbody rb;

    // 🔥 이전 회전 저장
    private Quaternion prevLeftRot;
    private Quaternion prevRightRot;

    private bool initialized = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.constraints = RigidbodyConstraints.FreezePositionZ;
        rb.constraints = RigidbodyConstraints.FreezePositionY;
    }

    void FixedUpdate()
    {
        if (cart == null || grabbable == null) return;

        // 🔥 양손 잡고 있는지 확인
        if (grabbable.GrabPoints.Count < 2)
        {
            ApplyReturn();
            initialized = false;
            return;
        }

        var left = grabbable.GrabPoints[0];
        var right = grabbable.GrabPoints[1];

        // 🔥 첫 프레임 초기화
        if (!initialized)
        {
            prevLeftRot = left.rotation;
            prevRightRot = right.rotation;
            initialized = true;
            return;
        }

        // =========================
        // 🔥 각속도 계산 (핵심)
        // =========================

        float leftAV = GetAngularVelocityY(prevLeftRot, left.rotation);
        float rightAV = GetAngularVelocityY(prevRightRot, right.rotation);

        prevLeftRot = left.rotation;
        prevRightRot = right.rotation;

        float input = rightAV - leftAV;

        // 🔥 입력 없으면 복귀
        if (Mathf.Abs(input) < inputThreshold)
        {
            ApplyReturn();
            return;
        }

        // 🔥 힘 적용
        Vector3 force = cart.right * input * moveForce;
        rb.AddForce(force, ForceMode.Acceleration);

        rb.velocity *= damping;

        ClampPosition();
    }

    // =========================
    // 🔥 각속도 계산 함수
    // =========================
    float GetAngularVelocityY(Quaternion prev, Quaternion current)
    {
        Quaternion delta = current * Quaternion.Inverse(prev);

        delta.ToAngleAxis(out float angle, out Vector3 axis);

        // 안정화
        if (angle > 180f) angle -= 360f;

        float angularVelocity = angle * axis.y / Time.fixedDeltaTime;

        return angularVelocity;
    }

    void ApplyReturn()
    {
        Vector3 localPos = cart.InverseTransformPoint(transform.position);

        float returnForceX = -localPos.x * returnForce;
        rb.AddForce(cart.right * returnForceX, ForceMode.Acceleration);

        rb.velocity *= damping;

        ClampPosition();
    }

    void ClampPosition()
    {
        Vector3 localPos = cart.InverseTransformPoint(transform.position);

        localPos.x = Mathf.Clamp(localPos.x, -maxOffset, maxOffset);

        Vector3 world = cart.TransformPoint(localPos);
        rb.MovePosition(world);
    }
}