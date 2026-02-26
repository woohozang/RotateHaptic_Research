using UnityEngine;
using Oculus.Interaction;
using System.Collections.Generic;

public class CartWeightController : MonoBehaviour
{
    [Header("References")]
    public DynamicWeightTwoGrabPlaneTransformer Transformer;
    public Transform AppleContainer;

    [Header("Weight Step Settings")]
    public float BaseSpeed = 20f;      // [수정] 초기 속도 20
    public float DampingPerApple = 5f; // [수정] 사과 개당 감소량 5
    public float MinSpeedLimit = 1.5f; // 최소 한계치

    private HashSet<GameObject> _apples = new HashSet<GameObject>();

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Apple") && !_apples.Contains(other.gameObject))
        {
            _apples.Add(other.gameObject);

            // 1. 부모 설정 (AppleContainer)
            // worldPositionStays를 true로 유지하여 현재 위치에서 부모만 변경합니다.
            other.transform.SetParent(AppleContainer, true);

            // 2. [사용자 요청] 스케일값 강제 고정
            // 이미지상에서 확인된 최적의 스케일 수치를 소수점까지 정확히 입력합니다.
            other.transform.localScale = new Vector3(0.7f, 0.35f, 0.4f);

            // 3. [회전 문제 해결] 로컬 회전값을 (0, 0, 0)으로 즉시 리셋
            // 이 코드가 있어야 자식이 된 후 부모의 움직임에 따라 회전값이 튀는 것을 막습니다.
            other.transform.localRotation = Quaternion.identity;

            // 4. Meta SDK 상호작용 강제 종료
            // 손에서 놓는 순간 SDK가 회전을 보정하려는 시도를 완전히 차단합니다.
            var grabbable = other.GetComponent<Oculus.Interaction.Grabbable>();
            if (grabbable != null) grabbable.enabled = false;

            // 5. 물리 엔진 연산 차단
            Rigidbody rb = other.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;        // 이동 속도 제거
                rb.angularVelocity = Vector3.zero; // 회전 관성 완전 제거 (회전 방지 핵심)
            }

            UpdateWeight();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Apple") && _apples.Contains(other.gameObject))
        {
            _apples.Remove(other.gameObject);

            // 1. 자식 해제 (다시 독립 오브젝트로)
            other.transform.SetParent(null);

            // 2. 물리 연산 재개
            Rigidbody appleRb = other.GetComponent<Rigidbody>();
            Collider appleCl = other.GetComponent<Collider>();
            if (appleRb != null)
            {
                appleRb.isKinematic = false;
                appleRb.useGravity = true;
                appleCl.isTrigger = false;
            }

            UpdateWeight();
        }
    }

    private void UpdateWeight()
    {
        // 20 - (사과 개수 * 5)
        float targetSpeed = BaseSpeed - (_apples.Count * DampingPerApple);
        float finalSpeed = Mathf.Max(MinSpeedLimit, targetSpeed);

        if (Transformer != null)
        {
            // [중요] 오직 LeftPosFollow만 수정합니다.
            Transformer.LeftPosFollow = finalSpeed;

            // LeftYawFollow는 건드리지 않습니다. (1.5 유지)
        }

        Debug.Log($"[Weight] 사과 개수: {_apples.Count} | 현재 PosFollow: {finalSpeed} | YawFollow: {Transformer.LeftYawFollow}");
    }
}