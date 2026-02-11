using System.Collections;
using System.Reflection;
using UnityEngine;
using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    public class DynamicCargoLagTwoGrabPlaneTransformer : MonoBehaviour, ITransformer
    {
        [Header("References")]
        [SerializeField] private CargoTracker _cargoTracker;
        [SerializeField] private Transform _cartPivot;

        [Header("Left/Right Interactor Roots (strongly recommended)")]
        [Tooltip("OVRInteractionComprehensive 아래 Left interactor 루트(혹은 왼손 grab interactor 루트)")]
        [SerializeField] private GameObject _leftInteractorRoot;

        [Tooltip("OVRInteractionComprehensive 아래 Right interactor 루트(혹은 오른손 grab interactor 루트)")]
        [SerializeField] private GameObject _rightInteractorRoot;

        [Header("Plane")]
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Lag (A 방식: 따라오되 늦게)")]
        [Range(0f, 30f)] public float posFollow = 6f;
        [Range(0f, 30f)] public float rotFollow = 6f;

        [Tooltip("지연으로 벌어질 수 있는 최대 오프셋(m). 너무 커지면 기괴해져서 클램프 권장")]
        [Range(0f, 0.5f)] public float maxOffsetMeters = 0.15f;

        [Header("Stability")]
        public float posJitterThreshold = 0.0005f;
        public float rotJitterThresholdDeg = 0.2f;

        [Header("Scaling (cart는 보통 scale 원치 않음)")]
        public bool enableScaling = false;

        [System.Serializable]
        public class TwoGrabPlaneConstraints
        {
            public FloatConstraint MaxScale;
            public FloatConstraint MinScale;
            public FloatConstraint MaxY;
            public FloatConstraint MinY;
        }

        [SerializeField] private TwoGrabPlaneConstraints _constraints;

        private IGrabbable _grabbable;

        // base state copied from TwoGrabPlaneTransformer
        private Pose _localToTarget;
        private float _localMagnitudeToTarget;

        // lag states for each grab point index
        private Vector3[] _lagPos = new Vector3[2];
        private Quaternion[] _lagRot = new Quaternion[2];

        private Vector3 WorldPlaneNormal()
        {
            Transform t = _planeTransform != null ? _planeTransform : _grabbable.Transform;
            return t.TransformDirection(_localPlaneNormal).normalized;
        }

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
        }

        public void BeginTransform()
        {
            var target = _grabbable.Transform;
            var planeNormal = WorldPlaneNormal();

            // 양손 가정(GrabPoints 2개)
            var grabA = _grabbable.GrabPoints[0];
            var grabB = _grabbable.GrabPoints[1];

            var state = TwoGrabPlane(grabA.position, grabB.position, planeNormal);
            _localToTarget = WorldToLocalPose(state.Center, target.worldToLocalMatrix);
            _localMagnitudeToTarget = WorldToLocalMagnitude(state.PlanarDistance, target.worldToLocalMatrix);

            // lag 초기화: 현재 포즈로 시작
            _lagPos[0] = grabA.position;
            _lagPos[1] = grabB.position;
            _lagRot[0] = grabA.rotation;
            _lagRot[1] = grabB.rotation;
        }

        public void UpdateTransform()
        {
            if (_grabbable == null) return;
            if (_grabbable.GrabPoints == null || _grabbable.GrabPoints.Count < 2) return;

            var target = _grabbable.Transform;
            var planeNormal = WorldPlaneNormal();

            // 현재 잡고 있는 두 그랩 포인트(원본)
            Pose raw0 = _grabbable.GrabPoints[0];
            Pose raw1 = _grabbable.GrabPoints[1];

            // 1) cargo 상태 판정 (Left/Center/Right)
            CargoTracker.BalanceState balance = CargoTracker.BalanceState.Center;
            if (_cargoTracker != null && _cartPivot != null && _cargoTracker.HasCargo())
            {
                balance = _cargoTracker.GetBalanceState(_cartPivot);
            }

            // 2) GrabPoint index(0/1) 중 어느 쪽이 Left hand인지 판별
            //    (가능하면 interactor root 비교로 확정)
            int leftIdx = -1;
            int rightIdx = -1;
            ResolveLeftRightGrabIndex(out leftIdx, out rightIdx);

            // fallback: 못 찾으면 0=left,1=right로 가정(최악의 경우)
            if (leftIdx < 0 || rightIdx < 0)
            {
                leftIdx = 0;
                rightIdx = 1;
            }

            // 3) 어떤 손(그랩포인트)에 지연을 적용할지 결정
            bool lagLeft = false;
            bool lagRight = false;

            switch (balance)
            {
                case CargoTracker.BalanceState.Left:
                    lagLeft = true; lagRight = false;
                    break;
                case CargoTracker.BalanceState.Center:
                    lagLeft = true; lagRight = true;
                    break;
                case CargoTracker.BalanceState.Right:
                    lagLeft = false; lagRight = true;
                    break;
            }

            // 4) 지연 적용(포인트별로)
            ApplyLagToGrabPoint(0, raw0, lagThis: (0 == leftIdx ? lagLeft : lagRight));
            ApplyLagToGrabPoint(1, raw1, lagThis: (1 == leftIdx ? lagLeft : lagRight));

            // 5) TwoGrabPlane 계산 시, raw가 아닌 lag 포인트를 사용
            Vector3 p0 = _lagPos[0];
            Vector3 p1 = _lagPos[1];

            var twoGrabPlaneState = TwoGrabPlane(p0, p1, planeNormal);

            // (옵션) 스케일 적용 여부
            if (enableScaling)
            {
                float prevDistInWorld = LocalToWorldMagnitude(_localMagnitudeToTarget, target.localToWorldMatrix);
                float scaleDelta = prevDistInWorld != 0 ? twoGrabPlaneState.PlanarDistance / prevDistInWorld : 1f;

                float targetScale = scaleDelta * target.localScale.x;
                if (_constraints.MinScale.Constrain)
                    targetScale = Mathf.Max(_constraints.MinScale.Value, targetScale);
                if (_constraints.MaxScale.Constrain)
                    targetScale = Mathf.Min(_constraints.MaxScale.Value, targetScale);

                target.localScale = (targetScale / target.localScale.x) * target.localScale;
            }

            // 6) 최종 포즈 적용(원본 TwoGrabPlaneTransformer 로직)
            Pose result = AlignLocalToWorldPose(target.localToWorldMatrix, _localToTarget, twoGrabPlaneState.Center);
            target.position = result.position;
            target.rotation = result.rotation;

            // Y 제약(원본 코드 동일)
            target.position = ConstrainAlongDirection(
                target.position,
                target.parent != null ? target.parent.position : Vector3.zero,
                planeNormal, _constraints.MinY, _constraints.MaxY);
        }

        public void EndTransform() { }

        private void ApplyLagToGrabPoint(int idx, Pose raw, bool lagThis)
        {
            if (!lagThis)
            {
                // 지연 미적용이면 즉시 동기화
                _lagPos[idx] = raw.position;
                _lagRot[idx] = raw.rotation;
                return;
            }

            // jitter guard
            float posErr = Vector3.Distance(_lagPos[idx], raw.position);
            float rotErr = Quaternion.Angle(_lagRot[idx], raw.rotation);

            float ap = 1f - Mathf.Exp(-posFollow * Time.deltaTime);
            float ar = 1f - Mathf.Exp(-rotFollow * Time.deltaTime);

            if (posErr > posJitterThreshold)
                _lagPos[idx] = Vector3.Lerp(_lagPos[idx], raw.position, ap);

            if (rotErr > rotJitterThresholdDeg)
                _lagRot[idx] = Quaternion.Slerp(_lagRot[idx], raw.rotation, ar);

            // max offset clamp
            if (maxOffsetMeters > 0f)
            {
                Vector3 diff = _lagPos[idx] - raw.position;
                float d = diff.magnitude;
                if (d > maxOffsetMeters)
                    _lagPos[idx] = raw.position + diff.normalized * maxOffsetMeters;
            }
        }

        /// <summary>
        /// Selecting interactor 목록의 순서가 GrabPoints 순서와 같다는 전제하에
        /// idx 0/1이 left/right인지 결정.
        /// </summary>
        private void ResolveLeftRightGrabIndex(out int leftIdx, out int rightIdx)
        {
            leftIdx = -1;
            rightIdx = -1;

            var view = FindInteractableViewOnSameObject();
            if (view == null) return;

            IEnumerable selecting = GetSelectingEnumerable(view);
            if (selecting == null) return;

            int i = 0;
            foreach (var it in selecting)
            {
                if (i > 1) break;
                if (it is Component comp)
                {
                    var go = comp.gameObject;

                    // robust: 루트로 판별
                    if (_leftInteractorRoot != null &&
                        (go == _leftInteractorRoot || go.transform.IsChildOf(_leftInteractorRoot.transform)))
                        leftIdx = i;

                    if (_rightInteractorRoot != null &&
                        (go == _rightInteractorRoot || go.transform.IsChildOf(_rightInteractorRoot.transform)))
                        rightIdx = i;

                    // fallback: 이름
                    if (_leftInteractorRoot == null)
                    {
                        string n = go.name.ToLowerInvariant();
                        if (n.Contains("left")) leftIdx = i;
                    }
                    if (_rightInteractorRoot == null)
                    {
                        string n = go.name.ToLowerInvariant();
                        if (n.Contains("right")) rightIdx = i;
                    }
                }
                i++;
            }
        }

        private object FindInteractableViewOnSameObject()
        {
            // 같은 GameObject에서 IInteractableView 구현 컴포넌트 찾기
            var comps = GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
                    return c;
            }
            return null;
        }

        private static IEnumerable GetSelectingEnumerable(object interactableView)
        {
            var t = interactableView.GetType();
            var p = t.GetProperty("InteractorsSelecting", BindingFlags.Public | BindingFlags.Instance)
                 ?? t.GetProperty("SelectingInteractors", BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(interactableView) as IEnumerable;
        }

        // ====== 원본 TwoGrabPlaneTransformer의 정적 함수 ======
        public struct TwoGrabPlaneState
        {
            public Pose Center;
            public float PlanarDistance;
        }

        public static TwoGrabPlaneState TwoGrabPlane(Vector3 p0, Vector3 p1, Vector3 planeNormal)
        {
            Vector3 centroid = p0 * 0.5f + p1 * 0.5f;

            Vector3 p0planar = Vector3.ProjectOnPlane(p0, planeNormal);
            Vector3 p1planar = Vector3.ProjectOnPlane(p1, planeNormal);

            Vector3 planarDelta = p1planar - p0planar;
            Quaternion poseDir = planarDelta.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(planarDelta, planeNormal)
                : Quaternion.LookRotation(Vector3.forward, planeNormal);

            return new TwoGrabPlaneState()
            {
                Center = new Pose(centroid, poseDir),
                PlanarDistance = planarDelta.magnitude
            };
        }
    }
}
