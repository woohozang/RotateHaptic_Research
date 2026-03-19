using UnityEngine;
using UnityEngine.Splines;

public class SplineColliderBuilder : MonoBehaviour
{
    public SplineContainer spline;
    public int segments = 30;
    public float width = 3f;

    void Start()
    {
        for (int i = 0; i < segments; i++)
        {
            float t1 = (float)i / segments;
            float t2 = (float)(i + 1) / segments;

            Vector3 p1 = spline.EvaluatePosition(t1);
            Vector3 p2 = spline.EvaluatePosition(t2);

            Vector3 mid = (p1 + p2) / 2f;
            Vector3 dir = (p2 - p1);

            GameObject col = new GameObject("Col_" + i);
            col.transform.parent = transform;
            col.transform.position = mid;
            col.transform.rotation = Quaternion.LookRotation(dir);

            BoxCollider bc = col.AddComponent<BoxCollider>();
            bc.size = new Vector3(width, 0.1f, dir.magnitude);
        }
    }
}