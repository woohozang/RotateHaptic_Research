using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// V2 버전: 전략적 손 지연(Left/Right) 선택 가능 및 스케일 변조 제거.
    /// </summary>
    public class DynamicGrabV2 : MonoBehaviour, ITransformer
    {
        public enum LagHandSide { Left, Right }

        [Header("Strategic Settings")]
        [Tooltip("현재 어떤 손에 무게감(지연)을 줄지 결정합니다.")]
        public LagHandSide TargetHand = LagHandSide.Left;

        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Hand Identification")]
        [SerializeField] private Transform _leftReference = null;

        [Header("Dynamic Weights (Follow Speed)")]
        [Range(0.5f, 40f)] public float PosFollow = 20f;
        [Range(0.5f, 40f)] public float YawFollow = 20f;

        public struct TwoGrabPlaneState { public Pose Center; public float PlanarDistance; }

        private IGrabbable _grabbable;
        private Pose _localToTarget;
        private Vector3 _laggedPos;
        private bool _hasLagInit;
        private Vector3 _laggedDelta;
        private bool _hasDeltaInit;

        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

        public void BeginTransform()
        {
            var target = _grabbable.Transform;
            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 left, out Vector3 right);

            // 현재 설정된 타겟 손에 맞춰 지연 포지션 초기화
            _laggedPos = (TargetHand == LagHandSide.Left) ? left : right;
            _hasLagInit = true;

            var planeNormal = WorldPlaneNormal();
            // 지연된 손과 고정된 손을 조합하여 초기 상태 계산
            Vector3 anchorPos = (TargetHand == LagHandSide.Left) ? right : left;
            var state = TwoGrabPlane(_laggedPos, anchorPos, planeNormal);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);

            // 회전 지연 초기화
            Vector3 rawDelta = (TargetHand == LagHandSide.Left) ? (anchorPos - _laggedPos) : (_laggedPos - anchorPos);
            _laggedDelta = Vector3.ProjectOnPlane(rawDelta, planeNormal);
            _hasDeltaInit = true;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 left, out Vector3 right);

            if (!_hasLagInit) { _laggedPos = (TargetHand == LagHandSide.Left) ? left : right; _hasLagInit = true; }

            // 1. 전략적으로 선택된 손에만 지연(무게감) 적용
            Vector3 currentTargetPos = (TargetHand == LagHandSide.Left) ? left : right;
            Vector3 anchorHandPos = (TargetHand == LagHandSide.Left) ? right : left;

            float a = 1f - Mathf.Exp(-PosFollow * Time.deltaTime);
            _laggedPos = Vector3.Lerp(_laggedPos, currentTargetPos, a);

            // 2. 평면 노멀 및 포즈 계산
            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane_Strategic(TargetHand, _laggedPos, anchorHandPos, planeNormal, ref _laggedDelta, ref _hasDeltaInit, YawFollow);

            // 3. 위치 및 회전 적용 (Y축 0 고정 유지)
            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);
            target.position = new Vector3(result.position.x, 0f, result.position.z);
            target.rotation = result.rotation;

            // 4. 스케일은 건드리지 않음 (원래 스케일 유지)
        }

        public void EndTransform() { }

        // --- 핵심 수식: 타겟 손에 따른 방향성 보정 ---
        private static TwoGrabPlaneState TwoGrabPlane_Strategic(LagHandSide side, Vector3 lagP, Vector3 anchorP, Vector3 normal, ref Vector3 delta, ref bool init, float yaw)
        {
            Vector3 centroid = lagP * 0.5f + anchorP * 0.5f;

            // 방향 벡터는 항상 '왼손 -> 오른손' 방향을 유지해야 LookRotation이 안정적임
            Vector3 rawDelta = (side == LagHandSide.Left) ? (anchorP - lagP) : (lagP - anchorP);
            Vector3 rawPlanar = Vector3.ProjectOnPlane(rawDelta, normal);

            if (!init) { delta = rawPlanar; init = true; }
            delta = Vector3.Lerp(delta, rawPlanar, 1f - Mathf.Exp(-yaw * Time.deltaTime));

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, Quaternion.LookRotation(delta, normal)),
                PlanarDistance = rawPlanar.magnitude
            };
        }

        private void GetLeftRightPositions(Vector3 pA, Vector3 pB, out Vector3 left, out Vector3 right)
        {
            if (_leftReference == null) { left = pA; right = pB; return; }
            if ((pA - _leftReference.position).sqrMagnitude <= (pB - _leftReference.position).sqrMagnitude) { left = pA; right = pB; }
            else { left = pB; right = pA; }
        }

        private Vector3 WorldPlaneNormal() => (_planeTransform != null ? _planeTransform : _grabbable.Transform).TransformDirection(_localPlaneNormal).normalized;

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 normal)
        {
            Vector3 delta = Vector3.ProjectOnPlane(p1 - p0, normal);
            return new TwoGrabPlaneState() { Center = new Pose((p0 + p1) * 0.5f, Quaternion.LookRotation(delta, normal)), PlanarDistance = delta.magnitude };
        }
    }
}