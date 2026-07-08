using System;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// 무게 중심(CurrentTargetSide)에 따라 특정 손의 반응성을 의도적으로 늦추고(Pseudo-haptics),
    /// 그 지연 거리를 햅틱 컨트롤러에 전달하는 트랜스포머입니다.
    ///
    /// [수정] 카트 자중 20kg + 생수병(1.5L, 1.5kg) 적재 개수(2/4/6개)에 따라
    /// C/D Ratio(추종 속도)가 질량 기반으로 자동 산출됩니다.
    ///   총 질량 M = 20 + 1.5 × bottleCount
    ///   massFactor = (1.5 × bottleCount) / 20          → 0.15 / 0.30 / 0.45
    ///   PosFollow  = basePosFollow / (1 + lagGain × massFactor)
    ///   YawFollow  = baseYawFollow / (1 + lagGain × massFactor)
    /// 기본값(basePos=20, baseYaw=1.5, lagGain=2)일 때:
    ///   2개(23kg): Pos 15.38, Yaw 1.15  |  4개(26kg): Pos 12.50, Yaw 0.94  |  6개(29kg): Pos 10.53, Yaw 0.79
    /// 지수 추종기의 정상상태 지연 거리 = 손 속도 / PosFollow 이므로,
    /// 손 속도 0.5 m/s 기준 시각적 지연은 약 32.5mm / 40.0mm / 47.5mm로 질량에 선형 비례합니다.
    /// </summary>
    public class GrabCDScript : MonoBehaviour, ITransformer
    {
        public enum TargetLagSide { Left, Right }

        [Header("Strategic Settings")]
        [Tooltip("상자의 위치에 따라 선택하세요. Left면 왼쪽 핸들, Right면 오른쪽 핸들에 지연이 발생합니다.")]
        public TargetLagSide CurrentTargetSide = TargetLagSide.Left;

        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        // ─────────────────────────────────────────────────────────────
        [Header("Mass Settings (실험 조건)")]
        [Tooltip("쇼핑카트 자중 (kg)")]
        public float CartMass = 20f;
        [Tooltip("생수병 1개 무게 (kg) — 1.5L 생수")]
        public float BottleMass = 1.5f;
        [Tooltip("적재된 생수병 개수 (실험 조건: 2 / 4 / 6)")]
        [Range(0, 12)] public int BottleCount = 2;
        [Tooltip("질량→지연 변환 이득. 클수록 병 개수에 따른 지연 차이가 커집니다.")]
        [Range(0.5f, 4f)] public float LagGain = 2f;

        [Header("Base Lag Settings (무부하 기준값)")]
        [Tooltip("빈 카트(20kg) 기준 Yaw 추종 속도")]
        [Range(0.5f, 40f)] public float BaseYawFollow = 1.5f;
        [Tooltip("빈 카트(20kg) 기준 위치 추종 속도")]
        [Range(0.5f, 40f)] public float BasePosFollow = 20f;

        [Header("Runtime (읽기 전용 — 질량으로부터 자동 산출)")]
        [SerializeField] private float _leftYawFollow;
        [SerializeField] private float _leftPosFollow;

        /// <summary>질량 기반으로 산출된 현재 Yaw 추종 속도</summary>
        public float LeftYawFollow => _leftYawFollow;
        /// <summary>질량 기반으로 산출된 현재 위치 추종 속도</summary>
        public float LeftPosFollow => _leftPosFollow;

        /// <summary>총 질량 (카트 + 적재물, kg)</summary>
        public float TotalMass => CartMass + BottleMass * BottleCount;
        /// <summary>적재 질량 (kg)</summary>
        public float LoadMass => BottleMass * BottleCount;
        /// <summary>적재 질량 정규화 값 (0~1). 최대 적재(6개=9kg) 기준 → 2개: 0.333, 4개: 0.667, 6개: 1.0</summary>
        public float NormalizedLoad => Mathf.Clamp01(LoadMass / (BottleMass * 6f));
        // ─────────────────────────────────────────────────────────────

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

        private void Awake() => RecalculateFollowFromMass();
        private void OnValidate() => RecalculateFollowFromMass();

        /// <summary>
        /// [핵심 수정] 총 질량으로부터 추종 속도(C/D Ratio 감쇄)를 산출합니다.
        /// 병이 많을수록(무거울수록) 추종 속도가 낮아져 가상 손의 시각적 지연이 커집니다.
        /// </summary>
        public void RecalculateFollowFromMass()
        {
            float massFactor = LoadMass / Mathf.Max(CartMass, 0.001f); // 0.15 / 0.30 / 0.45
            float attenuation = 1f + LagGain * massFactor;             // 1.30 / 1.60 / 1.90
            _leftPosFollow = BasePosFollow / attenuation;              // 15.38 / 12.50 / 10.53
            _leftYawFollow = BaseYawFollow / attenuation;              // 1.15  / 0.94  / 0.79
        }

        /// <summary>
        /// 실험 조건 전환용 API. 병 개수(2/4/6)와 편향 방향을 설정하고 지연 상태를 초기화합니다.
        /// 예) SetLoadCondition(4, TargetLagSide.Right);
        /// </summary>
        public void SetLoadCondition(int bottleCount, TargetLagSide side)
        {
            BottleCount = Mathf.Max(0, bottleCount);
            CurrentTargetSide = side;
            RecalculateFollowFromMass();
            ResetLagState();
            Debug.Log($"[Transformer] 조건 설정: 병 {BottleCount}개, 총 {TotalMass:F1}kg, " +
                      $"PosFollow={_leftPosFollow:F2}, YawFollow={_leftYawFollow:F2}, 편향={side}");
        }

        public void BeginTransform()
        {
            var target = _grabbable.Transform;
            RecalculateFollowFromMass(); // 파지 시점의 적재 상태 반영

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

            // [핵심] 질량 기반으로 산출된 추종 속도(_leftPosFollow)에 따른 시각적 지연 계산 (Lerp)
            float a = 1f - Mathf.Exp(-_leftPosFollow * Time.deltaTime);
            _targetLagPos = Vector3.Lerp(_targetLagPos, actualTargetPos, a);

            var planeNormal = _planeTransform != null ? _planeTransform.TransformDirection(_localPlaneNormal).normalized : target.TransformDirection(_localPlaneNormal).normalized;

            // 지연된 위치를 적용하여 카트의 새로운 위치와 회전 계산
            TwoGrabPlaneState state;
            if (CurrentTargetSide == TargetLagSide.Left)
            {
                state = TwoGrabPlane_WithLaggedDelta(_targetLagPos, rightPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, _leftYawFollow);
            }
            else
            {
                state = TwoGrabPlane_WithLaggedDelta(leftPos, _targetLagPos, planeNormal, ref _laggedPlanarDelta, ref _hasDeltaInit, _leftYawFollow);
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

            // 같은 쪽의 지연된 위치와 실제 위치 사이의 거리만 계산 (핸들 너비 간섭 제거)
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