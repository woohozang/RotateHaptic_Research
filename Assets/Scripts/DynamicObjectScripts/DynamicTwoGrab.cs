using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    public class DynamicTwoGrab : MonoBehaviour, ITransformer
    {
        [Header("Plane")]
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Refs")]
        [SerializeField] private Transform _cartPivot;
        [SerializeField] private Transform _cargoObject;

        [Header("Grab Points")]
        [SerializeField] private Transform _leftGrabPointRef;
        [SerializeField] private Transform _rightGrabPointRef;

        [Header("Switch")]
        [SerializeField] private float _switchZone = 0.02f;

        [Header("Lag (무게감)")]
        [SerializeField] private float _leftFollow = 1f;
        [SerializeField] private float _rightFollow = 1f;

        [Header("Offset Strength")]
        [SerializeField] private float _offsetForce = 0.15f;

        [Header("Jitter")]
        [SerializeField] private float _posJitterThreshold = 0.0005f;

        private IGrabbable _grabbable;

        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        private Vector3 _filtA;
        private Vector3 _filtB;
        private bool _filterInitialized;

        private float _fixedY;

        private enum BalanceState { Left, Right }
        private BalanceState _state = BalanceState.Left;

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

            // 🔥 Y 고정 값 저장
            _fixedY = target.position.y;

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
            if (_grabbable == null || _grabbable.GrabPoints.Count < 2)
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

            int leftIdx = ResolveLeftIdx(rawA, rawB);

            float followA = GetFollowForIndex(0, leftIdx);
            float followB = GetFollowForIndex(1, leftIdx);

            float jt2 = _posJitterThreshold * _posJitterThreshold;
            if ((rawA - _filtA).sqrMagnitude < jt2) rawA = _filtA;
            if ((rawB - _filtB).sqrMagnitude < jt2) rawB = _filtB;

            _filtA = ExpFollow(_filtA, rawA, followA, Time.deltaTime);
            _filtB = ExpFollow(_filtB, rawB, followB, Time.deltaTime);

            // 🔥 COM 기반 오프셋 강화
            float bias = 0f;
            if (_cartPivot != null && _cargoObject != null)
            {
                float x = _cartPivot.InverseTransformPoint(_cargoObject.position).x;
                bias = Mathf.Clamp(x * 10f, -1f, 1f);
            }

            Vector3 dir = _cartPivot.right * bias;

            // 🔥 강한 오프셋 적용
            Vector3 offset = dir * _offsetForce;

            _filtA += offset;
            _filtB += offset;

            // 🔥 추가 강제 밀림 (체감 강화)
            _filtA += dir * 0.02f;
            _filtB += dir * 0.02f;

            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane(_filtA, _filtB, planeNormal);

            float prevDist = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
            float scaleDelta = prevDist != 0 ? state.PlanarDistance / prevDist : 1f;

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);

            // 🔥 Y축 완전 고정 (핵심)
            Vector3 fixedPos = result.position;
            fixedPos.y = _fixedY;

            target.position = fixedPos;
            target.rotation = result.rotation;
        }

        public void EndTransform() { }

        private void UpdateBalanceStateLR()
        {
            if (_cartPivot == null || _cargoObject == null) return;

            float x = _cartPivot.InverseTransformPoint(_cargoObject.position).x;

            if (_state == BalanceState.Left)
            {
                if (x > _switchZone) _state = BalanceState.Right;
            }
            else
            {
                if (x < -_switchZone) _state = BalanceState.Left;
            }
        }

        private int ResolveLeftIdx(Vector3 rawA, Vector3 rawB)
        {
            float aToLeft = (rawA - _leftGrabPointRef.position).sqrMagnitude;
            float bToLeft = (rawB - _leftGrabPointRef.position).sqrMagnitude;
            return (aToLeft <= bToLeft) ? 0 : 1;
        }

        private float GetFollowForIndex(int index, int leftIdx)
        {
            bool isLeft = (index == leftIdx);

            if (_state == BalanceState.Left)
                return isLeft ? _leftFollow : 9999f;
            else
                return isLeft ? 9999f : _rightFollow;
        }

        private static Vector3 ExpFollow(Vector3 current, Vector3 target, float follow, float dt)
        {
            float a = 1f - Mathf.Exp(-follow * dt);
            return Vector3.Lerp(current, target, a);
        }

        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = (p0 + p1) * 0.5f;

            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);

            Vector3 planarDelta = p1planar - p0planar;

            Quaternion rotation = planarDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(planarDelta, planeNormal)
                : Quaternion.identity;

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, rotation),
                PlanarDistance = planarDelta.magnitude
            };
        }
    }
}