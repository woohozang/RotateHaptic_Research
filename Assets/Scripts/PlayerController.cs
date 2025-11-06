using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("플레이어의 이동 속도입니다.")]
    public float moveSpeed = 2.0f;

    [Header("Rotation Settings")]
    [Tooltip("회전 방식을 선택합니다. (Smooth: 부드럽게, Snap: 끊어서)")]
    public RotationType rotationType = RotationType.Snap;

    [Tooltip("Smooth Turn 시 회전 속도입니다.")]
    public float rotationSpeed = 60.0f;

    [Tooltip("Snap Turn 시 한 번에 회전할 각도입니다.")]
    public float snapTurnAngle = 45.0f;

    // Snap Turn을 위한 변수 (연속 입력을 방지)
    private bool _isReadyToSnapTurn = true;

    // 회전 방식을 선택하기 위한 Enum
    public enum RotationType
    {
        Smooth,
        Snap
    }

    void Update()
    {
        HandleMovement();
        HandleRotation();
    }

    private void HandleMovement()
    {
        // 왼쪽 컨트롤러 조이스틱 입력 받기 (Primary: 주로 왼손)
        Vector2 moveInput = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);

        // OVRCameraRig의 전방 및 우측 방향을 기준으로 이동 방향 계산
        // 카메라(머리)가 아닌 Rig 본체를 기준으로 해야 멀미가 덜합니다.
        Vector3 forward = transform.forward * moveInput.y;
        Vector3 right = transform.right * moveInput.x;

        // 이동 방향을 정규화하여 대각선 이동이 더 빨라지는 것을 방지
        Vector3 moveDirection = (forward + right).normalized;

        // 이동 적용 (Time.deltaTime을 곱해 프레임 속도에 관계없이 일정한 속도 유지)
        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }

    private void HandleRotation()
    {
        // 오른쪽 컨트롤러 조이스틱 입력 받기 (Secondary: 주로 오른손)
        Vector2 rotationInput = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        switch (rotationType)
        {
            case RotationType.Smooth:
                // Smooth Turn: 입력 값에 따라 부드럽게 회전
                transform.Rotate(0, rotationInput.x * rotationSpeed * Time.deltaTime, 0);
                break;

            case RotationType.Snap:
                // Snap Turn: 조이스틱을 끝까지 밀었을 때만 한 번 회전
                if (Mathf.Abs(rotationInput.x) > 0.8f && _isReadyToSnapTurn)
                {
                    // 오른쪽으로 밀면 양수, 왼쪽으로 밀면 음수 각도로 회전
                    float angle = snapTurnAngle * Mathf.Sign(rotationInput.x);
                    transform.Rotate(0, angle, 0);
                    _isReadyToSnapTurn = false; // 연속 회전 방지
                }
                // 조이스틱이 중앙으로 돌아오면 다시 회전 준비
                else if (Mathf.Abs(rotationInput.x) < 0.2f)
                {
                    _isReadyToSnapTurn = true;
                }
                break;
        }
    }
}