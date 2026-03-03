using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    public class DynamicWeightTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        public enum TargetLagSide { Left, Right }

        [Header("Strategic Settings")]
        [Tooltip("상자의 위치에 따라 선택하세요. Left면 왼쪽 핸들, Right면 오른쪽 핸들이 무거워집니다.")]
        public TargetLagSide CurrentTargetSide = TargetLagSide.Left;

        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Left lag Settings")]
        [Range(0.5f, 40f)] public float LeftYawFollow = 1.5f; //
        [Range(0.5f, 40f)] public float LeftPosFollow = 20f; //

        [Header("Left hand identification References")]
        [SerializeField] private Transform _leftGrabPoint;  // BothHandle/LeftGrabPoint 연결
        [SerializeField] private Transform _rightGrabPoint; // BothHandle/RightGrabPoint 연결

        // 기존 코드의 로직 유지를 위한 변수 (내부용)
        private Transform _leftReference;

        public struct TwoGrabPlaneState { public Pose Center; public float PlanarDistance; }
        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

        private IGrabbable _grabbable;
        private Pose _localToTarget;
        private Vector3 _leftLagPos;
        private bool _hasLagInit;
        private Vector3 _laggedPlanarDelta;
        private bool _hasDeltaInit;

        public void BeginTransform()
        {
            var target = _grabbable.Transform;

            // 1. [수정 사항] 설정에 따라 기준점을 실시간으로 교체
            _leftReference = (CurrentTargetSide == TargetLagSide.Left) ? _leftGrabPoint : _rightGrabPoint;

            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);
            _leftLagPos = leftPos; _hasLagInit = true;

            var planeNormal = _planeTransform != null ? _planeTransform.TransformDirection(_localPlaneNormal).normalized : target.TransformDirection(_localPlaneNormal).normalized;
            var state = TwoGrabPlane(_leftLagPos, rightPos, planeNormal);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _laggedPlanarDelta = (Vector3.ProjectOnPlane(rightPos, planeNormal) - Vector3.ProjectOnPlane(_leftLagPos, planeNormal));
            _hasDeltaInit = true;

            // 시작 시 초기 설정 강제 고정 (기존 유지)
            target.localScale = Vector3.one * 1.2f;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;

            // 2. [수정 사항] 매 프레임 상자 위치(TargetSide)에 맞춰 기준점 갱신
            _leftReference = (CurrentTargetSide == TargetLagSide.Left) ? _leftGrabPoint : _rightGrabPoint;

            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);
            if (!_hasLagInit) { _leftLagPos = leftPos; _hasLagInit = true; }

            float a = 1f - Mathf.Exp(-LeftPosFollow * Time.deltaTime);
            _leftLagPos = Vector3.Lerp(_leftLagPos, leftPos, a);

            var planeNormal = _planeTransform != null ? _planeTransform.TransformDirection(_localPlaneNormal).normalized : target.TransformDirection(_localPlaneNormal).normalized;
            var state = TwoGrabPlane_WithLaggedDelta(_leftLagPos, rightPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, LeftYawFollow);

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);

            // X, Z는 계산된 값을 따르고, Y는 무조건 0으로 고정 (기존 유지)
            target.position = new Vector3(result.position.x, 0f, result.position.z);
            target.rotation = result.rotation;

            // 스케일도 1.2로 계속 유지 (기존 유지)
            target.localScale = Vector3.one * 1.2f;
        }

        public void EndTransform() { }

        // --- 기존 Helper Methods (변경 없음) ---
        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(Vector3 leftP, Vector3 rightP, Vector3 planeNormal, ref Vector3 laggedDelta, ref bool hasInit, float yawFollow)
        {
            Vector3 centroid = leftP * 0.5f + rightP * 0.5f;
            Vector3 rawDelta = Vector3.ProjectOnPlane(rightP, planeNormal) - Vector3.ProjectOnPlane(leftP, planeNormal);
            if (!hasInit) { laggedDelta = rawDelta; hasInit = true; }
            float a = 1f - Mathf.Exp(-yawFollow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(laggedDelta, planeNormal)), PlanarDistance = rawDelta.magnitude };
        }

        private void GetLeftRightPositions(Vector3 pA, Vector3 pB, out Vector3 left, out Vector3 right)
        {
            // 내부적으로 설정된 _leftReference와의 거리를 비교합니다.
            if (_leftReference == null) { left = pA; right = pB; return; }
            if ((pA - _leftReference.position).sqrMagnitude <= (pB - _leftReference.position).sqrMagnitude) { left = pA; right = pB; }
            else { left = pB; right = pA; }
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;
            Vector3 delta = Vector3.ProjectOnPlane(p1, planeNormal) - Vector3.ProjectOnPlane(p0, planeNormal);
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(delta, planeNormal)), PlanarDistance = delta.magnitude };
        }
        //햅틱 로직에 필요
        public float GetCurrentLagDistance()
        {
            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);
            Vector3 currentTargetPos = (CurrentTargetSide == TargetLagSide.Left) ? leftPos : rightPos;

            // 실제 손 위치와 지연된 위치 사이의 월드 거리 반환
            return Vector3.Distance(_leftLagPos, currentTargetPos);
        }
        public void ResetLagState()
        {
            _hasLagInit = false; // 다음 Update에서 새로운 손 위치로 즉시 점프하도록 함
            _hasDeltaInit = false;
            Debug.Log("[Transformer] 손 교체에 따른 지연 상태 초기화 완료");
        }
    }
}