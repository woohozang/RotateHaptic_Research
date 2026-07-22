using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SpringVisual : MonoBehaviour
{
    public Transform startPoint;
    public Transform endPoint;

    public int coilCount = 8;
    public int pointsPerCoil = 16;
    public float radius = 0.04f;

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.012f;
    }

    void Update()
    {
        DrawSpring();
    }

    void DrawSpring()
    {
        if (startPoint == null || endPoint == null) return;

        int pointCount = coilCount * pointsPerCoil + 1;
        line.positionCount = pointCount;

        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;
        Vector3 axis = end - start;

        float length = axis.magnitude;
        if (length <= 0.001f) return;

        Vector3 forward = axis.normalized;

        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.magnitude < 0.001f)
            right = Vector3.Cross(forward, Vector3.right);

        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward).normalized;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float angle = t * coilCount * Mathf.PI * 2f;

            Vector3 center = Vector3.Lerp(start, end, t);
            Vector3 offset =
                Mathf.Cos(angle) * radius * right +
                Mathf.Sin(angle) * radius * up;

            line.SetPosition(i, center + offset);
        }
    }
}