using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// SDK 81 TwoGrabPlaneTransformer 기반 (양손 조작 가정)
    /// - Cargo(예: Sphere) 위치를 CartPivot 로컬 X로 판정
    /// - Center 없이 Left/Right만 사용 (끊김 감소)
    /// - Left 상태: "왼손 grab point"만 지연(Lag)
    /// - Right 상태: "오른손 grab point"만 지연(Lag)
    ///
    /// 중요:
    /// - _leftGrabPointRef / _rightGrabPointRef 를 반드시 연결하면
    ///   왼손/오른손 매핑이 100% 안정적으로 동작합니다.
    /// </summary>
    public class DynamicTwoGrab : MonoBehaviour, ITransformer
    {
        [Header("Plane (same as TwoGrabPlaneTransformer)")]
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Serializable]
        public class TwoGrabPlaneConstraints
        {
            public FloatConstraint MaxScale;
            public FloatConstraint MinScale;
            public FloatConstraint MaxY;
            public FloatConstraint MinY;
        }

        [SerializeField] private TwoGrabPlaneConstraints _constraints;

        [Header("Refs (required)")]
        [Tooltip("카트 중심 기준 Transform (CartPivot 추천)")]
        [SerializeField] private Transform _cartPivot;

        [Tooltip("좌/우를 판정할 동적 물체 Transform (Sphere 등, 카트의 자식이 아니어도 됨)")]
        [SerializeField] private Transform _cargoObject;

        [Header("Left/Right GrabPoint Refs (strongly recommended)")]
        [Tooltip("왼손 쪽 그랩 포인트 마커 Transform (LeftGrabPoint)")]
        [SerializeField] private Transform _leftGrabPointRef;

        [Tooltip("오른손 쪽 그랩 포인트 마커 Transform (RightGrabPoint)")]
        [SerializeField] private Transform _rightGrabPointRef;

        [Header("Switch zone (hysteresis, no Center)")]
        [Tooltip("현재 Left일 때 x가 +zone을 넘으면 Right로 전환 / Right일 때 x가 -zone을 넘으면 Left로 전환")]
        [SerializeField] private float _switchZone = 0.05f;

        public enum BalanceState { Left, Right }
        [SerializeField] private BalanceState _state = BalanceState.Left;

        [Header("Lag tuning (smaller = heavier/slower)")]
        [SerializeField] private float _leftFollow = 5f;   // Left 상태일 때 왼손 lag 강도(낮을수록 더 무거움)
        [SerializeField] private float _rightFollow = 5f;  // Right 상태일 때 오른손 lag 강도

        [Header("Jitter threshold")]
        [SerializeField] private float _posJitterThreshold = 0.0005f;

        private IGrabbable _grabbable;

        // base state copied from TwoGrabPlaneTransformer
        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        // filtered grab point positions
        private Vector3 _filtA;
        private Vector3 _filtB;
        private bool _filterInitialized;

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

            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            _filtA = grabA.position;
            _filtB = grabB.position;
            _filterInitialized = true;

            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane(_filtA, _filtB, planeNormal);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(state.PlanarDistance, target.worldToLocalMatrix);
        }

        public void UpdateTransform()
        {
            if (_grabbable == null || _grabbable.GrabPoints == null || _grabbable.GrabPoints.Count < 2)
                return;

            UpdateBalanceStateLR();

            var target = _grabbable.Transform;
            Vector3 rawA = _grabbable.GrabPoints[0].position;
            Vector3 rawB = _grabbable.GrabPoints[1].position;

            if (!_filterInitialized)
            {
                _filtA = rawA;
                _filtB = rawB;
                _filterInitialized = true;
            }

            // "왼손 grab point index"를 확정 (0 또는 1)
            int leftIdx = ResolveLeftIdx(rawA, rawB);

            // 상태에 따라 왼손 or 오른손에만 lag를 적용
            float followA = GetFollowForIndex(index: 0, leftIdx);
            float followB = GetFollowForIndex(index: 1, leftIdx);

            // micro jitter ignore
            float jt2 = _posJitterThreshold * _posJitterThreshold;
            if ((rawA - _filtA).sqrMagnitude < jt2) rawA = _filtA;
            if ((rawB - _filtB).sqrMagnitude < jt2) rawB = _filtB;

            // exp smoothing
            _filtA = ExpFollow(_filtA, rawA, followA, Time.deltaTime);
            _filtB = ExpFollow(_filtB, rawB, followB, Time.deltaTime);

            var planeNormal = WorldPlaneNormal();
            var twoGrabPlaneState = TwoGrabPlane(_filtA, _filtB, planeNormal);

            // --- Below: same flow as SDK81 TwoGrabPlaneTransformer (scale/pose/constraints) ---
            float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDistInWorld != 0 ? twoGrabPlaneState.PlanarDistance / prevDistInWorld : 1f;

            float targetScale = scaleDelta * target.localScale.x;
            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain) targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                if (_constraints.MaxScale.Constrain) targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
            }

            // 원본과 동일하게 localScale.x를 기준으로 비율 유지
            if (_constraints != null)
            {
                target.localScale = (targetScale / target.localScale.x) * target.localScale;
            }

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, twoGrabPlaneState.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            if (_constraints != null)
            {
                target.position = ConstrainAlongDirection(
                    target.position,
                    target.parent != null ? target.parent.position : Vector3.zero,
                    planeNormal,
                    _constraints.MinY,
                    _constraints.MaxY);
            }
        }

        public void EndTransform() { }

        // ------------------------
        // Left/Right only balance logic (no Center)
        // ------------------------
        private void UpdateBalanceStateLR()
        {
            if (_cartPivot == null || _cargoObject == null) return;

            float x = _cartPivot.InverseTransformPoint(_cargoObject.position).x;

            if (_state == BalanceState.Left)
            {
                // 오른쪽으로 충분히 넘어가야 Right로 전환
                if (x > _switchZone) _state = BalanceState.Right;
            }
            else // Right
            {
                // 왼쪽으로 충분히 넘어가야 Left로 전환
                if (x < -_switchZone) _state = BalanceState.Left;
            }
        }

        /// <summary>
        /// rawA/rawB 중 어느 쪽이 "왼손 grab point"인지 확정.
        /// - _leftGrabPointRef가 있으면 그것에 더 가까운 쪽을 Left로 봄(권장)
        /// - 없으면 cartPivot 기준 localX로 더 왼쪽(X가 작은 쪽)을 Left로 봄
        /// </summary>
        private int ResolveLeftIdx(Vector3 rawA, Vector3 rawB)
        {
            if (_leftGrabPointRef != null)
            {
                float aToLeft = (rawA - _leftGrabPointRef.position).sqrMagnitude;
                float bToLeft = (rawB - _leftGrabPointRef.position).sqrMagnitude;
                return (aToLeft <= bToLeft) ? 0 : 1;
            }

            if (_cartPivot != null)
            {
                float ax = _cartPivot.InverseTransformPoint(rawA).x;
                float bx = _cartPivot.InverseTransformPoint(rawB).x;
                return (ax <= bx) ? 0 : 1;
            }

            return (rawA.x <= rawB.x) ? 0 : 1;
        }

        private float GetFollowForIndex(int index, int leftIdx)
        {
            bool isLeftPoint = (index == leftIdx);

            // Left 상태면 "왼손 포인트"만 lag, Right 상태면 "오른손 포인트"만 lag
            bool lagLeft = (_state == BalanceState.Left);
            bool lagRight = (_state == BalanceState.Right);

            if (isLeftPoint)
            {
                return lagLeft ? Mathf.Max(0.01f, _leftFollow) : 9999f;
            }
            else
            {
                return lagRight ? Mathf.Max(0.01f, _rightFollow) : 9999f;
            }
        }

        private static Vector3 ExpFollow(Vector3 current, Vector3 target, float follow, float dt)
        {
            // follow가 클수록 빠르게 따라감. 9999면 거의 즉시.
            float a = 1f - Mathf.Exp(-follow * dt);
            return Vector3.Lerp(current, target, a);
        }

        // ------------------------
        // SDK81 helper (unchanged)
        // ------------------------
        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;

            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);

            Vector3 planarDelta = p1planar - p0planar;
            Quaternion poseDir = planarDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(planarDelta, planeNormal)
                : Quaternion.LookRotation(Vector3.forward, planeNormal);

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = planarDelta.magnitude
            };
        }
    }
}
