using UnityEngine;

public class ManualInertialPhysics : MonoBehaviour
{
    [Header("Inertia Settings")]
    [Tooltip("관성 강도: 높을수록 카트 움직임에 더 크게 반응함")]
    public float inertiaScale = 0.5f;
    [Tooltip("복원력: 구체가 원래 위치(중앙)로 돌아오려는 힘")]
    public float returnSpring = 2f;

    [Header("Shelf Limits")]
    public Vector2 shelfSize = new Vector2(0.4f, 0.6f); // 선반의 가로, 세로 반경

    [Header("Rolling Settings")]
    public float sphereRadius = 0.15f;

    private Vector3 _lastCartPos;
    private float _lastCartYaw;
    private Vector3 _localVel; // 구체의 로컬 속도
    

    void Start()
    {
        _lastCartPos = transform.parent.position;
        _lastCartYaw = transform.parent.eulerAngles.y;
       
    }

    void Update()
    {
        Transform cart = transform.parent;
        float dt = Time.deltaTime;

        // 1. 카트의 이동량 계산 (World Space -> Local Space 변환)
        Vector3 worldDelta = cart.position - _lastCartPos;
        Vector3 localDelta = cart.InverseTransformDirection(worldDelta);

        // 2. 카트의 회전량(원심력) 계산
        float currentYaw = cart.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(_lastCartYaw, currentYaw);

        // 3. 관성에 의한 로컬 속도 계산
        // 카트가 앞으로(+Z) 가면 구체 속도는 뒤로(-Z) 발생
        _localVel.z -= localDelta.z * inertiaScale / dt;
        _localVel.x -= localDelta.x * inertiaScale / dt;

        // 회전 시 원심력 (왼쪽 회전 시 오른쪽으로 밀림)
        _localVel.x += yawDelta * inertiaScale * 0.5f;

        // 4. 복원력 및 마찰 적용 (제자리에 멈추게 함)
        _localVel -= transform.localPosition * returnSpring * dt; // 중앙으로 복원
        _localVel *= 0.95f; // 감쇄(Damping)

        // 5. 위치 업데이트 및 제한(Clamp)
        Vector3 nextLocalPos = transform.localPosition + _localVel * dt;
        nextLocalPos.x = Mathf.Clamp(nextLocalPos.x, -shelfSize.x, shelfSize.x);
        nextLocalPos.z = Mathf.Clamp(nextLocalPos.z, -shelfSize.y, shelfSize.y);

        // 실제 이동 거리 계산
        Vector3 actualMove = nextLocalPos - transform.localPosition;
        transform.localPosition = nextLocalPos;

        // 6. 🔥 핵심: 이동 거리에 따른 회전 적용 (Rolling)
        // 전진/후진(Z) 이동 -> X축 회전
        float rollX = (actualMove.z / sphereRadius) * Mathf.Rad2Deg;
        // 좌우(X) 이동 -> Z축 회전
        float rollZ = -(actualMove.x / sphereRadius) * Mathf.Rad2Deg;

        transform.Rotate(Vector3.right, rollX, Space.Self);
        transform.Rotate(Vector3.forward, rollZ, Space.Self);

        // 데이터 갱신
        _lastCartPos = cart.position;
        _lastCartYaw = currentYaw;
    }
}