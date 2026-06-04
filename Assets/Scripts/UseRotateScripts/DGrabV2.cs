using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// 무게추의 위치에 따라 양손의 저항을 비대칭적으로, 그리고 부드럽게 제어하는 최종 트랜스포머입니다.
    /// </summary>
    public class DGrab_Final : MonoBehaviour, ITransformer
    {
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Weight & Pivot References")]
        [SerializeField] private Transform _cartPivot;    // 카트 중앙 기준점
        [SerializeField] private Transform _cargoObject;  // 움직이는 무게추 (Weight)

        [Header("Hand Detection")]
        [SerializeField] private Transform _leftReference;  // 왼쪽 핸들 끝
        [SerializeField] private Transform _rightReference; // 오른쪽 핸들 끝

        [Header("Resistance Settings (Lag)")]
        [Tooltip("무게가 최대일 때의 추적 속도. 낮을수록 더 묵직하게 뒤처집니다.")]
        [SerializeField] private float _leftPosFollow = 3.5f;
        [SerializeField] private float _rightPosFollow = 3.5f;
        [SerializeField] private float _yawFollowBase = 4.0f;

        // 저항이 없을 때의 즉각 추적 속도
        private const float INSTANT_FOLLOW = 20f;

        [Header("Smoothness Control")]
        [Tooltip("무게 변화에 따른 오프셋 전환 속도. 낮을수록 더 부드럽게 바뀝니다.")]
        [SerializeField] private float _smoothingFactor = 1.0f;

        [Tooltip("편향이 햅틱/지연으로 변환되는 곡선. 높을수록 초반 변화가 부드럽습니다.")]
        [SerializeField] private float _biasCurvePower = 2.8f;

        [Tooltip("최대 저항이 걸리는 거리 배율 (maxOffsetX가 0.18이면 5.5 추천)")]
        [SerializeField] private float _biasScale = 5.5f;

        [Header("Constraints")]
        [SerializeField] private TwoGrabPlaneConstraints _constraints;

        private IGrabbable _grabbable;
        private Vector3 _leftLagPos;
        private Vector3 _rightLagPos;
        private bool _init;
        private Vector3 _laggedDelta;
        private bool _deltaInit;

        private float _smoothedBias = 0f;
        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        [Serializable]
        public class TwoGrabPlaneConstraints
        {
            public FloatConstraint MaxScale;
            public FloatConstraint MinScale;
            public FloatConstraint MaxY;
            public FloatConstraint MinY;
        }

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
            if (_grabbable.GrabPoints.Count < 2) return;

            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            GetLeftRight(grabA.position, grabB.position, out var left, out var right);

            // 1. 무게추 위치 기반 바이어스 계산 및 1차 평활화
            float weightX = _cargoObject.localPosition.x;
            float targetBias = Mathf.Clamp(weightX * _biasScale, -1f, 1f);
            _smoothedBias = Mathf.Lerp(_smoothedBias, targetBias, Time.deltaTime * _smoothingFactor);

            // 2. 🔥 S-곡선(SmoothStep)을 이용한 2차 보간 (급격한 꺾임 방지 핵심)
            float absBias = Mathf.Abs(_smoothedBias);
            float smoothedIntensity = Mathf.SmoothStep(0f, 1f, absBias);
            smoothedIntensity = Mathf.Pow(smoothedIntensity, _biasCurvePower);

            // 방향에 따른 가중치 분리
            float leftWeight = (_smoothedBias < 0) ? smoothedIntensity : 0f;
            float rightWeight = (_smoothedBias > 0) ? smoothedIntensity : 0f;

            // 3. 실시간 추적 속도(Follow) 결정
            float currentLeftFollow = Mathf.Lerp(INSTANT_FOLLOW, _leftPosFollow, leftWeight);
            float currentRightFollow = Mathf.Lerp(INSTANT_FOLLOW, _rightPosFollow, rightWeight);

            // 4. 지연 위치 계산 (ExpFollow)
            _leftLagPos = ExpFollow(_leftLagPos, left, currentLeftFollow);
            _rightLagPos = ExpFollow(_rightLagPos, right, currentRightFollow);

            // 5. 회전 및 최종 포즈 적용
            float combinedWeight = Mathf.SmoothStep(0f, 1f, leftWeight + rightWeight);
            float currentYawFollow = Mathf.Lerp(INSTANT_FOLLOW, _yawFollowBase, combinedWeight);

            var planeNormal = WorldPlaneNormal();
            var state = TwoGrabPlane_WithLaggedDelta(
                _leftLagPos, _rightLagPos, planeNormal,
                ref _laggedDelta, ref _deltaInit, currentYawFollow);

            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, state.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            // 6. 🔥 Y축 강제 고정 (바닥 출렁임 완전 차단)
            float lockY = target.parent != null ? target.parent.position.y : 0f;
            target.position = new Vector3(target.position.x, lockY, target.position.z);

            // 디버그 라인
            Debug.Log($"WeightX: {weightX:F3} | LeftW: {leftWeight:F2} | RightW: {rightWeight:F2}");
        }

        public void EndTransform() { }

        private void GetLeftRight(Vector3 a, Vector3 b, out Vector3 left, out Vector3 right)
        {
            float distALeft = (a - _leftReference.position).sqrMagnitude;
            float distBLeft = (b - _leftReference.position).sqrMagnitude;
            if (distALeft < distBLeft) { left = a; right = b; }
            else { left = b; right = a; }
        }

        private Vector3 ExpFollow(Vector3 current, Vector3 target, float follow)
        {
            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            return Vector3.Lerp(current, target, a);
        }

        // --- TransformerUtils 내장 함수 복제 ---
        public struct TwoGrabPlaneState { public Pose Center; public float PlanarDistance; }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;
            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);
            Vector3 planarDelta = p1planar - p0planar;
            Quaternion poseDir = planarDelta.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(planarDelta, planeNormal) : Quaternion.identity;
            return new TwoGrabPlaneState() { Center = new Pose(centroid, poseDir), PlanarDistance = planarDelta.magnitude };
        }

        private static TwoGrabPlaneState TwoGrabPlane_WithLaggedDelta(Vector3 leftP, Vector3 rightP, Vector3 planeNormal, ref Vector3 laggedDelta, ref bool hasInit, float follow)
        {
            Vector3 lPlanar = Vector3.ProjectOnPlane(leftP, planeNormal);
            Vector3 rPlanar = Vector3.ProjectOnPlane(rightP, planeNormal);
            Vector3 rawDelta = rPlanar - lPlanar;
            if (!hasInit) { laggedDelta = rawDelta; hasInit = true; }
            float a = 1f - Mathf.Exp(-follow * Time.deltaTime);
            laggedDelta = Vector3.Lerp(laggedDelta, rawDelta, a);
            Quaternion rot = laggedDelta.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(laggedDelta, planeNormal) : Quaternion.identity;
            return new TwoGrabPlaneState() { Center = new Pose((leftP + rightP) * 0.5f, rot), PlanarDistance = rawDelta.magnitude };
        }
    }
}