using UnityEngine;
using Oculus.Interaction;
using System.Collections.Generic;

public class CartWeightController : MonoBehaviour
{
    [Header("References")]
    public DynamicWeightTwoGrabPlaneTransformer Transformer;
    public Transform AppleContatiner;

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

            // 1. 박스의 자식으로 설정
            other.transform.SetParent(AppleContatiner, true);

            other.transform.localScale = Vector3.one;
            // 2. 물리 연산 중지 (뚫림 방지 핵심)
            Rigidbody appleRb = other.GetComponent<Rigidbody>();
            Collider appleCl = other.GetComponent<Collider>();
            if (appleRb != null)
            {
                appleRb.isKinematic = true; // 물리 엔진 대신 부모의 트랜스폼을 따름
                appleRb.useGravity = false;
                appleCl.isTrigger = true;
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