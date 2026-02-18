using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// [최종 수정본] 이미지 레이아웃 반영 및 에러 해결 버전
    /// </summary>
    public class DynamicWeightTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional]
        private Transform _planeTransform = null;

        [SerializeField, Optional]
        private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Left lag - rotation feel")]
        [Tooltip("값이 낮을수록 회전(yaw)도 더 무겁게 느껴짐(느리게). (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _leftYawFollow = 40f;

        [Header("Left hand identification (IMPORTANT)")]
        [Tooltip("왼쪽 손잡이 위치 기준 마커. 이 Transform에 더 가까운 GrabPoint를 '왼손'으로 판정합니다.")]
        [SerializeField]
        private Transform _leftReference = null;

        [Header("Left lag (visual heavy feel)")]
        [Tooltip("값이 낮을수록 더 무겁게(느리게) 따라옵니다. (권장: 2~10)")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _leftPosFollow = 40f;

        // 에러 CS0246 해결을 위한 구조체 정의
        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

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

        // 외부(Apple Controller)에서 수치를 조절하기 위한 프로퍼티
        public float LeftPosFollow { get => _leftPosFollow; set => _leftPosFollow = value; }
        public float LeftYawFollow { get => _leftYawFollow; set => _leftYawFollow = value; }

        private IGrabbable _grabbable;
        private Pose _localToTarget;
        private float _localMagnitudeToTarget;
        private Vector3 _leftLagPos;
        private bool _hasLagInit;
        private Vector3 _laggedPlanarDelta;
        private bool _hasDeltaInit;

        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

        private Vector3 WorldPlaneNormal()
        {
            Transform t = _planeTransform != null ? _planeTransform : _grabbable.Transform;
            return t.TransformDirection(_localPlaneNormal).normalized;
        }

        public void BeginTransform()
        {
            var target = _grabbable.Transform;
            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);

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
            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);

            if (!_hasLagInit) { _leftLagPos = leftPos; _hasLagInit = true; }

            // 실시간 변수 적용 (지연 효과 연산)
            float a = 1f - Mathf.Exp(-_leftPosFollow * Time.deltaTime);
            _leftLagPos = Vector3.Lerp(_leftLagPos, leftPos, a);

            var planeNormal = WorldPlaneNormal();
            var twoGrabPlaneState = TwoGrabPlane_WithLaggedDelta(_leftLagPos, rightPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, _leftYawFollow);

            float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDistInWorld != 0 ? twoGrabPlaneState.PlanarDistance / prevDistInWorld : 1f;
            target.localScale = (scaleDelta * target.localScale.x / target.localScale.x) * target.localScale;

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, twoGrabPlaneState.Center);
            target.position = result.position;
            target.rotation = result.rotation;
        }

        // 에러 CS0535 해결을 위한 필수 메서드
        public void EndTransform() { }

        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(Vector3 leftP, Vector3 rightP, Vector3 planeNormal, ref Vector3 laggedDelta, ref bool hasInit, float yawFollow)
        {
            Vector3 centroid = leftP * 0.5f + rightP * 0.5f;
            Vector3 lPlanar = Vector3.ProjectOnPlane(leftP, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightP, planeNormal);
            Vector3 rawDelta = rPlanar - lPlanar;
            if (!hasInit) { laggedDelta = rawDelta; hasInit = true; }
            float a = 1f - Mathf.Exp(-yawFollow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(laggedDelta, planeNormal)), PlanarDistance = rawDelta.magnitude };
        }

        private void GetLeftRightPositions(Vector3 pA, Vector3 pB, out Vector3 left, out Vector3 right)
        {
            if (_leftReference == null) { left = pA; right = pB; return; }
            if ((pA - _leftReference.position).sqrMagnitude <= (pB - _leftReference.position).sqrMagnitude) { left = pA; right = pB; }
            else { left = pB; right = pA; }
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;
            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);
            Vector3 planarDelta = p1planar - p0planar;
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(planarDelta, planeNormal)), PlanarDistance = planarDelta.magnitude };
        }
    }
}