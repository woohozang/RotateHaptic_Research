using UnityEngine;

public class DestinationSpot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("쇼핑카트 루트 오브젝트")]
    public Transform cartRoot;

    [Tooltip("색이 바뀔 Spot Renderer")]
    public Renderer spotRenderer;

    [Header("Arrival Settings")]
    [Tooltip("도착 판정 반경")]
    public float arriveRadius = 1.0f;

    [Tooltip("Y축 높이는 무시하고 XZ 평면 거리만 계산")]
    public bool ignoreY = true;

    [Header("Colors")]
    public Color waitingColor = Color.red;
    public Color arrivedColor = Color.green;

    [Header("State")]
    public bool isArrived = false;

    private Material _runtimeMaterial;

    private void Awake()
    {
        if (spotRenderer == null)
            spotRenderer = GetComponentInChildren<Renderer>();

        if (spotRenderer != null)
        {
            // 공유 Material을 직접 바꾸지 않기 위해 런타임 Material 생성
            _runtimeMaterial = spotRenderer.material;
            SetSpotColor(waitingColor);
        }
    }

    private void Update()
    {
        if (cartRoot == null) return;

        float distance = GetHorizontalDistance(cartRoot.position, transform.position);

        bool arrivedNow = distance <= arriveRadius;

        if (arrivedNow != isArrived)
        {
            isArrived = arrivedNow;
            SetSpotColor(isArrived ? arrivedColor : waitingColor);
        }
    }

    private float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        if (ignoreY)
        {
            a.y = 0f;
            b.y = 0f;
        }

        return Vector3.Distance(a, b);
    }

    private void SetSpotColor(Color color)
    {
        if (_runtimeMaterial == null) return;

        _runtimeMaterial.color = color;

        // URP/Lit에서 Emission을 쓰고 싶을 때도 대비
        if (_runtimeMaterial.HasProperty("_EmissionColor"))
        {
            _runtimeMaterial.EnableKeyword("_EMISSION");
            _runtimeMaterial.SetColor("_EmissionColor", color * 1.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isArrived ? Color.green : Color.red;

        Vector3 center = transform.position;
        center.y += 0.05f;

        Gizmos.DrawWireSphere(center, arriveRadius);
    }
}