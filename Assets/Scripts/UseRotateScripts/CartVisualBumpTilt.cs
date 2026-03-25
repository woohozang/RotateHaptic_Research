using UnityEngine;

public class CartVisualBumpTilt : MonoBehaviour
{
    [Header("References")]
    public Transform visualTiltRoot;   // 기울일 시각 루트
    public Transform cargoObject;      // CartPivot 아래 CargoObject
    public Transform cargoRoot;        // cargoObject의 부모(선택)

    [Header("Tilt Settings")]
    public float targetRollAngle = 5f;     // 최대 기울기 크기
    public float tiltLerpSpeed = 8f;       // 기울기 보간 속도
    public float tiltResetSpeed = 6f;      // 복귀 속도

    [Header("Optional Lift")]
    public float liftAmount = 0.015f;      // 방지턱 탈 때 살짝 위로
    public float liftLerpSpeed = 8f;

    [Header("Cargo Sway")]
    public float cargoOffsetAmount = 0.12f;  // 좌우 쏠림 최대
    public float cargoLerpSpeed = 6f;

    private int _leftBumpContacts = 0;
    private int _rightBumpContacts = 0;

    private Quaternion _visualBaseLocalRot;
    private Vector3 _visualBaseLocalPos;

    private Vector3 _cargoBaseLocalPos;

    void Start()
    {
        if (visualTiltRoot == null)
            visualTiltRoot = transform;

        _visualBaseLocalRot = visualTiltRoot.localRotation;
        _visualBaseLocalPos = visualTiltRoot.localPosition;

        if (cargoObject != null)
            _cargoBaseLocalPos = cargoObject.localPosition;
    }

    void LateUpdate()
    {
        UpdateVisualTilt();
        UpdateCargoOffset();
    }

    private void UpdateVisualTilt()
    {
        float targetZ = 0f;
        float targetY = _visualBaseLocalPos.y;

        bool leftActive = _leftBumpContacts > 0;
        bool rightActive = _rightBumpContacts > 0;

        if (leftActive && !rightActive)
        {
            // 왼쪽 방지턱 -> 카트가 왼쪽으로 기울어 보이게
            targetZ = -targetRollAngle;
            targetY = _visualBaseLocalPos.y + liftAmount;
        }
        else if (!leftActive && rightActive)
        {
            // 오른쪽 방지턱 -> 반대로
            targetZ = +targetRollAngle;
            targetY = _visualBaseLocalPos.y + liftAmount;
        }
        else
        {
            targetZ = 0f;
            targetY = _visualBaseLocalPos.y;
        }

        Vector3 currentEuler = visualTiltRoot.localEulerAngles;
        float currentZ = NormalizeAngle(currentEuler.z);

        float zSpeed = (Mathf.Abs(targetZ) > Mathf.Abs(currentZ)) ? tiltLerpSpeed : tiltResetSpeed;
        float newZ = Mathf.Lerp(currentZ, targetZ, 1f - Mathf.Exp(-zSpeed * Time.deltaTime));

        Quaternion targetRot = _visualBaseLocalRot * Quaternion.Euler(0f, 0f, newZ);
        visualTiltRoot.localRotation = targetRot;

        Vector3 pos = visualTiltRoot.localPosition;
        pos.y = Mathf.Lerp(pos.y, targetY, 1f - Mathf.Exp(-liftLerpSpeed * Time.deltaTime));
        visualTiltRoot.localPosition = pos;
    }

    private void UpdateCargoOffset()
    {
        if (cargoObject == null) return;

        float targetCargoX = 0f;

        bool leftActive = _leftBumpContacts > 0;
        bool rightActive = _rightBumpContacts > 0;

        if (leftActive && !rightActive)
        {
            // 왼쪽 방지턱 -> 짐은 오른쪽으로
            targetCargoX = +cargoOffsetAmount;
        }
        else if (!leftActive && rightActive)
        {
            // 오른쪽 방지턱 -> 짐은 왼쪽으로
            targetCargoX = -cargoOffsetAmount;
        }
        else
        {
            targetCargoX = 0f;
        }

        Vector3 local = cargoObject.localPosition;
        local.x = Mathf.Lerp(local.x, _cargoBaseLocalPos.x + targetCargoX, 1f - Mathf.Exp(-cargoLerpSpeed * Time.deltaTime));
        cargoObject.localPosition = local;
    }

    public void EnterLeftBump()
    {
        _leftBumpContacts++;
        // Debug.Log($"[Bump] EnterLeft -> {_leftBumpContacts}");
    }

    public void ExitLeftBump()
    {
        _leftBumpContacts = Mathf.Max(0, _leftBumpContacts - 1);
        // Debug.Log($"[Bump] ExitLeft -> {_leftBumpContacts}");
    }

    public void EnterRightBump()
    {
        _rightBumpContacts++;
        // Debug.Log($"[Bump] EnterRight -> {_rightBumpContacts}");
    }

    public void ExitRightBump()
    {
        _rightBumpContacts = Mathf.Max(0, _rightBumpContacts - 1);
        // Debug.Log($"[Bump] ExitRight -> {_rightBumpContacts}");
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}