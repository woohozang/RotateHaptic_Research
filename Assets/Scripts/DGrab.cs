using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    public class DGrab : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Refs")]
        [SerializeField] private Transform _cartPivot;
        [SerializeField] private Transform _cargoObject;

        [Header("Grab References")]
        [SerializeField] private Transform _leftReference;
        [SerializeField] private Transform _rightReference;

        [Header("Lag Settings")]
        [SerializeField] private float _leftPosFollow = 4f;
        [SerializeField] private float _rightPosFollow = 4f;
        [SerializeField] private float _leftYawFollow = 4f;
        [SerializeField] private float _rightYawFollow = 4f;

        // 🔥 즉각적인 추적을 위한 높은 값
        private const float INSTANT_FOLLOW = 100f;

        [Header("Bias & Smoothing")]
        [SerializeField] private float _biasScale = 5f;
        [SerializeField] private float _smoothingFactor = 10f; // 오프셋 전환의 부드러움 정도

        [Header("Constraints")]
        [SerializeField] private TwoGrabPlaneConstraints _constraints;

        private IGrabbable _grabbable;
        private Vector3 _leftLagPos;
        private Vector3 _rightLagPos;
        private bool _init;
        private Vector3 _laggedDelta;
        private bool _deltaInit;

        // 🔥 상태 대신 부드러운 가중치 사용
        private float _smoothedBias = 0f;

        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

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

            GetLeftRight(grabA.position, grabB.position, out var left, out var right);

            _leftLagPos = left;
            _rightLagPos = right;
            _init = true;

            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane(_leftLagPos, _rightLagPos, planeNormal);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(state.PlanarDistance, target.worldToLocalMatrix);

            _laggedDelta = Vector3.ProjectOnPlane(right, planeNormal) - Vector3.ProjectOnPlane(left, planeNormal);
            _deltaInit = true;

            // 초기 바이어스 설정
            _smoothedBias = CalculateRawBias();
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            GetLeftRight(grabA.position, grabB.position, out var left, out var right);

            // 🔥 1. 바이어스 값을 부드럽게 보간 (끊김 해결의 핵심)
            float rawBias = CalculateRawBias();
            _smoothedBias = Mathf.Lerp(_smoothedBias, rawBias, Time.deltaTime * _smoothingFactor);

            // 🔥 2. 바이어스에 따라 저항값(Follow)을 부드럽게 섞음
            // _smoothedBias가 -1이면 왼쪽이 최대 저항, 1이면 오른쪽이 최대 저항
            float leftWeight = Mathf.Clamp01(-_smoothedBias);
            float rightWeight = Mathf.Clamp01(_smoothedBias);

            float currentLeftPosFollow = Mathf.Lerp(INSTANT_FOLLOW, _leftPosFollow, leftWeight);
            float currentRightPosFollow = Mathf.Lerp(INSTANT_FOLLOW, _rightPosFollow, rightWeight);

            _leftLagPos = ExpFollow(_leftLagPos, left, currentLeftPosFollow);
            _rightLagPos = ExpFollow(_rightLagPos, right, currentRightPosFollow);

            // Yaw 저항도 부드럽게 보간
            float currentYawFollow = Mathf.Lerp(_leftYawFollow, _rightYawFollow, (_smoothedBias + 1f) * 0.5f);

            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane_WithLaggedDelta(
                _leftLagPos, _rightLagPos, planeNormal,
                ref _laggedDelta, ref _deltaInit, currentYawFollow);

            // Scale 및 Pose 적용 로직 (기존과 동일)
            float prevDist = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDist != 0 ? state.PlanarDistance / prevDist : 1f;
            float targetScale = scaleDelta * target.localScale.x;

            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain) targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                if (_constraints.MaxScale.Constrain) targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
            }

            target.localScale = (targetScale / target.localScale.x) * target.localScale;

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);
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

        private float CalculateRawBias()
        {
            if (_cartPivot == null || _cargoObject == null) return 0f;
            float x = _cartPivot.InverseTransformPoint(_cargoObject.position).x;
            return Mathf.Clamp(x * _biasScale, -1f, 1f);
        }

        private void GetLeftRight(Vector3 a, Vector3 b, out Vector3 left, out Vector3 right)
        {
            float da = (a - _leftReference.position).sqrMagnitude;
            float db = (b - _leftReference.position).sqrMagnitude;

            if (da < db) { left = a; right = b; }
            else { left = b; right = a; }
        }

        private Vector3 ExpFollow(Vector3 current, Vector3 target, float follow)
        {
            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            return Vector3.Lerp(current, target, a);
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

            return new TwoGrabPlaneState() { Center = new Pose(centroid, poseDir), PlanarDistance = planarDelta.magnitude };
        }

        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(
            Vector3 leftP, Vector3 rightP, Vector3 planeNormal,
            ref Vector3 laggedDelta, ref bool hasInit, float follow)
        {
            Vector3 lPlanar = Vector3.ProjectOnPlane(leftP, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightP, planeNormal);
            Vector3 rawDelta = rPlanar - lPlanar;

            if (!hasInit) { laggedDelta = rawDelta; hasInit = true; }

            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);

            Quaternion rot = laggedDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(laggedDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState() { Center = new Pose((leftP + rightP) * 0.5f, rot), PlanarDistance = rawDelta.magnitude };
        }
    }
}