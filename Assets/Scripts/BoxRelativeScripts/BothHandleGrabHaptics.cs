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
public class BothHandleGrabHaptics : MonoBehaviour
{
    [Header("Assign a component on BothHandle that implements IInteractableView (usually Grabbable)")]
    public MonoBehaviour grabbableComponent; // BothHandle의 Grabbable(권장) 드래그

    [Header("Optional (recommended): assign left/right interactor roots for robust detection")]
    public GameObject leftInteractorRoot;   // 예: LeftHandGrabInteractor 루트
    public GameObject rightInteractorRoot;  // 예: RightHandGrabInteractor 루트

    [Header("Speed source")]
    [Tooltip("true면 선속도, false면 각속도(회전) 기반")]
    public bool useLinearVelocity = true;

    [Header("Dead zone (micro jitter prevention)")]
    [Tooltip("이 값 이하의 속도는 움직임으로 치지 않음(노이즈 제거)")]
    public float speedThreshold = 0.03f;

    [Header("Left hand (dynamic)")]
    public float leftSpeedMin = 0.05f;
    public float leftSpeedMax = 1.5f;
    [Range(0f, 1f)] public float leftAmpMin = 0.05f; // 움직일 때 최소 진동
    [Range(0f, 1f)] public float leftAmpMax = 0.8f;
    [Range(0f, 1f)] public float leftFrequency = 0.7f;

    [Header("Right hand (weak buzz)")]
    [Range(0f, 1f)] public float rightAmp = 0.10f;
    [Range(0f, 1f)] public float rightFrequency = 0.3f;

    [Header("Smoothing")]
    [Range(0f, 30f)] public float smoothing = 12f;

    private bool _leftHeld;
    private bool _rightHeld;
    private float _leftAmpSmoothed;

    /// <summary>
    /// BothHandle의 Unity Event Wrapper Select/Unselect 둘 다 이 함수에 연결
    /// </summary>
    public void OnSelectChanged()
    {
        UpdateHeldFlagsFromSelecting();
    }

    private void Update()
    {
        // (권장) 이벤트 누락/순서 꼬임 방지: 매 프레임 갱신
        UpdateHeldFlagsFromSelecting();

        // 아무도 안 잡으면 OFF
        if (!_leftHeld && !_rightHeld)
        {
            StopAll();
            return;
        }

        // 속도 읽기
        float speedL = GetSpeed(OVRInput.Controller.LTouch);
        float speedR = GetSpeed(OVRInput.Controller.RTouch);

        // threshold는 "빼서 0으로" 처리 (노이즈 제거)
        float speedLEff = Mathf.Max(0f, speedL - speedThreshold);
        float speedREff = Mathf.Max(0f, speedR - speedThreshold);

        // ------------------------------
        // LEFT: (요청사항) 잡고 + 움직일 때만 진동, 정지 시 OFF
        // ------------------------------
        float leftAmpTarget = 0f;

        if (_leftHeld && speedLEff > 0f)
        {
            float t = Mathf.InverseLerp(leftSpeedMin, leftSpeedMax, speedLEff);
            leftAmpTarget = Mathf.Lerp(leftAmpMin, leftAmpMax, t);
            // 움직일 때 최소 진동은 leftAmpMin부터 시작
            leftAmpTarget = Mathf.Max(leftAmpMin, leftAmpTarget);
        }

        // 스무딩
        if (smoothing > 0f)
        {
            float a = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            _leftAmpSmoothed = Mathf.Lerp(_leftAmpSmoothed, leftAmpTarget, a);
        }
        else
        {
            _leftAmpSmoothed = leftAmpTarget;
        }

        // ------------------------------
        // RIGHT: 잡고 + 움직일 때만 buzz, 정지 시 OFF
        // ------------------------------
        float rightAmpFinal = (_rightHeld && speedREff > 0f) ? rightAmp : 0f;

        // ------------------------------
        // Apply
        // ------------------------------
        OVRInput.SetControllerVibration(leftFrequency, _leftHeld ? _leftAmpSmoothed : 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(rightFrequency, rightAmpFinal, OVRInput.Controller.RTouch);
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
        _leftAmpSmoothed = 0f;
    }

    private void OnDisable()
    {
        StopAll();
    }

    /// <summary>
    /// 현재 Selecting 목록을 읽어 left/right held bool을 갱신
    /// - leftInteractorRoot/rightInteractorRoot 지정 시 가장 정확
    /// - 미지정 시 이름에 left/right 포함 여부로 fallback
    /// </summary>
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

                // Robust: 루트 지정 비교
                if (leftInteractorRoot != null &&
                    (go == leftInteractorRoot || go.transform.IsChildOf(leftInteractorRoot.transform)))
                {
                    _leftHeld = true;
                }

                if (rightInteractorRoot != null &&
                    (go == rightInteractorRoot || go.transform.IsChildOf(rightInteractorRoot.transform)))
                {
                    _rightHeld = true;
                }

                // Fallback: 이름 기반
                if (leftInteractorRoot == null)
                {
                    string n = go.name.ToLowerInvariant();
                    if (n.Contains("left")) _leftHeld = true;
                }

                if (rightInteractorRoot == null)
                {
                    string n = go.name.ToLowerInvariant();
                    if (n.Contains("right")) _rightHeld = true;
                }
            }
        }
    }

    private static object FindInteractableView(MonoBehaviour anyCompOnBothHandle)
    {
        if (anyCompOnBothHandle == null) return null;

        // 같은 오브젝트에서 IInteractableView 구현 컴포넌트 찾기
        var comps = anyCompOnBothHandle.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            if (c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
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
