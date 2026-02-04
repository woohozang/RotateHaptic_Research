using UnityEngine;

public class CartTwoHandLagDriver : MonoBehaviour
{
    [Header("References")]
    public Transform cartRoot;          // 쇼핑카트 루트
    public Transform leftGrabPoint;     // 왼손 기준점(손잡이 왼쪽)
    public Transform rightGrabPoint;    // 오른손 기준점(손잡이 오른쪽)
    public Transform leftGripLag;       // 왼손 지연 포인트(빈 오브젝트)
    public Rigidbody cartRb;            // (선택) 루트 RB (Kinematic 권장)

    [Header("Motion")]
    public bool lockY = true;
    public bool yawOnly = true;
    public bool allowMoveX = true;
    public bool allowMoveZ = true;

    [Header("Left lag (smaller = heavier)")]
    [Range(1f, 40f)] public float leftPosFollow = 6f;   // 낮을수록 더 무거움
    [Range(1f, 40f)] public float leftRotFollow = 6f;   // (지연 포인트가 회전 필요 없으면 무시 가능)

    [Header("Grab state (set by events)")]
    public bool leftGrabbed;
    public bool rightGrabbed;

    float _lockedY;
    Vector3 _offsetXZ;   // 카트 중심 오프셋 유지(잡는 순간 튀지 않게)

    void Awake()
    {
        if (!cartRoot) cartRoot = transform;
        if (!cartRb) cartRb = cartRoot.GetComponent<Rigidbody>();
        _lockedY = cartRoot.position.y;

        if (leftGripLag && leftGrabPoint)
            leftGripLag.position = leftGrabPoint.position;
    }

    void Update()
    {
        // 왼손 지연 포인트 업데이트(잡고 있을 때만)
        if (leftGrabbed && leftGrabPoint && leftGripLag)
        {
            float a = 1f - Mathf.Exp(-leftPosFollow * Time.deltaTime);
            leftGripLag.position = Vector3.Lerp(leftGripLag.position, leftGrabPoint.position, a);
        }
        else if (leftGrabPoint && leftGripLag)
        {
            // 안 잡을 때는 즉시 복귀(드리프트 방지)
            leftGripLag.position = leftGrabPoint.position;
        }

        // 한 손/두 손 루트 구동
        if (leftGrabbed && rightGrabbed)
            DriveTwoHands();
        else if (leftGrabbed)
            DriveOneHand(leftGripLag ? leftGripLag.position : leftGrabPoint.position);
        else if (rightGrabbed)
            DriveOneHand(rightGrabPoint.position);
    }

    void DriveOneHand(Vector3 handPos)
    {
        Vector3 target = handPos + _offsetXZ;
        if (lockY) target.y = _lockedY;

        Vector3 cur = cartRoot.position;
        if (!allowMoveX) target.x = cur.x;
        if (!allowMoveZ) target.z = cur.z;

        ApplyMove(target, cartRoot.rotation);
    }

    void DriveTwoHands()
    {
        Vector3 l = leftGripLag ? leftGripLag.position : leftGrabPoint.position;
        Vector3 r = rightGrabPoint.position;

        // 위치: 중점
        Vector3 mid = (l + r) * 0.5f;
        Vector3 targetPos = mid + _offsetXZ;
        if (lockY) targetPos.y = _lockedY;

        Vector3 cur = cartRoot.position;
        if (!allowMoveX) targetPos.x = cur.x;
        if (!allowMoveZ) targetPos.z = cur.z;

        // 회전: 좌->우 벡터 기반 yaw
        Vector3 dir = r - l;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        Vector3 forward = Vector3.Cross(Vector3.up, dir).normalized;
        Quaternion targetRot = Quaternion.LookRotation(forward, Vector3.up);

        if (yawOnly)
        {
            float y = targetRot.eulerAngles.y;
            targetRot = Quaternion.Euler(0f, y, 0f);
        }

        ApplyMove(targetPos, targetRot);
    }

    void ApplyMove(Vector3 pos, Quaternion rot)
    {
        if (cartRb && cartRb.isKinematic)
        {
            cartRb.MovePosition(pos);
            cartRb.MoveRotation(rot);
        }
        else
        {
            cartRoot.position = pos;
            cartRoot.rotation = rot;
        }
    }

    // ---- 이벤트 연결용 ----
    public void LeftGrabBegin() { leftGrabbed = true; CaptureOffset(); }
    public void LeftGrabEnd() { leftGrabbed = false; if (!rightGrabbed) _offsetXZ = Vector3.zero; }

    public void RightGrabBegin() { rightGrabbed = true; CaptureOffset(); }
    public void RightGrabEnd() { rightGrabbed = false; if (!leftGrabbed) _offsetXZ = Vector3.zero; }

    void CaptureOffset()
    {
        _lockedY = cartRoot.position.y;

        Vector3 refPoint;
        if (leftGrabbed && rightGrabbed)
        {
            Vector3 l = leftGripLag ? leftGripLag.position : leftGrabPoint.position;
            Vector3 r = rightGrabPoint.position;
            refPoint = (l + r) * 0.5f;
        }
        else if (leftGrabbed)
            refPoint = leftGripLag ? leftGripLag.position : leftGrabPoint.position;
        else
            refPoint = rightGrabPoint.position;

        Vector3 off = cartRoot.position - refPoint;
        off.y = 0f;
        _offsetXZ = off;

        if (leftGripLag && leftGrabPoint)
            leftGripLag.position = leftGrabPoint.position;
    }
}
