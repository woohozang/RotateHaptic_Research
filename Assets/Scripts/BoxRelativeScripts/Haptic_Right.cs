using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// BothHandle(Grabbable) 하나에 붙여서 사용.
/// - Unity Event Wrapper의 Select()/Unselect()에 OnSelectChanged() 연결
/// - Selecting 목록을 읽어 left/right/grab 상태를 판별
/// - 한손이면 해당 손만, 양손이면 양손 모두 햅틱
/// - 왼손/오른손 모두 "잡고 + 움직일 때만" 진동 (정지 시 OFF)  ✅요청사항 반영
/// </summary>
public class Haptic_Right : MonoBehaviour
{
    [Header("Assign Grabbable component")]
    public MonoBehaviour grabbableComponent;

    [Header("Interactor Roots")]
    public GameObject leftInteractorRoot;
    public GameObject rightInteractorRoot;

    [Header("Speed source")]
    public bool useLinearVelocity = true;

    [Header("Dead zone")]
    public float speedThreshold = 0.03f;

    [Header("Right hand (Dynamic - Modified)")]
    public float rightSpeedMin = 0.05f;
    public float rightSpeedMax = 1.5f;
    [Range(0f, 1f)] public float rightAmpMin = 0.05f;
    [Range(0f, 1f)] public float rightAmpMax = 0.8f;
    [Range(0f, 1f)] public float rightFrequency = 0.7f;

    [Header("Left hand (Weak buzz - Modified)")]
    [Range(0f, 1f)] public float leftAmp = 0.10f;
    [Range(0f, 1f)] public float leftFrequency = 0.3f;

    [Header("Smoothing")]
    [Range(0f, 30f)] public float smoothing = 12f;

    private bool _leftHeld;
    private bool _rightHeld;
    private float _rightAmpSmoothed;

    public void OnSelectChanged() => UpdateHeldFlagsFromSelecting();

    private void Update()
    {
        UpdateHeldFlagsFromSelecting();

        if (!_leftHeld && !_rightHeld)
        {
            StopAll();
            return;
        }

        // 속도 읽기
        float speedL = GetSpeed(OVRInput.Controller.LTouch);
        float speedR = GetSpeed(OVRInput.Controller.RTouch);

        float speedLEff = Mathf.Max(0f, speedL - speedThreshold);
        float speedREff = Mathf.Max(0f, speedR - speedThreshold);

        // ------------------------------------------------
        // RIGHT: 이제 오른손이 속도에 따라 다이내믹하게 진동합니다.
        // ------------------------------------------------
        float rightAmpTarget = 0f;

        if (_rightHeld && speedREff > 0f)
        {
            float t = Mathf.InverseLerp(rightSpeedMin, rightSpeedMax, speedREff);
            rightAmpTarget = Mathf.Lerp(rightAmpMin, rightAmpMax, t);
            rightAmpTarget = Mathf.Max(rightAmpMin, rightAmpTarget);
        }

        // 스무딩 적용
        if (smoothing > 0f)
        {
            float a = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            _rightAmpSmoothed = Mathf.Lerp(_rightAmpSmoothed, rightAmpTarget, a);
        }
        else
        {
            _rightAmpSmoothed = rightAmpTarget;
        }

        // ------------------------------------------------
        // LEFT: 왼손은 잡고 움직일 때만 고정된 buzz 진동을 줍니다.
        // ------------------------------------------------
        float leftAmpFinal = (_leftHeld && speedLEff > 0f) ? leftAmp : 0f;

        // 진동 적용
        OVRInput.SetControllerVibration(rightFrequency, _rightHeld ? _rightAmpSmoothed : 0f, OVRInput.Controller.RTouch);
        OVRInput.SetControllerVibration(leftFrequency, leftAmpFinal, OVRInput.Controller.LTouch);
    }

    private float GetSpeed(OVRInput.Controller c)
    {
        Vector3 v = useLinearVelocity
            ? OVRInput.GetLocalControllerVelocity(c)
            : OVRInput.GetLocalControllerAngularVelocity(c);
        return v.magnitude;
    }

    public void StopAll()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        _rightAmpSmoothed = 0f;
    }

    private void OnDisable() => StopAll();

    private void UpdateHeldFlagsFromSelecting()
    {
        _leftHeld = false;
        _rightHeld = false;

        object view = FindInteractableView(grabbableComponent);
        if (view == null) return;

        IEnumerable selecting = GetSelectingEnumerable(view);
        if (selecting == null) return;

        foreach (var it in selecting)
        {
            if (it is Component comp)
            {
                GameObject go = comp.gameObject;
                if (leftInteractorRoot != null && (go == leftInteractorRoot || go.transform.IsChildOf(leftInteractorRoot.transform)))
                    _leftHeld = true;
                if (rightInteractorRoot != null && (go == rightInteractorRoot || go.transform.IsChildOf(rightInteractorRoot.transform)))
                    _rightHeld = true;

                if (leftInteractorRoot == null && go.name.ToLowerInvariant().Contains("left")) _leftHeld = true;
                if (rightInteractorRoot == null && go.name.ToLowerInvariant().Contains("right")) _rightHeld = true;
            }
        }
    }

    private static object FindInteractableView(MonoBehaviour anyCompOnBothHandle)
    {
        if (anyCompOnBothHandle == null) return null;
        var comps = anyCompOnBothHandle.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c != null && c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
                return c;
        }
        return null;
    }

    private static IEnumerable GetSelectingEnumerable(object interactableView)
    {
        var t = interactableView.GetType();
        var p = t.GetProperty("InteractorsSelecting", BindingFlags.Public | BindingFlags.Instance)
             ?? t.GetProperty("SelectingInteractors", BindingFlags.Public | BindingFlags.Instance);
        return p?.GetValue(interactableView) as IEnumerable;
    }
}
