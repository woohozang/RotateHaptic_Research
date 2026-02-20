using UnityEngine;

public class ContainerParenting : MonoBehaviour
{
    // 사과가 상자 트리거에 들어왔을 때
    private void OnTriggerEnter(Collider other)
    {
        // 태그나 레이어로 사과인지 확인
        if (other.CompareTag("Apple"))
        {
            // 사과의 부모를 이 상자로 설정 (부모-자식 관계 형성)
            other.transform.SetParent(this.transform);

            // 물리 연산이 튀는 것을 방지하기 위해 속도 초기화 (선택 사항)
            Rigidbody appleRb = other.GetComponent<Rigidbody>();
            if (appleRb != null)
            {
                appleRb.velocity = Vector3.zero;
                appleRb.angularVelocity = Vector3.zero;
            }
        }
    }

    // 사과가 상자 트리거를 벗어났을 때
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Apple"))
        {
            // 부모 관계를 해제 (최상단 뎁스로 이동)
            other.transform.SetParent(null);
        }
    }
}