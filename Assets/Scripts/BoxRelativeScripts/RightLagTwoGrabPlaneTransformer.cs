using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// TwoGrabPlaneTransformer 기반.
    /// 두 GrabPoint 중 "오른쪽 기준 Transform"에 더 가까운 GrabPoint를 오른손으로 판정하고,
    /// 그 포인트만 지연(저역통과)시켜 시각적 무게감(오른손 오프셋)을 만듭니다.
    /// </summary>
    public class RightLagTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional]
        private Transform _planeTransform = null;

        [SerializeField, Optional]
        private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Right lag - rotation feel")]
        [Tooltip("값이 낮을수록 회전(yaw)이 더 무겁게 느껴짐. (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _rightYawFollow = 6f;

        private Vector3 _laggedPlanarDelta;
        private bool _hasDeltaInit;

        [Header("Right hand identification")]
        [Tooltip("오른쪽 손잡이 위치 기준 마커. 이 Transform에 더 가까운 쪽을 '오른손'으로 판정합니다.")]
        [SerializeField]
        private Transform _rightReference = null;

        [Header("Right lag (visual heavy feel)")]
        [Tooltip("값이 낮을수록 오른손이 더 느리게 따라옵니다. (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _rightPosFollow = 6f;

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

        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

        private IGrabbable _grabbable;
        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        // --- right lag state ---
        private Vector3 _rightLagPos;
        private bool _hasLagInit;

        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

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

            // 오른손과 왼손의 위치를 판정하여 가져옴
            GetRightLeftPositions(grabA.position, grabB.position, out Vector3 rightPos, out Vector3 leftPos);

            // 오른손 지연 초기화
            _rightLagPos = rightPos;
            _hasLagInit = true;

            var planeNormal = WorldPlaneNormal();
            var twoGrabPlaneState = TwoGrabPlane(leftPos, _rightLagPos, planeNormal);

            _localToTarget = WorldToLocalPose(twoGrabPlaneState.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(twoGrabPlaneState.PlanarDistance, target.worldToLocalMatrix);

            Vector3 pLplanar = Vector3.ProjectOnPlane(leftPos, planeNormal);
            Vector3 pRplanar = Vector3.ProjectOnPlane(_rightLagPos, planeNormal);
            _laggedPlanarDelta = (pRplanar - pLplanar);
            _hasDeltaInit = true;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            GetRightLeftPositions(grabA.position, grabB.position, out Vector3 rightPos, out Vector3 leftPos);

            // 오른손 지연 업데이트 (Position)
            if (!_hasLagInit)
            {
                _rightLagPos = rightPos;
                _hasLagInit = true;
            }
            float a = 1f - Mathf.Exp(-_rightPosFollow * Time.deltaTime);
            _rightLagPos = Vector3.Lerp(_rightLagPos, rightPos, a);

            var planeNormal = WorldPlaneNormal();

            // 오른손 지연된 위치(_rightLagPos)를 사용하여 트랜스폼 계산
            var twoGrabPlaneState = TwoGrabPlane_WithLaggedDelta(leftPos, _rightLagPos, planeNormal,
                                    ref _laggedPlanarDelta, ref _hasDeltaInit, _rightYawFollow);

            float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDistInWorld != 0 ? twoGrabPlaneState.PlanarDistance / prevDistInWorld : 1f;

            float targetScale = scaleDelta * target.localScale.x;
            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain) targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                if (_constraints.MaxScale.Constrain) targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
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

            Vector3 rawDelta = rPlanar - lPlanar; // 왼손에서 오른손을 향하는 벡터

            if (!hasInit)
            {
                laggedDelta = rawDelta;
                hasInit = true;
            }

            // 회전(방향) 지연 적용
            float a = 1f - Mathf.Exp(-yawFollow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);

            Quaternion poseDir = laggedDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(laggedDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = rawDelta.magnitude
            };
        }

        public void EndTransform() { }

        /// <summary>
        /// _rightReference에 더 가까운 쪽을 Right로 선택.
        /// </summary>
        private void GetRightLeftPositions(Vector3 pA, Vector3 pB, out Vector3 right, out Vector3 left)
        {
            if (_rightReference == null)
            {
                right = pA;
                left = pB;
                return;
            }

            float dA = (pA - _rightReference.position).sqrMagnitude;
            float dB = (pB - _rightReference.position).sqrMagnitude;

            if (dA <= dB) // A가 기준점에 더 가까우면 A가 오른손
            {
                right = pA;
                left = pB;
            }
            else
            {
                right = pB;
                left = pA;
            }
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;
            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);
            Vector3 planarDelta = p1planar - p0planar;

            Quaternion poseDir = planarDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(planarDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = planarDelta.magnitude
            };
        }
    }
}