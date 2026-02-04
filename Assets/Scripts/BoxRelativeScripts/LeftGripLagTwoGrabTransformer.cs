using System;
using UnityEngine;

using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// TwoGrabPlaneTransformer 기반.
    /// 두 GrabPoint 중 "왼쪽 기준 Transform"에 더 가까운 GrabPoint를 왼손으로 판정하고,
    /// 그 포인트만 지연(저역통과)시켜 무게감(시각적 지연)을 만듭니다.
    /// </summary>
    public class LeftLagTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional]
        private Transform _planeTransform = null;

        [SerializeField, Optional]
        private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);
        
        [Header("Left lag - rotation feel")]
        [Tooltip("값이 낮을수록 회전(yaw)도 더 무겁게 느껴짐(느리게). (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _leftYawFollow = 6f;

        private Vector3 _laggedPlanarDelta;
        private bool _hasDeltaInit;

        [Header("Left hand identification (IMPORTANT)")]
        [Tooltip("왼쪽 손잡이 위치 기준 마커(예: LeftGrabPoint). 이 Transform에 더 가까운 GrabPoint를 '왼손'으로 판정합니다.")]
        [SerializeField]
        private Transform _leftReference = null;

        [Header("Left lag (visual heavy feel)")]
        [Tooltip("값이 낮을수록 더 무겁게(느리게) 따라옵니다. (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _leftPosFollow = 6f;

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

        // --- left lag state ---
        private Vector3 _leftLagPos;
        private bool _hasLagInit;

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

            Vector3 leftPos, rightPos;
            GetLeftRightPositions(grabA.position, grabB.position, out leftPos, out rightPos);

            // lag init
            _leftLagPos = leftPos;
            _hasLagInit = true;

            var planeNormal = WorldPlaneNormal();
            var twoGrabPlaneState = TwoGrabPlane(_leftLagPos, rightPos, planeNormal);

            _localToTarget = WorldToLocalPose(twoGrabPlaneState.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(twoGrabPlaneState.PlanarDistance, target.worldToLocalMatrix);

            Vector3 p0planar = Vector3.ProjectOnPlane(_leftLagPos, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(rightPos, planeNormal);
            _laggedPlanarDelta = (p1planar - p0planar);
            _hasDeltaInit = true;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;

            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            Vector3 leftPos, rightPos;
            GetLeftRightPositions(grabA.position, grabB.position, out leftPos, out rightPos);

            // left lag update (position only)
            if (!_hasLagInit)
            {
                _leftLagPos = leftPos;
                _hasLagInit = true;
            }
            float a = 1f - Mathf.Exp(-_leftPosFollow * Time.deltaTime);
            _leftLagPos = Vector3.Lerp(_leftLagPos, leftPos, a);

            var planeNormal = WorldPlaneNormal();
            //var twoGrabPlaneState = TwoGrabPlane(_leftLagPos, rightPos, planeNormal);
            var twoGrabPlaneState = TwoGrabPlane_WithLaggedDelta(_leftLagPos, rightPos, planeNormal,
    ref _laggedPlanarDelta, ref _hasDeltaInit, _leftYawFollow);
            float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDistInWorld != 0 ? twoGrabPlaneState.PlanarDistance / prevDistInWorld : 1f;

            float targetScale = scaleDelta * target.localScale.x;
            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain)
                {
                    targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                }
                if (_constraints.MaxScale.Constrain)
                {
                    targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
                }
            }

            target.localScale = (targetScale / target.localScale.x) * target.localScale;

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, twoGrabPlaneState.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            if (_constraints != null)
            {
                target.position = ConstrainAlongDirection(
                    target.position,
                    target.parent != null ? target.parent.position : Vector3.zero,
                    planeNormal,
                    _constraints.MinY, _constraints.MaxY);
            }
        }
        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(
    Vector3 leftP, Vector3 rightP, Vector3 planeNormal,
    ref Vector3 laggedDelta, ref bool hasInit, float yawFollow)
        {
            Vector3 centroid = leftP * 0.5f + rightP * 0.5f;

            Vector3 lPlanar = Vector3.ProjectOnPlane(leftP, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightP, planeNormal);

            Vector3 rawDelta = rPlanar - lPlanar;

            if (!hasInit)
            {
                laggedDelta = rawDelta;
                hasInit = true;
            }

            // 방향(회전 느낌) 지연: planarDelta 자체를 저역통과
            float a = 1f - Mathf.Exp(-yawFollow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);

            Quaternion poseDir = laggedDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(laggedDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = rawDelta.magnitude // 스케일은 실제 거리로 유지(안정)
            };
        }

        public void EndTransform() { }

        /// <summary>
        /// 두 포인트 중 _leftReference에 더 가까운 쪽을 left로 선택.
        /// _leftReference가 없으면 grabA를 left로 가정.
        /// </summary>
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

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;

            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);

            Vector3 planarDelta = p1planar - p0planar;

            // planarDelta가 너무 작으면 LookRotation이 불안정해질 수 있음 → 최소 안정화
            Quaternion poseDir = planarDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(planarDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = planarDelta.magnitude
            };
        }

        #region Inject
        public void InjectOptionalPlaneTransform(Transform planeTransform) => _planeTransform = planeTransform;
        public void InjectOptionalConstraints(TwoGrabPlaneConstraints constraints) => _constraints = constraints;
        #endregion
    }
}
