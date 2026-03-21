using UnityEngine;
using Oculus.Interaction;

public class DCartFollower : MonoBehaviour
{
    [Header("References")]
    public Transform cart;       // 쇼핑카트의 Transform
    public Grabbable grabbable;  // 핸들의 Grabbable 스크립트

    [Header("Settings")]
    public float moveSpeed = 5f;      // 회전 시 물체가 밀려나는 속도
    public float maxOffset = 0.3f;    // 카트 내부 가로 폭 제한
    public float returnSpeed = 3f;    // 중앙으로 돌아오는 속도
    public float inputThreshold = 0.02f;

    private Quaternion prevLeftRot;
    private Quaternion prevRightRot;
    private bool initialized = false;
    private float currentOffsetX = 0f; // 로컬 X축 오프셋 값

    void Start()
    {
        // 물리 연산이 위치 계산을 방해하지 않도록 설정
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void Update()
    {
        if (cart == null || grabbable == null) return;

        // 양손을 잡고 있지 않을 때
        if (grabbable.GrabPoints.Count < 2)
        {
            //ReturnToCenter();
            initialized = false;
            ApplyPosition();
            return;
        }

        var left = grabbable.GrabPoints[0];
        var right = grabbable.GrabPoints[1];

        if (!initialized)
        {
            prevLeftRot = left.rotation;
            prevRightRot = right.rotation;
            initialized = true;
            return;
        }

        // 양손의 각속도 계산 (Y축 회전)
        float leftAV = GetAngularVelocityY(prevLeftRot, left.rotation);
        float rightAV = GetAngularVelocityY(prevRightRot, right.rotation);

        prevLeftRot = left.rotation;
        prevRightRot = right.rotation;

        // 핸들을 돌리는 방향 결정 (평균값 사용)
        float avgAV = (leftAV + rightAV) * 0.5f;

        // 🔥 핵심 로직: 왼쪽 회전(양수) 시 물체는 왼쪽(-X)으로 이동해야 함
        if (Mathf.Abs(avgAV) > inputThreshold)
        {
            // 회전 반대 방향으로 물체를 밀어냄
            currentOffsetX -= avgAV * moveSpeed * Time.deltaTime;
        }
        else
        {
            //ReturnToCenter();
        }

        // 오프셋 제한 (카트 벽을 뚫지 않게)
        currentOffsetX = Mathf.Clamp(currentOffsetX, -maxOffset, maxOffset);

        ApplyPosition();
    }

    float GetAngularVelocityY(Quaternion prev, Quaternion current)
    {
        Quaternion delta = current * Quaternion.Inverse(prev);
        delta.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;
        return (angle * axis.y) / Time.deltaTime;
    }

    /*void ReturnToCenter()
    {
        // 입력이 없을 때 서서히 중앙(0)으로 복귀
        currentOffsetX = Mathf.Lerp(currentOffsetX, 0f, returnSpeed * Time.deltaTime);
    }*/

    // 🔥 핵심 수정 사항: 카트의 방향에 따른 위치 적용
    void ApplyPosition()
    {
        // 1. 카트의 로컬 좌표를 설정 (X만 변하고 Y, Z는 카트 중심 기준 고정)
        // 만약 물체의 높이를 유지하고 싶다면 0 대신 transform.localPosition.y를 사용하세요.
        Vector3 localTargetPos = new Vector3(currentOffsetX, 0.6f, 0f);

        // 2. 카트의 World 좌표와 Rotation을 기준으로 로컬 좌표를 월드 좌표로 변환
        transform.position = cart.TransformPoint(localTargetPos);

        // 3. 물체의 회전도 카트와 일치시킴 (필요 시)
        transform.rotation = cart.rotation;
    }
}