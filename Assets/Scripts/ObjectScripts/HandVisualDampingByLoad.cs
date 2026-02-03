using UnityEngine;

public class HandVisualDampingByLoad : MonoBehaviour
{
    [Header("Bias")]
    public CartLoadBias biasSource; // -1(left) ~ +1(right)

    [Header("True tracked anchors (do NOT modify)")]
    public Transform leftAnchor;    // LeftControllerAnchor
    public Transform rightAnchor;   // RightControllerAnchor

    [Header("Visual roots (modify these only)")]
    public Transform leftVisual;    // LeftVisualRoot (mesh parent)
    public Transform rightVisual;   // RightVisualRoot

    [Header("Follow speeds (higher = snappier)")]
    public float baseFollow = 25f;     // 기본 따라가기 속도
    public float slowAmount = 18f;     // 무거운 쪽이 느려지는 양
    public float strength = 1.0f;      // 편향 강도(0~1.5 추천)

    [Header("Optional")]
    public bool matchInitialOnStart = true;

    void Start()
    {
        if (matchInitialOnStart)
        {
            if (leftVisual && leftAnchor)
            {
                leftVisual.position = leftAnchor.position;
                leftVisual.rotation = leftAnchor.rotation;
            }
            if (rightVisual && rightAnchor)
            {
                rightVisual.position = rightAnchor.position;
                rightVisual.rotation = rightAnchor.rotation;
            }
        }
    }

    void LateUpdate()
    {
        if (!biasSource) return;

        float b = biasSource.Bias; // -1..+1
        float leftHeavy = Mathf.Clamp01(-b) * strength;
        float rightHeavy = Mathf.Clamp01(b) * strength;

        // 무거운 손은 follow 속도를 낮춰서 더 "댐핑" 느낌
        float leftFollow = Mathf.Max(2f, baseFollow - slowAmount * leftHeavy);
        float rightFollow = Mathf.Max(2f, baseFollow - slowAmount * rightHeavy);

        FollowVisual(leftVisual, leftAnchor, leftFollow);
        FollowVisual(rightVisual, rightAnchor, rightFollow);
    }

    void FollowVisual(Transform visual, Transform target, float follow)
    {
        if (!visual || !target) return;

        float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
        visual.position = Vector3.Lerp(visual.position, target.position, a);
        visual.rotation = Quaternion.Slerp(visual.rotation, target.rotation, a);
    }
}
