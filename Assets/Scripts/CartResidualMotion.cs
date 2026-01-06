using UnityEngine;
using Oculus.Interaction;

[RequireComponent(typeof(Rigidbody))]
public class CartResidualMotion : MonoBehaviour
{

    [Header("References")]
    public Grabbable grabbable;     // BothHandle의 Grabbable
    public Rigidbody cartRb;        // RootCart Rigidbody
    public Transform leftHand;      // LeftHandAnchor (OVRCameraRig)
    public Transform rightHand;     // RightHandAnchor

    [Header("Linear Motion")]
    public float residualBoost = 5.0f;
    public float releaseSpeedGain = 1.2f;
    public float maxLinearSpeed = 7.5f;

    [Header("Angular Motion (Asymmetric)")]
    public float angularGain = 2.5f;
    public float angularResidualBoost = 0.8f;
    public float maxAngularImpulse = 2.5f;

    [Header("Inertia / Drag")]
    public float minInertiaTime = 1.8f;
    public float maxInertiaTime = 3.5f;
    public float minInertiaDrag = 0.008f;
    public float maxInertiaDrag = 0.025f;
    public float normalDrag = 0.3f;
    public float normalAngularDrag = 1.2f;

    [Header("Debug")]
    public bool debugLog = true;

    // 내부 상태
    private bool wasGrabbing = false;
    private float inertiaTimer = 0f;

    private Vector3 cachedVL;
    private Vector3 cachedVR;

    void Reset()
    {
        cartRb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        bool isGrabbing = grabbable.SelectingPoints.Count > 0;

        if (isGrabbing)
        {
            CacheControllerVelocity();
        }

        if (wasGrabbing && !isGrabbing)
        {
            ApplyResidualMotion();
        }

        wasGrabbing = isGrabbing;

        // 감쇠 복구
        if (inertiaTimer > 0f)
        {
            inertiaTimer -= Time.deltaTime;
        }
        else
        {
            cartRb.drag = normalDrag;
            cartRb.angularDrag = normalAngularDrag;
        }
    }

    // -------------------------------------------------
    // 손 속도 캐싱 (월드 기준)
    // -------------------------------------------------
    void CacheControllerVelocity()
    {
        Vector3 vL = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.LTouch);
        Vector3 vR = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

        if (Camera.main != null)
        {
            vL = Camera.main.transform.TransformDirection(vL);
            vR = Camera.main.transform.TransformDirection(vR);
        }

        cachedVL = vL;
        cachedVR = vR;
    }

    // -------------------------------------------------
    // Release 시 잔존 관성 적용
    // -------------------------------------------------
    void ApplyResidualMotion()
    {
        Vector3 center = transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        // =========================
        // 1️⃣ 선형 관성 (앞으로 밀림)
        // =========================
        float linearSpeed =
            Mathf.Clamp(
                (cachedVL.magnitude + cachedVR.magnitude) * 0.5f,
                0f,
                maxLinearSpeed
            );

        float speed01 = Mathf.Clamp01(linearSpeed / maxLinearSpeed);

        // =========================
        // 2️⃣ 회전 관성 (접선 기반, 핵심)
        // =========================
        float tSpeedL = ComputeTangentialSpeed(leftHand.position, cachedVL, center);
        float tSpeedR = ComputeTangentialSpeed(rightHand.position, cachedVR, center);

        // 비대칭 회전 기여도
        float angularInput = tSpeedL + tSpeedR;

        // 회전 임펄스 (비선형 억제)
        float angularImpulse =
            Mathf.Sign(angularInput) *
            Mathf.Sqrt(Mathf.Abs(angularInput)) *
            angularGain *
            angularResidualBoost;

        angularImpulse = Mathf.Clamp(
            angularImpulse,
            -maxAngularImpulse,
            maxAngularImpulse
        );

        // =========================
        // 3️⃣ 감쇠 설정
        // =========================
        inertiaTimer = Mathf.Lerp(minInertiaTime, maxInertiaTime, speed01);

        cartRb.drag = Mathf.Lerp(maxInertiaDrag, minInertiaDrag, speed01);
        cartRb.angularDrag = Mathf.Lerp(3.0f, 6.0f, speed01);

        // =========================
        // 4️⃣ 물리 적용 (Impulse)
        // =========================
        cartRb.AddForce(
            forward * linearSpeed * releaseSpeedGain * residualBoost * cartRb.mass,
            ForceMode.Impulse
        );

        cartRb.AddTorque(
            Vector3.up * angularImpulse * cartRb.mass,
            ForceMode.Impulse
        );

        if (debugLog)
        {
            Debug.Log(
                $"[CartRelease]\n" +
                $"Linear:{linearSpeed:F2} speed01:{speed01:F2}\n" +
                $"tL:{tSpeedL:F2}, tR:{tSpeedR:F2}\n" +
                $"AngularImpulse:{angularImpulse:F2}"
            );
        }
    }

    // -------------------------------------------------
    // 접선 속도 계산 (좌/우 완전 대칭)
    // -------------------------------------------------
    float ComputeTangentialSpeed(Vector3 handPos, Vector3 handVel, Vector3 center)
    {
        Vector3 r = handPos - center;
        r.y = 0f;

        if (r.sqrMagnitude < 0.0001f)
            return 0f;

        Vector3 tangent = Vector3.Cross(Vector3.up, r).normalized;
        return Vector3.Dot(handVel, tangent);
    }
}
