using UnityEngine;

public class CartLoadBias : MonoBehaviour
{
    [Header("References")]
    public Transform cartPivot;          // CartPivot
    public Rigidbody loadRigidbody;      // 수박 Rigidbody

    [Header("Tuning")]
    public float halfWidth = 0.25f;      // 카트 중심에서 좌/우 판단 기준 폭(미터). 대략 손잡이 반쪽 길이/바구니 반폭
    public float biasSmoothing = 8f;     // 값 부드럽게(클수록 빠르게 반응)

    // -1 = 완전 왼쪽, +1 = 완전 오른쪽
    public float Bias { get; private set; }

    void Reset()
    {
        cartPivot = transform;
    }

    void FixedUpdate()
    {
        if (!cartPivot || !loadRigidbody) return;

        Vector3 comWorld = loadRigidbody.worldCenterOfMass;
        Vector3 comLocal = cartPivot.InverseTransformPoint(comWorld);

        // localX를 -1~+1로 정규화
        float raw = Mathf.Clamp(comLocal.x / Mathf.Max(halfWidth, 0.001f), -1f, 1f);

        // 스무딩
        Bias = Mathf.Lerp(Bias, raw, 1f - Mathf.Exp(-biasSmoothing * Time.fixedDeltaTime));
    }
}
