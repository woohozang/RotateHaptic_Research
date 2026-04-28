using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// 무게 중심(CurrentTargetSide)에 따라 특정 손의 반응성을 의도적으로 늦추고(Pseudo-haptics),
    /// 그 지연 거리를 햅틱 컨트롤러에 전달하는 트랜스포머입니다.
    /// </summary>
    public class DynamicWeightTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        public enum TargetLagSide { Left, Right }

        [Header("Strategic Settings")]
        [Tooltip("상자의 위치에 따라 선택하세요. Left면 왼쪽 핸들, Right면 오른쪽 핸들에 지연이 발생합니다.")]
        public TargetLagSide CurrentTargetSide = TargetLagSide.Left;

        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Lag Settings")]
        [Range(0.5f, 40f)] public float LeftYawFollow = 1.5f;
        [Range(0.5f, 40f)] public float LeftPosFollow = 20f;

        [Header("Hand Identification References")]
        [SerializeField] private Transform _leftGrabPoint;  // BothHandle/LeftGrabPoint 연결
        [SerializeField] private Transform _rightGrabPoint; // BothHandle/RightGrabPoint 연결

        private IGrabbable _grabbable;
        private Transform _referencePoint;
        private Pose _localToTarget;
        private Vector3 _targetLagPos; // 무거운 쪽 손의 지연된 위치 (통합 관리)
        private bool _hasLagInit;
        private Vector3 _laggedPlanarDelta;
        private bool _hasDeltaInit;

        public struct TwoGrabPlaneState { public Pose Center; public float PlanarDistance; }
        public void Initialize(IGrabbable grabbable) => _grabbable = grabbable;

        public void BeginTransform()
        {
            var target = _grabbable.Transform;

            // 현재 설정된 무게 중심에 따라 기준점 설정
            _referencePoint = (CurrentTargetSide == TargetLagSide.Left) ? _leftGrabPoint : _rightGrabPoint;

            if (_grabbable.GrabPoints.Count < 2) return;

            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);

            // 초기 위치 설정 (지연된 위치 = 실제 위치)
            _targetLagPos = (CurrentTargetSide == TargetLagSide.Left) ? leftPos : rightPos;
            _hasLagInit = true;

            var planeNormal = _planeTransform != null ? _planeTransform.TransformDirection(_localPlaneNormal).normalized : target.TransformDirection(_localPlaneNormal).normalized;

            // 초기 State 계산
            TwoGrabPlaneState state = (CurrentTargetSide == TargetLagSide.Left)
                ? TwoGrabPlane(_targetLagPos, rightPos, planeNormal)
                : TwoGrabPlane(leftPos, _targetLagPos, planeNormal);

            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _laggedPlanarDelta = (Vector3.ProjectOnPlane(rightPos, planeNormal) - Vector3.ProjectOnPlane(leftPos, planeNormal));
            _hasDeltaInit = true;

            target.localScale = Vector3.one * 1.2f;
        }

        public void UpdateTransform()
        {
            var target = _grabbable.Transform;
            _referencePoint = (CurrentTargetSide == TargetLagSide.Left) ? _leftGrabPoint : _rightGrabPoint;

            if (_grabbable.GrabPoints.Count < 2) return;

            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);

            // 무거운 쪽 손의 실제 물리 위치 결정
            Vector3 actualTargetPos = (CurrentTargetSide == TargetLagSide.Left) ? leftPos : rightPos;

            if (!_hasLagInit) { _targetLagPos = actualTargetPos; _hasLagInit = true; }

            // [핵심] 질량에 따른 시각적 지연 계산 (Lerp)
            float a = 1f - Mathf.Exp(-LeftPosFollow * Time.deltaTime);
            _targetLagPos = Vector3.Lerp(_targetLagPos, actualTargetPos, a);

            var planeNormal = _planeTransform != null ? _planeTransform.TransformDirection(_localPlaneNormal).normalized : target.TransformDirection(_localPlaneNormal).normalized;

            // 지연된 위치를 적용하여 카트의 새로운 위치와 회전 계산
            TwoGrabPlaneState state;
            if (CurrentTargetSide == TargetLagSide.Left)
            {
                state = TwoGrabPlane_WithLaggedDelta(_targetLagPos, rightPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, LeftYawFollow);
            }
            else
            {
                state = TwoGrabPlane_WithLaggedDelta(leftPos, _targetLagPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, LeftYawFollow);
            }

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);

            // 결과 적용 (바닥 고정 및 스케일 유지)
            target.position = new Vector3(result.position.x, 0f, result.position.z);
            target.rotation = result.rotation;
            target.localScale = Vector3.one * 1.2f;
        }

        public void EndTransform() { }

        // --- Haptic 로직을 위한 지연 거리 반환 메서드 ---
        public float GetCurrentLagDistance()
        {
            // 손을 뗐을 때 인덱스 에러 방지
            if (_grabbable == null || _grabbable.GrabPoints.Count < 2)
            {
                return 0f;
            }

            GetLeftRightPositions(_grabbable.GrabPoints[0].position, _grabbable.GrabPoints[1].position, out Vector3 leftPos, out Vector3 rightPos);

            // 실제 손 위치 결정
            Vector3 actualTargetPos = (CurrentTargetSide == TargetLagSide.Left) ? leftPos : rightPos;

            // [수정 핵심] 같은 쪽의 지연된 위치와 실제 위치 사이의 거리만 계산 (핸들 너비 간섭 제거)
            return Vector3.Distance(_targetLagPos, actualTargetPos);
        }

        public void ResetLagState()
        {
            _hasLagInit = false;
            _hasDeltaInit = false;
            Debug.Log("[Transformer] 지연 상태 초기화 완료");
        }

        // --- Helper Methods ---
        private void GetLeftRightPositions(Vector3 pA, Vector3 pB, out Vector3 left, out Vector3 right)
        {
            if (_referencePoint == null) { left = pA; right = pB; return; }

            // pA와 pB 중 어느 것이 현재 추적 중인 _referencePoint(왼쪽 혹은 오른쪽 GrabPoint)와 더 가까운지 판별
            if ((pA - _referencePoint.position).sqrMagnitude <= (pB - _referencePoint.position).sqrMagnitude)
            {
                if (CurrentTargetSide == TargetLagSide.Left) { left = pA; right = pB; }
                else { right = pA; left = pB; }
            }
            else
            {
                if (CurrentTargetSide == TargetLagSide.Left) { left = pB; right = pA; }
                else { right = pB; left = pA; }
            }
        }

        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(Vector3 leftP, Vector3 rightP, Vector3 planeNormal, ref Vector3 laggedDelta, ref bool hasInit, float yawFollow)
        {
            Vector3 centroid = leftP * 0.5f + rightP * 0.5f;
            Vector3 rawDelta = Vector3.ProjectOnPlane(rightP, planeNormal) - Vector3.ProjectOnPlane(leftP, planeNormal);
            if (!hasInit) { laggedDelta = rawDelta; hasInit = true; }
            float a = 1f - Mathf.Exp(-yawFollow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(laggedDelta, planeNormal)), PlanarDistance = rawDelta.magnitude };
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;
            Vector3 delta = Vector3.ProjectOnPlane(p1, planeNormal) - Vector3.ProjectOnPlane(p0, planeNormal);
            return new TwoGrabPlaneState() { Center = new Pose(centroid, Quaternion.LookRotation(delta, planeNormal)), PlanarDistance = delta.magnitude };
        }
    }
}