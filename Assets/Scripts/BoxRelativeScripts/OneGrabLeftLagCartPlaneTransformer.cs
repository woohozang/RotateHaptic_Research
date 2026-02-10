using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Oculus.Interaction
{
    /// <summary>
    /// One-hand cart control using hand POSITION around a pivot (stable):
    /// - Forward/back: along target forward projected to plane
    /// - Left/right: produces Yaw around pivot (from pivot->hand vector angle)
    /// - Left hand only: apply lag (pos+rot) for heavy feel
    /// </summary>
    public class OneGrabLeftLagCartPivotTransformer : MonoBehaviour, ITransformer
    {
        [Header("References")]
        [SerializeField] private Transform _cartPivot; // 중심 회전축(필수)

        [Header("Plane")]
        [SerializeField, Optional] private Transform _planeTransform = null;
        [SerializeField, Optional] private Vector3 _localPlaneNormal = new Vector3(0, 1, 0);

        [Header("Tuning")]
        [Tooltip("전진/후진 민감도 (1이면 그대로)")]
        [SerializeField] private float _moveGain = 1f;

        [Tooltip("회전 민감도 (1이면 그대로)")]
        [SerializeField] private float _yawGain = 1f;

        [Header("Left-hand lag (visual offset)")]
        [SerializeField] private bool _enableLeftLag = true;

        [Header("Left-hand detection (robust)")]
        [SerializeField] private GameObject _leftInteractorObject;

        [Range(0f, 30f)][SerializeField] private float _posFollow = 6f; // 낮을수록 무거움
        [Range(0f, 30f)][SerializeField] private float _rotFollow = 6f;

        [Range(0f, 0.5f)][SerializeField] private float _maxOffsetMeters = 0.15f;
        [SerializeField] private float _posJitterThreshold = 0.0005f;
        [SerializeField] private float _rotJitterThresholdDeg = 0.2f;

        private IGrabbable _grabbable;
        private Rigidbody _targetRb;

        // Begin state
        private Vector3 _targetStartPos;
        private Quaternion _targetStartRot;

        private Vector3 _grabStartOnPlane;
        private Vector3 _pivotStartOnPlane;

        private Vector3 _startPivotToGrab;   // 평면 상 pivot->grab 벡터(시작)
        private Vector3 _startForwardOnPlane; // 시작 시점 target forward(평면 투영)

        // Lag state
        private Vector3 _lagPos;
        private Quaternion _lagRot;

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
            if (_cartPivot == null)
            {
                Debug.LogError("[OneGrabLeftLagCartPivotTransformer] CartPivot is NULL. Assign it in Inspector.");
                return;
            }

            var target = _grabbable.Transform;
            _targetRb = target.GetComponent<Rigidbody>();

            var planeNormal = WorldPlaneNormal();
            var grab = _grabbable.GrabPoints[0];

            _targetStartPos = target.position;
            _targetStartRot = target.rotation;

            _grabStartOnPlane = Vector3.ProjectOnPlane(grab.position, planeNormal);
            _pivotStartOnPlane = Vector3.ProjectOnPlane(_cartPivot.position, planeNormal);

            _startPivotToGrab = (_grabStartOnPlane - _pivotStartOnPlane);
            if (_startPivotToGrab.sqrMagnitude < 1e-6f)
            {
                // pivot과 grab이 거의 같으면 회전 계산이 불안정 -> 아주 작은 오프셋
                _startPivotToGrab = Vector3.forward * 0.001f;
            }

            _startForwardOnPlane = Vector3.ProjectOnPlane(target.forward, planeNormal);
            if (_startForwardOnPlane.sqrMagnitude < 1e-6f)
                _startForwardOnPlane = Vector3.forward;
            _startForwardOnPlane.Normalize();

            _lagPos = _targetStartPos;
            _lagRot = _targetStartRot;
        }

        public void UpdateTransform()
        {
            if (_cartPivot == null) return;

            var target = _grabbable.Transform;
            var planeNormal = WorldPlaneNormal();
            var grab = _grabbable.GrabPoints[0];

            Vector3 grabNowOnPlane = Vector3.ProjectOnPlane(grab.position, planeNormal);
            Vector3 pivotNowOnPlane = Vector3.ProjectOnPlane(_cartPivot.position, planeNormal);

            // 1) 전/후 이동: grab의 평면 이동을 시작 forward축으로 투영
            Vector3 grabDelta = (grabNowOnPlane - _grabStartOnPlane);
            float forwardAmount = Vector3.Dot(grabDelta, _startForwardOnPlane) * _moveGain;
            Vector3 moveDelta = _startForwardOnPlane * forwardAmount;

            // 2) 좌/우 회전: pivot->grab 벡터의 "각도 변화"로 yaw 계산 (손목 방향 안씀)
            Vector3 pivotToGrabNow = (grabNowOnPlane - pivotNowOnPlane);
            if (pivotToGrabNow.sqrMagnitude < 1e-6f)
                pivotToGrabNow = _startPivotToGrab;

            float yawDelta = Vector3.SignedAngle(_startPivotToGrab, pivotToGrabNow, planeNormal) * _yawGain;

            Quaternion desiredRot = Quaternion.AngleAxis(yawDelta, planeNormal) * _targetStartRot;

            // 3) 위치는: 시작 위치 + moveDelta 를 기본으로 두고,
            // 회전으로 인한 pivot 고정 효과를 주려면 pivot을 기준으로 회전 적용한 보정이 필요함
            // (pivot이 카트 내부에 고정되어 있으므로 "pivot이 같은 자리"를 유지하도록 위치 보정)
            Vector3 desiredPos = _targetStartPos + moveDelta;

            // pivot 고정 보정:
            // 시작 pivot의 target 로컬 위치를 회전한 뒤 world로 다시 맞춰서 target.position 보정
            Vector3 pivotLocalInTarget = target.InverseTransformPoint(_cartPivot.position);
            Vector3 pivotWorldIfApplied = desiredPos + (desiredRot * (target.TransformVector(pivotLocalInTarget))); // 대략 보정
            // 위가 너무 과하면 아래 간단 보정 사용(권장): "회전만 적용하고 pivot 월드 위치 변화 최소화"
            // => 실제론 cartPivot이 target 자식이면 자동이라 크게 필요 없을 수도 있어 스킵 가능.
            // 여기서는 불안정 방지를 위해 보정은 최소화:
            // (원하면 이 줄을 주석 처리하고 테스트)
            // desiredPos += (_cartPivot.position - pivotWorldIfApplied);

            // 4) 왼손만 지연
            bool leftActive = _enableLeftLag && IsLeftHandSelecting(_grabbable);

            if (leftActive)
            {
                float posErr = Vector3.Distance(_lagPos, desiredPos);
                float rotErr = Quaternion.Angle(_lagRot, desiredRot);

                float ap = 1f - Mathf.Exp(-_posFollow * Time.deltaTime);
                float ar = 1f - Mathf.Exp(-_rotFollow * Time.deltaTime);

                if (posErr > _posJitterThreshold) _lagPos = Vector3.Lerp(_lagPos, desiredPos, ap);
                if (rotErr > _rotJitterThresholdDeg) _lagRot = Quaternion.Slerp(_lagRot, desiredRot, ar);

                if (_maxOffsetMeters > 0f)
                {
                    Vector3 diff = _lagPos - desiredPos;
                    float d = diff.magnitude;
                    if (d > _maxOffsetMeters)
                        _lagPos = desiredPos + diff.normalized * _maxOffsetMeters;
                }

                ApplyPose(target, _lagPos, _lagRot);
            }
            else
            {
                ApplyPose(target, desiredPos, desiredRot);
                _lagPos = desiredPos;
                _lagRot = desiredRot;
            }
        }

        public void EndTransform() { }

        private void ApplyPose(Transform target, Vector3 pos, Quaternion rot)
        {
            if (_targetRb != null && _targetRb.isKinematic)
            {
                _targetRb.MovePosition(pos);
                _targetRb.MoveRotation(rot);
            }
            else
            {
                target.position = pos;
                target.rotation = rot;
            }
        }

        // SDK 81 대응(버전별 프로퍼티명 차이) + 이름에 "left" 포함 방식
        private bool IsLeftHandSelecting(IGrabbable grabbable)
        {
            if (grabbable == null) return false;

            // 1) 인스펙터에서 왼손 interactor를 지정한 경우: 그게 selecting 중인지 비교
            if (_leftInteractorObject != null)
            {
                var mb = grabbable as MonoBehaviour;
                if (mb == null) return false;

                MonoBehaviour viewComp = null;
                foreach (var c in mb.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    if (c.GetType().GetInterface("Oculus.Interaction.IInteractableView") != null)
                    {
                        viewComp = c;
                        break;
                    }
                }
                if (viewComp == null) return false;

                var t = viewComp.GetType();
                var p = t.GetProperty("InteractorsSelecting", BindingFlags.Public | BindingFlags.Instance)
                     ?? t.GetProperty("SelectingInteractors", BindingFlags.Public | BindingFlags.Instance);
                if (p == null) return false;

                object val = p.GetValue(viewComp);
                if (val is IEnumerable enumerable)
                {
                    foreach (var it in enumerable)
                    {
                        // 인터랙터를 Component로 캐스팅해서 GameObject 비교
                        if (it is Component comp)
                        {
                            if (comp.gameObject == _leftInteractorObject) return true;
                            // 혹시 상위/하위로 들어오는 경우 대비
                            if (comp.transform.IsChildOf(_leftInteractorObject.transform)) return true;
                        }
                    }
                }
                return false;
            }

            // 2) 지정 안했으면 기존대로 이름 기반(백업)
            return IsLeftHandSelecting(grabbable);
        }
    }
}
