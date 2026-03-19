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

        [Header("Bias")]
        [SerializeField] private float _biasScale = 5f;
        [SerializeField] private float _switchDeadZone = 0.02f;

        [Header("Constraints")]
        [SerializeField] private TwoGrabPlaneConstraints _constraints;

        private IGrabbable _grabbable;

        private Vector3 _leftLagPos;
        private Vector3 _rightLagPos;
        private bool _init;

        private Vector3 _laggedDelta;
        private bool _deltaInit;

        private enum State { Left, Right }
        private State _state = State.Left;

        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        // =========================
        // Struct
        // =========================
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

        // =========================

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
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;

            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            GetLeftRight(grabA.position, grabB.position, out var left, out var right);

            float bias = GetBias();
            UpdateState(bias);

            float leftFollow = (_state == State.Left) ? _leftPosFollow : 9999f;
            float rightFollow = (_state == State.Right) ? _rightPosFollow : 9999f;

            _leftLagPos = ExpFollow(_leftLagPos, left, leftFollow);
            _rightLagPos = ExpFollow(_rightLagPos, right, rightFollow);

            float yawFollow = (_state == State.Left) ? _leftYawFollow : _rightYawFollow;

            var planeNormal = WorldPlaneNormal();

            var state = TwoGrabPlane_WithLaggedDelta(
                _leftLagPos, _rightLagPos, planeNormal,
                ref _laggedDelta, ref _deltaInit, yawFollow);

            // 🔥 Scale 처리
            float prevDist = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDist != 0 ? state.PlanarDistance / prevDist : 1f;

            float targetScale = scaleDelta * target.localScale.x;

            if (_constraints != null)
            {
                if (_constraints.MinScale.Constrain)
                    targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);

                if (_constraints.MaxScale.Constrain)
                    targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);
            }

            target.localScale = (targetScale / target.localScale.x) * target.localScale;

            // 🔥 위치 + 회전
            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            // 🔥 Y축 고정
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

        // =========================

        private float GetBias()
        {
            float x = _cartPivot.InverseTransformPoint(_cargoObject.position).x;
            return Mathf.Clamp(x * _biasScale, -1f, 1f);
        }

        private void UpdateState(float bias)
        {
            if (_state == State.Left && bias > _switchDeadZone)
                _state = State.Right;
            else if (_state == State.Right && bias < -_switchDeadZone)
                _state = State.Left;
        }

        private void GetLeftRight(Vector3 a, Vector3 b, out Vector3 left, out Vector3 right)
        {
            float da = (a - _leftReference.position).sqrMagnitude;
            float db = (b - _leftReference.position).sqrMagnitude;

            if (da < db)
            {
                left = a;
                right = b;
            }
            else
            {
                left = b;
                right = a;
            }
        }

        private Vector3 ExpFollow(Vector3 current, Vector3 target, float follow)
        {
            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            return Vector3.Lerp(current, target, a);
        }

        public static TwoGrabPlaneState TwoGrabPlane(
            Vector3 p0, Vector3 p1, Vector3 planeNormal)
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

        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(
            Vector3 leftP, Vector3 rightP, Vector3 planeNormal,
            ref Vector3 laggedDelta, ref bool hasInit, float follow)
        {
            Vector3 lPlanar = Vector3.ProjectOnPlane(leftP, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightP, planeNormal);

            Vector3 rawDelta = rPlanar - lPlanar;

            if (!hasInit)
            {
                laggedDelta = rawDelta;
                hasInit = true;
            }

            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);

            Quaternion rot = laggedDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(laggedDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose((leftP + rightP) * 0.5f, rot),
                PlanarDistance = rawDelta.magnitude
            };
        }
    }
}