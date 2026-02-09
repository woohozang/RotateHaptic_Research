using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// One-hand cart transformer:
    /// - Move on plane (default Y-up plane)
    /// - Optionally forward-only (project to forward axis)
    /// - Yaw-only rotation
    /// - LEFT hand only: apply visual lag/offset (pos+rot) via smoothing + optional max offset clamp
    /// </summary>
    public class OneGrabLeftLagCartPlaneTransformer : MonoBehaviour, ITransformer
    {
        [Header("Plane")]
        [SerializeField, Optional]
        private Transform _planeTransform = null;

        [SerializeField, Optional]
        private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Axes")]
        [Tooltip("전진축(로컬 Z)만 이동 허용")]
        [SerializeField] private bool _forwardOnly = true;

        [Tooltip("전진축 기준 Transform (없으면 Target Transform 기준)")]
        [SerializeField, Optional] private Transform _forwardReference = null;

        [Tooltip("Yaw(Y 회전)만 허용")]
        [SerializeField] private bool _yawOnly = true;

        [Header("Left-hand lag (visual offset)")]
        [Tooltip("왼손으로 잡았을 때만 지연 적용")]
        [SerializeField] private bool _enableLeftLag = true;

        [Tooltip("값이 클수록 더 빨리 따라감(=덜 무거움). 0이면 거의 안 따라감")]
        [Range(0f, 30f)]
        [SerializeField] private float _posFollow = 6f;

        [Tooltip("값이 클수록 더 빨리 따라감(=덜 무거움). 0이면 거의 안 따라감")]
        [Range(0f, 30f)]
        [SerializeField] private float _rotFollow = 6f;

        [Tooltip("지연으로 인해 벌어질 수 있는 최대 오프셋 거리(미터). 0이면 클램프 안함")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _maxOffsetMeters = 0.15f;

        [Tooltip("왼손 지연 적용 시, 아주 작은 떨림 방지(미터)")]
        [SerializeField] private float _posJitterThreshold = 0.0005f;

        [Tooltip("왼손 지연 적용 시, 아주 작은 떨림 방지(도)")]
        [SerializeField] private float _rotJitterThresholdDeg = 0.2f;

        private IGrabbable _grabbable;

        // cached
        private Rigidbody _targetRb;

        // begin state
        private Vector3 _grabStartOnPlane;
        private Vector3 _targetStartPos;
        private Quaternion _targetStartRot;

        // lagged state (for left)
        private Vector3 _lagPos;
        private Quaternion _lagRot;

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
        }

        private Vector3 WorldPlaneNormal()
        {
            Transform t = _planeTransform != null ? _planeTransform : _grabbable.Transform;
            return t.TransformDirection(_localPlaneNormal).normalized;
        }

        public void BeginTransform()
        {
            var target = _grabbable.Transform;
            _targetRb = target.GetComponent<Rigidbody>();

            var planeNormal = WorldPlaneNormal();
            var grab = _grabbable.GrabPoints[0];

            _targetStartPos = target.position;
            _targetStartRot = target.rotation;

            _grabStartOnPlane = Vector3.ProjectOnPlane(grab.position, planeNormal);

            // initialize lag state to current
            _lagPos = _targetStartPos;
            _lagRot = _targetStartRot;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            var planeNormal = WorldPlaneNormal();
            var grab = _grabbable.GrabPoints[0];

            // 1) Compute desired position on plane
            Vector3 grabNowOnPlane = Vector3.ProjectOnPlane(grab.position, planeNormal);
            Vector3 planarDelta = (grabNowOnPlane - _grabStartOnPlane);

            if (_forwardOnly)
            {
                Transform fRef = _forwardReference != null ? _forwardReference : target;
                Vector3 fwd = Vector3.ProjectOnPlane(fRef.forward, planeNormal);
                if (fwd.sqrMagnitude > 1e-6f)
                {
                    fwd.Normalize();
                    planarDelta = Vector3.Dot(planarDelta, fwd) * fwd;
                }
                else
                {
                    planarDelta = Vector3.zero;
                }
            }

            Vector3 desiredPos = _targetStartPos + planarDelta;

            // 2) Compute desired rotation (yaw only)
            Quaternion desiredRot = _targetStartRot;

            if (_yawOnly)
            {
                // grab.forward projected on plane
                Vector3 f = Vector3.ProjectOnPlane(grab.forward, planeNormal);
                if (f.sqrMagnitude > 1e-6f)
                {
                    Quaternion look = Quaternion.LookRotation(f.normalized, planeNormal);
                    float yaw = look.eulerAngles.y;
                    desiredRot = Quaternion.Euler(0f, yaw, 0f);
                }
            }
            else
            {
                desiredRot = grab.rotation;
            }

            // 3) Apply left-hand lag only
            bool leftActive = _enableLeftLag && IsLeftHandSelecting(_grabbable);

            if (leftActive)
            {
                // jitter guard
                float posErr = Vector3.Distance(_lagPos, desiredPos);
                float rotErr = Quaternion.Angle(_lagRot, desiredRot);

                // exponential smoothing (frame-rate independent)
                float ap = 1f - Mathf.Exp(-_posFollow * Time.deltaTime);
                float ar = 1f - Mathf.Exp(-_rotFollow * Time.deltaTime);

                if (posErr > _posJitterThreshold)
                    _lagPos = Vector3.Lerp(_lagPos, desiredPos, ap);

                if (rotErr > _rotJitterThresholdDeg)
                    _lagRot = Quaternion.Slerp(_lagRot, desiredRot, ar);

                // clamp max offset (optional)
                if (_maxOffsetMeters > 0f)
                {
                    Vector3 diff = _lagPos - desiredPos;
                    float d = diff.magnitude;
                    if (d > _maxOffsetMeters)
                        _lagPos = desiredPos + diff.normalized * _maxOffsetMeters;
                }

                ApplyPose(target, _lagPos, _lagRot);
            }
            else
            {
                // right hand: 1:1
                ApplyPose(target, desiredPos, desiredRot);

                // keep lag state synced so it doesn't "snap" when switching
                _lagPos = desiredPos;
                _lagRot = desiredRot;
            }
        }

        public void EndTransform() { }

        private void ApplyPose(Transform target, Vector3 pos, Quaternion rot)
        {
            if (_targetRb != null && _targetRb.isKinematic)
            {
                _targetRb.MovePosition(pos);
                _targetRb.MoveRotation(rot);
            }
            else
            {
                target.position = pos;
                target.rotation = rot;
            }
        }

        /// <summary>
        /// SDK 버전마다 IInteractableView의 Selecting 리스트 프로퍼티 이름이 달라질 수 있어
        /// Reflection으로 InteractorsSelecting / SelectingInteractors 둘 다 대응.
        /// </summary>
        private static bool IsLeftHandSelecting(IGrabbable grabbable)
        {
            if (grabbable == null) return false;

            // Grabbable은 보통 MonoBehaviour 컴포넌트이므로 GetComponent를 통해 IInteractableView를 찾을 수 있음
            var mb = grabbable as MonoBehaviour;
            if (mb == null) return false;

            // 같은 GameObject에 붙어있는 IInteractableView 계열을 찾는다
            var views = mb.GetComponents<MonoBehaviour>();
            object interactableView = null;

            foreach (var c in views)
            {
                if (c == null) continue;
                // namespace/type name 기반으로 넓게 매칭
                if (c.GetType().Name.Contains("Interactable", StringComparison.OrdinalIgnoreCase) ||
                    c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
                {
                    // IInteractableView 인터페이스를 직접 참조하지 않아도 되도록 object로 잡음
                    if (c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
                    {
                        interactableView = c;
                        break;
                    }
                }
            }

            if (interactableView == null) return false;

            var t = interactableView.GetType();

            // Try property names
            PropertyInfo p = t.GetProperty("InteractorsSelecting", BindingFlags.Public | BindingFlags.Instance)
                           ?? t.GetProperty("SelectingInteractors", BindingFlags.Public | BindingFlags.Instance);

            if (p == null) return false;

            object val = p.GetValue(interactableView);
            if (val is IEnumerable enumerable)
            {
                foreach (var it in enumerable)
                {
                    if (it == null) continue;

                    // interactor view -> component -> name check
                    if (it is MonoBehaviour itMb)
                    {
                        string n = itMb.gameObject.name.ToLowerInvariant();
                        if (n.Contains("left")) return true;
                    }
                    else
                    {
                        // 혹시 컴포넌트가 아닌 경우 ToString fallback
                        string s = it.ToString().ToLowerInvariant();
                        if (s.Contains("left")) return true;
                    }
                }
            }

            return false;
        }

        #region Inject
        public void InjectOptionalPlaneTransform(Transform planeTransform) => _planeTransform = planeTransform;
        public void InjectOptionalForwardReference(Transform forwardRef) => _forwardReference = forwardRef;
        #endregion
    }
}
