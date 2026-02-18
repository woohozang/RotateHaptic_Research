using UnityEngine;
using Oculus.Interaction;
using System.Collections.Generic;

public class CartWeightController : MonoBehaviour
{
    [Header("References")]
    public DynamicWeightTwoGrabPlaneTransformer Transformer;

    [Header("Weight Step Settings")]
    [Tooltip("사과가 0개일 때의 팔로우 속도 (높을수록 가벼움)")]
    public float EmptyFollowSpeed = 40f;
    [Tooltip("사과 1개당 감소시킬 속도 수치")]
    public float DampingPerApple = 3.5f;
    [Tooltip("최대로 무거워졌을 때의 한계치 (너무 낮으면 조작 불능)")]
    public float MaxHeavyLimit = 3f;

    private HashSet<GameObject> _apples = new HashSet<GameObject>();

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Apple") && !_apples.Contains(other.gameObject))
        {
            _apples.Add(other.gameObject);
            ApplyWeight();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Apple") && _apples.Contains(other.gameObject))
        {
            _apples.Remove(other.gameObject);
            ApplyWeight();
        }
    }

    private void ApplyWeight()
    {
        float newSpeed = Mathf.Max(MaxHeavyLimit, EmptyFollowSpeed - (_apples.Count * DampingPerApple));

        // 이 부분을 수정합니다.
        Transformer.LeftPosFollow = newSpeed;
        Transformer.LeftYawFollow = newSpeed;
    }
}