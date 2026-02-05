/*
 * Based on Meta Interaction SDK v81 TwoGrabPlaneTransformer
 * Modified: Fixed offset (no catch-up) for left-hand contribution
 */

using System;
using UnityEngine;

using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    public class LeftLagTwoGrabPlaneTransformer_FixedOffset : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional]
        private Transform _planeTransform = null;

        [SerializeField, Optional]
        private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Left hand identification")]
        [Tooltip("왼쪽 손잡이 위치 기준 마커(예: LeftGrabPoint). 이 Transform에 더 가까운 GrabPoint를 '왼손'으로 판정합니다.")]
        [SerializeField]
        private Transform _leftReference = null;

        [Header("Fixed offset (NO catch-up)")]
        [Tooltip("왼손 위치 오프셋(미터). 클수록 왼손이 더 '뒤에' 남습니다.")]
        [Min(0f)]
        [SerializeField] private float _posOffsetMeters = 0.08f;

        [Tooltip("회전(yaw) 오프셋(도). 클수록 회전 시 왼손 지연이 더 크게 느껴집니다.")]
        [Range(0f, 90f)]
        [SerializeField] private float _yawOffsetDegrees = 10f;

        [Serializable]
        public class TwoGrabPlaneConstraints
        {
            public FloatConstraint MaxScale;
            public FloatConstraint MinScale;
            public FloatConstraint MaxY;
            public FloatConstraint MinY;
        }

        [SerializeField]
        private TwoGrabPlaneConstraints _constraints;

        public TwoGrabPlaneConstraints Constraints
        {
            get => _constraints;
            set => _constraints = value;
        }

        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

        private IGrabbable _grabbable;

        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        // --- fixed offset state ---
        private Vector3 _leftLagPos;          // "가상 왼손 포인트"
        private bool _hasLagInit;

        private Vector3 _laggedPlanarDelta;   // "가상 planarDelta 방향"
        private bool _hasDeltaInit;

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

            Vector3 leftPosRaw, rightPosRaw;
            GetLeftRightPositions(grabA.position, grabB.position, out leftPosRaw, out rightPosRaw);

            var planeNormal = WorldPlaneNormal();

            // init left lag pos: 시작 시에는 raw와 동일하게
            _leftLagPos = leftPosRaw;
            _hasLagInit = true;

            // init delta
            Vector3 lPlanar = Vector3.ProjectOnPlane(_leftLagPos, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightPosRaw, planeNormal);
            _laggedPlanarDelta = (rPlanar - lPlanar);
            _hasDeltaInit = true;

            var state = TwoGrabPlane_WithFixedOffsets(
                leftPosRaw, rightPosRaw, planeNormal,
                ref _leftLagPos, ref _hasLagInit,
                ref _laggedPlanarDelta, ref _hasDeltaInit,
                _posOffsetMeters, _yawOffsetDegrees);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(state.PlanarDistance, target.worldToLocalMatrix);
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            Vector3 leftPosRaw, rightPosRaw;
            GetLeftRightPositions(grabA.position, grabB.position, out leftPosRaw, out rightPosRaw);

            var planeNormal = WorldPlaneNormal();

            var state = TwoGrabPlane_WithFixedOffsets(
                leftPosRaw, rightPosRaw, planeNormal,
                ref _leftLagPos, ref _hasLagInit,
                ref _laggedPlanarDelta, ref _hasDeltaInit,
                _posOffsetMeters, _yawOffsetDegrees);

            // --- scale (원본과 동일) ---
            float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDistInWorld != 0 ? state.PlanarDistance / prevDistInWorld : 1f;

            float targetScale = scaleDelta * target.localScale.x;
            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain)
                    targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                if (_constraints.MaxScale.Constrain)
                    targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
            }

            target.localScale = (targetScale / target.localScale.x) * target.localScale;

            // --- pose (원본과 동일) ---
            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            // --- Y constraint (원본과 동일) ---
            if (_constraints != null)
            {
                target.position = ConstrainAlongDirection(
                    target.position,
                    target.parent != null ? target.parent.position : Vector3.zero,
                    planeNormal,
                    _constraints.MinY, _constraints.MaxY);
            }
        }

        public void EndTransform() { }

        private void GetLeftRightPositions(Vector3 pA, Vector3 pB, out Vector3 left, out Vector3 right)
        {
            if (_leftReference == null)
            {
                left = pA;
                right = pB;
                return;
            }

            float dA = (pA - _leftReference.position).sqrMagnitude;
            float dB = (pB - _leftReference.position).sqrMagnitude;

            if (dA <= dB)
            {
                left = pA;
                right = pB;
            }
            else
            {
                left = pB;
                right = pA;
            }
        }

        /// <summary>
        /// 핵심:
        /// 1) 왼손 위치: rawLeft와 lagLeft의 거리가 offsetMeters를 넘으면 lagLeft를 따라오게 하되,
        ///    항상 "offsetMeters"만큼 떨어진 상태로 유지 (따라잡지 않음)
        /// 2) 회전: raw planarDelta 방향과 laggedDelta 방향의 각도차를 yawOffsetDeg로 유지 (따라잡지 않음)
        /// </summary>
        private static TwoGrabPlaneState TwoGrabPlane_WithFixedOffsets(
            Vector3 rawLeft, Vector3 rawRight, Vector3 planeNormal,
            ref Vector3 lagLeft, ref bool hasLagInit,
            ref Vector3 laggedDelta, ref bool hasDeltaInit,
            float offsetMeters, float yawOffsetDeg)
        {
            if (!hasLagInit)
            {
                lagLeft = rawLeft;
                hasLagInit = true;
            }

            // --- 1) Left position: fixed distance leash ---
            // 거리(offsetMeters) 이상 벌어지면 lagLeft를 "offsetMeters만큼 떨어진 위치"로 당겨옴
            if (offsetMeters > 0f)
            {
                Vector3 diff = rawLeft - lagLeft;
                float dist = diff.magnitude;
                if (dist > offsetMeters && dist > 1e-6f)
                {
                    // lagLeft가 rawLeft를 따라오되, 정확히 offsetMeters만큼 뒤에 남게
                    lagLeft = rawLeft - (diff / dist) * offsetMeters;
                }
                // dist <= offsetMeters면 lagLeft는 그대로 (따라잡지 않음)
            }
            else
            {
                lagLeft = rawLeft; // offset 0이면 1:1
            }

            // --- planar projections ---
            Vector3 lPlanar = Vector3.ProjectOnPlane(lagLeft, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rawRight, planeNormal);

            Vector3 rawDelta = rPlanar - Vector3.ProjectOnPlane(rawLeft, planeNormal); // 스케일용(실제 거리)
            Vector3 useDelta = rPlanar - lPlanar;                                      // 방향용(지연 적용된 left)

            // --- 2) Rotation: fixed angular offset on delta direction ---
            if (!hasDeltaInit)
            {
                laggedDelta = useDelta;
                hasDeltaInit = true;
            }

            if (yawOffsetDeg > 0f && useDelta.sqrMagnitude > 1e-8f && laggedDelta.sqrMagnitude > 1e-8f)
            {
                // laggedDelta를 "useDelta를 따라가되, 각도 차이를 yawOffsetDeg로 유지"하도록 업데이트
                float ang = Vector3.Angle(laggedDelta, useDelta);
                if (ang > yawOffsetDeg + 0.001f)
                {
                    // 각도 차이가 너무 크면, 필요한 만큼만 따라가서 딱 yawOffsetDeg 남기기
                    float moveAngle = ang - yawOffsetDeg;
                    float t = Mathf.Clamp01(moveAngle / ang); // 0~1
                    laggedDelta = Vector3.Slerp(laggedDelta, useDelta, t);
                }
                // ang <= yawOffsetDeg면 그대로 유지 (따라잡지 않음)
            }
            else
            {
                laggedDelta = useDelta; // yawOffset 0이면 1:1
            }

            Vector3 centroid = lagLeft * 0.5f + rawRight * 0.5f;

            Quaternion poseDir = laggedDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(laggedDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                // 스케일은 "실제 두 손 거리" 기반으로 유지 (안정)
                PlanarDistance = Vector3.ProjectOnPlane(rawRight, planeNormal).magnitude == 0f
                    ? useDelta.magnitude
                    : (Vector3.ProjectOnPlane(rawRight, planeNormal) - Vector3.ProjectOnPlane(rawLeft, planeNormal)).magnitude
            };
        }

        #region Inject
        public void InjectOptionalPlaneTransform(Transform planeTransform) => _planeTransform = planeTransform;
        public void InjectOptionalConstraints(TwoGrabPlaneConstraints constraints) => _constraints = constraints;
        #endregion
    }
}
