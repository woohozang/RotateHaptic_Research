using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.HandGrab;   // HandGrabInteractable
using Oculus.Interaction;            // IInteractor 등

[RequireComponent(typeof(Rigidbody))]
public class CartPhysics_Interaction : MonoBehaviour
{
    [Header("References")]
    public Rigidbody rb;
    public HandGrabInteractable[] handGrabInteractables; // Interaction 오브젝트의 HGI만 넣기

    [Header("Tuning")]
    public float forceMultiplier = 50f;     // 선형 힘 민감도
    public float maxLinearSpeed = 5f;
    public float maxAngularSpeed = 3f;      // rad/s (Y)
    public float linearDamping = 1.5f;
    public float angularDamping = 4f;
    public float velocityDeadzone = 0.05f;  // m/s
    public float twoHandYawGain = 1.2f;     // 두 손 회전 민감도(각속도 가산)

    // 내부 상태
    private readonly List<Transform> activeHands = new();
    private readonly Dictionary<Transform, Vector3> lastPos = new();
    private Vector3 lastTwoHandDir = Vector3.zero; // 두 손 사이 벡터(수평) 기록

    void Reset() { rb = GetComponent<Rigidbody>(); }

    void FixedUpdate()
    {
        ApplyDamping();

        RefreshActiveHands();
        if (activeHands.Count == 0) return;

        float dt = Time.fixedDeltaTime;

        // 1) 한 손/두 손 공통: 각 손의 수평 속도를 힘으로 적용
        foreach (var handTf in activeHands)
        {
            Vector3 cur = handTf.position;
            Vector3 prev;
            if (!lastPos.TryGetValue(handTf, out prev))
            {
                // 포즈 스냅 첫 프레임 스파이크 억제
                prev = cur;
                lastPos[handTf] = cur;
            }

            Vector3 vel = (cur - prev) / dt;
            vel.y = 0f;

            if (vel.sqrMagnitude < velocityDeadzone * velocityDeadzone)
                vel = Vector3.zero;

            Vector3 force = vel * forceMultiplier;

            // 손 위치(오프셋)에서 힘 적용 → 자연스러운 전진 + 회전
            rb.AddForceAtPosition(force * dt, cur, ForceMode.VelocityChange);

            lastPos[handTf] = cur;
        }

        // 2) 두 손일 때: 손 사이 수평 벡터의 각도 변화로 Y축 각속도 가산
        if (activeHands.Count >= 2)
        {
            var p0 = ProjectToPlane(activeHands[0].position, Vector3.up);
            var p1 = ProjectToPlane(activeHands[1].position, Vector3.up);

            var dir = (p1 - p0).normalized;
            if (lastTwoHandDir == Vector3.zero) lastTwoHandDir = dir;

            float signedAngle = Vector3.SignedAngle(lastTwoHandDir, dir, Vector3.up);
            float yawVel = (signedAngle * Mathf.Deg2Rad) / dt; // rad/s
            rb.angularVelocity = new Vector3(0f,
                Mathf.Clamp(rb.angularVelocity.y + yawVel * twoHandYawGain, -maxAngularSpeed, maxAngularSpeed),
                0f);

            lastTwoHandDir = dir;
        }
        else
        {
            lastTwoHandDir = Vector3.zero;
        }

        // 속도 제한(수평만)
        Vector3 v = rb.velocity; v.y = 0f;
        if (v.magnitude > maxLinearSpeed)
            rb.velocity = new Vector3(v.normalized.x * maxLinearSpeed, rb.velocity.y, v.normalized.z * maxLinearSpeed);

        // 각속도(Y) 제한
        Vector3 w = rb.angularVelocity;
        w.x = 0f; w.z = 0f;
        w.y = Mathf.Clamp(w.y, -maxAngularSpeed, maxAngularSpeed);
        rb.angularVelocity = w;
    }

    private void ApplyDamping()
    {
        float dt = Time.fixedDeltaTime;
        float lin = 1f / (1f + linearDamping * dt);
        float ang = 1f / (1f + angularDamping * dt);

        rb.velocity = new Vector3(rb.velocity.x * lin, rb.velocity.y, rb.velocity.z * lin);
        rb.angularVelocity = new Vector3(0f, rb.angularVelocity.y * ang, 0f);
    }

    private Vector3 ProjectToPlane(Vector3 p, Vector3 up)
    {
        p.y = 0f; return p;
    }

    private void RefreshActiveHands()
    {
        activeHands.Clear();

        // HandGrabInteractable에서 현재 선택 중인 인터랙터들의 Transform 수집
        if (handGrabInteractables != null)
        {
            foreach (var hgi in handGrabInteractables)
            {
                if (hgi == null) continue;
                var list = hgi.SelectingInteractors;
                foreach (var inter in list)
                {
                    var mb = inter as MonoBehaviour;
                    if (mb != null)
                    {
                        var t = mb.transform;
                        if (!activeHands.Contains(t))
                            activeHands.Add(t);

                        // 스냅 첫 프레임 초기화
                        if (!lastPos.ContainsKey(t))
                            lastPos[t] = t.position;
                    }
                }
            }
        }

        // 놓은 손 정리
        var keys = new List<Transform>(lastPos.Keys);
        foreach (var k in keys)
            if (!activeHands.Contains(k))
                lastPos.Remove(k);
    }
}
