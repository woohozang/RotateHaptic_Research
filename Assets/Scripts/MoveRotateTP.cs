using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveRotateTP : MonoBehaviour
{
    public Transform centerEyeAnchor; // OVRCameraRig의 CenterEyeAnchor
    public float speed = 1f; // 초당 이동 거리

    void Update()
    {
        if (centerEyeAnchor == null) return;

        // Y축 제거 (수평 이동만)
        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;
        forward.Normalize();

        transform.position += forward * speed * Time.deltaTime;
    }
}
