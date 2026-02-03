using UnityEngine;

public class StaticMarkerBias : MonoBehaviour
{
    public Transform cartPivot;     // CartPivot
    public Transform marker;        // LoadCube_Left

    [Header("Tuning")]
    public float halfWidth = 0.25f; // 좌/우 정규화 폭(카트 반폭 정도)
    public float smooth = 10f;

    // -1 = 완전 왼쪽, +1 = 완전 오른쪽
    public float Bias { get; private set; }

    void FixedUpdate()
    {
        if (!cartPivot || !marker) return;

        Vector3 local = cartPivot.InverseTransformPoint(marker.position);
        float raw = Mathf.Clamp(local.x / Mathf.Max(halfWidth, 0.001f), -1f, 1f);

        // 부드럽게
        Bias = Mathf.Lerp(Bias, raw, 1f - Mathf.Exp(-smooth * Time.fixedDeltaTime));
    }
}
