/*
 * Directional C/D Ratio Transformer
 *
 * -Z 방향으로 당길 때:
 * 실제 손 이동량 × C/D Ratio
 *
 * +Z 방향으로 밀어 복귀할 때:
 * 실제 손 이동량 × 1.0
 */

using System;
using UnityEngine;

using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    [DisallowMultipleComponent]
    public class OneGrabTranslateCDTransformer :
        MonoBehaviour,
        ITransformer
    {
        public enum ZMovementMode
        {
            Idle,
            Compressing,
            Returning
        }

        [Serializable]
        public class OneGrabTranslateCDConstraints
        {
            [Tooltip("초기 위치를 기준으로 제약값을 계산합니다.")]
            public bool ConstraintsAreRelative = true;

            public FloatConstraint MinX = new FloatConstraint();
            public FloatConstraint MaxX = new FloatConstraint();

            public FloatConstraint MinY = new FloatConstraint();
            public FloatConstraint MaxY = new FloatConstraint();

            public FloatConstraint MinZ = new FloatConstraint();
            public FloatConstraint MaxZ = new FloatConstraint();
        }

        [Header("C/D Ratio")]

        [Tooltip(
            "-Z 방향으로 손잡이를 당길 때 적용되는 비율입니다. " +
            "1.0은 일반 이동이며, 값이 낮을수록 손잡이가 덜 움직입니다."
        )]
        [Range(0.05f, 1f)]
        [SerializeField]
        private float _cdRatio = 0.8f;

        [Header("방향별 Z 설정")]

        [Tooltip(
            "활성화하면 -Z 방향에는 C/D Ratio를 적용하고, " +
            "+Z 방향에는 1:1 트래킹을 적용합니다."
        )]
        [SerializeField]
        private bool _useDirectionalZMapping = true;

        [Tooltip(
            "손의 미세한 떨림으로 압축/복귀 상태가 계속 전환되는 것을 방지합니다. " +
            "단위는 미터입니다."
        )]
        [Min(0f)]
        [SerializeField]
        private float _zDirectionDeadzone = 0.0005f;

        [Header("C/D 적용 축")]

        [SerializeField]
        private bool _applyCDToX;

        [SerializeField]
        private bool _applyCDToY;

        [SerializeField]
        private bool _applyCDToZ = true;

        [Header("이동 제약")]

        [SerializeField]
        private OneGrabTranslateCDConstraints _constraints =
            new OneGrabTranslateCDConstraints();

        [Header("디버그")]

        [SerializeField]
        private bool _debugLog;

        [Tooltip("디버그 출력 간격")]
        [Min(0.05f)]
        [SerializeField]
        private float _debugInterval = 0.2f;

        private IGrabbable _grabbable;

        private OneGrabTranslateCDConstraints _parentConstraints;

        private Vector3 _initialLocalPosition;

        // SDK 원본과 동일한 Grab Offset
        private Pose _localToTarget;

        // Grab 시작 시점의 손잡이 Transform
        private Vector3 _targetStartLocalPosition;
        private Quaternion _targetStartLocalRotation;

        /*
         * 이동 방향 판별에 사용되는,
         * 이전 프레임의 C/D 적용 전 목표 위치입니다.
         */
        private Vector3 _previousRawTargetLocalPosition;

        /*
         * 방향이 전환될 때 위치가 순간적으로 점프하지 않도록
         * 현재 방향 구간의 시작점을 저장합니다.
         */
        private float _zRawAnchor;
        private float _zMappedAnchor;

        private ZMovementMode _zMovementMode =
            ZMovementMode.Idle;

        private float _currentZStep;
        private float _activeZRatio = 1f;
        private float _nextDebugTime;

        public float CDRatio
        {
            get => _cdRatio;
            set => _cdRatio =
                Mathf.Clamp(value, 0.05f, 1f);
        }

        /// <summary>
        /// 현재 Z 이동 상태입니다.
        /// </summary>
        public ZMovementMode CurrentZMovementMode =>
            _zMovementMode;

        /// <summary>
        /// 현재 Z 방향에 실제로 적용되는 비율입니다.
        /// 압축 중에는 C/D Ratio, 복귀 중에는 1.0입니다.
        /// </summary>
        public float ActiveZRatio =>
            _activeZRatio;

        /// <summary>
        /// Grab 시작 위치를 기준으로 한
        /// C/D 적용 전 Z 이동량입니다.
        /// </summary>
        public float RawZDelta { get; private set; }

        /// <summary>
        /// Grab 시작 위치를 기준으로 한
        /// C/D 적용 후 Z 이동량입니다.
        /// </summary>
        public float MappedZDelta { get; private set; }

        public OneGrabTranslateCDConstraints Constraints
        {
            get => _constraints;

            set
            {
                _constraints = value;
                GenerateParentConstraints();
            }
        }

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;

            if (_grabbable == null)
            {
                Debug.LogError(
                    "[OneGrabTranslateCDTransformer] " +
                    "Grabbable 초기화에 실패했습니다."
                );

                return;
            }

            _initialLocalPosition =
                _grabbable.Transform.localPosition;

            GenerateParentConstraints();
        }

        public void BeginTransform()
        {
            if (!HasValidGrabPoint())
            {
                return;
            }

            Transform target =
                _grabbable.Transform;

            Pose grabPose =
                _grabbable.GrabPoints[0];

            /*
             * Grab 시 손과 Target 사이의
             * 위치·회전 오프셋을 저장합니다.
             */
            _localToTarget =
                WorldToLocalPose(
                    grabPose,
                    target.worldToLocalMatrix
                );

            _targetStartLocalPosition =
                target.localPosition;

            _targetStartLocalRotation =
                target.localRotation;

            /*
             * 첫 프레임의 방향 계산 기준입니다.
             */
            _previousRawTargetLocalPosition =
                target.localPosition;

            _zRawAnchor =
                target.localPosition.z;

            _zMappedAnchor =
                target.localPosition.z;

            _zMovementMode =
                ZMovementMode.Idle;

            _currentZStep = 0f;
            _activeZRatio = 1f;

            RawZDelta = 0f;
            MappedZDelta = 0f;
        }

        public void UpdateTransform()
        {
            if (!HasValidGrabPoint())
            {
                return;
            }

            Transform target =
                _grabbable.Transform;

            Pose grabPose =
                _grabbable.GrabPoints[0];

            /*
             * 1. 현재 손 위치를 기준으로
             *    C/D 적용 전 목표 Pose를 계산합니다.
             */
            Quaternion initialGrabRotation =
                target.rotation *
                _localToTarget.rotation;

            Pose targetPose =
                new Pose(
                    grabPose.position,
                    initialGrabRotation
                );

            Pose rawResult =
                AlignLocalToWorldPose(
                    target.localToWorldMatrix,
                    _localToTarget,
                    targetPose
                );

            /*
             * 2. World Position을
             *    부모 기준 Local Position으로 변환합니다.
             */
            Vector3 rawTargetLocalPosition;

            if (target.parent != null)
            {
                rawTargetLocalPosition =
                    target.parent.InverseTransformPoint(
                        rawResult.position
                    );
            }
            else
            {
                rawTargetLocalPosition =
                    rawResult.position;
            }

            Vector3 rawDelta =
                rawTargetLocalPosition -
                _targetStartLocalPosition;

            RawZDelta =
                rawDelta.z;

            /*
             * 현재 위치의 부호가 아니라,
             * 이전 프레임과 비교한 이동 방향을 계산합니다.
             *
             * 음수: 당기기 / 압축
             * 양수: 밀기 / 복귀
             */
            _currentZStep =
                rawTargetLocalPosition.z -
                _previousRawTargetLocalPosition.z;

            /*
             * 3. X/Y는 기존 방식처럼
             *    Grab 시작 위치 기준으로 C/D를 적용합니다.
             */
            Vector3 mappedTargetLocalPosition =
                rawTargetLocalPosition;

            if (_applyCDToX)
            {
                mappedTargetLocalPosition.x =
                    _targetStartLocalPosition.x +
                    rawDelta.x * _cdRatio;
            }

            if (_applyCDToY)
            {
                mappedTargetLocalPosition.y =
                    _targetStartLocalPosition.y +
                    rawDelta.y * _cdRatio;
            }

            /*
             * 4. Z는 이동 방향에 따라 비율을 변경합니다.
             */
            if (_applyCDToZ)
            {
                if (_useDirectionalZMapping)
                {
                    UpdateZMovementMode(
                        target,
                        rawTargetLocalPosition
                    );

                    if (_zMovementMode ==
                        ZMovementMode.Compressing)
                    {
                        // -Z 방향: C/D Ratio 적용
                        _activeZRatio =
                            _cdRatio;
                    }
                    else if (_zMovementMode ==
                             ZMovementMode.Returning)
                    {
                        // +Z 방향: 1:1 트래킹
                        _activeZRatio =
                            1f;
                    }

                    if (_zMovementMode ==
                        ZMovementMode.Idle)
                    {
                        /*
                         * 미세 떨림 구간에서는
                         * 현재 위치를 그대로 유지합니다.
                         */
                        mappedTargetLocalPosition.z =
                            target.localPosition.z;
                    }
                    else
                    {
                        /*
                         * 방향 전환 지점을 기준으로 계산하므로
                         * C/D ↔ 1:1 전환 시 위치가 점프하지 않습니다.
                         */
                        mappedTargetLocalPosition.z =
                            _zMappedAnchor +
                            (
                                rawTargetLocalPosition.z -
                                _zRawAnchor
                            ) *
                            _activeZRatio;
                    }
                }
                else
                {
                    /*
                     * Directional Mapping을 끈 경우
                     * 기존처럼 양방향 모두 C/D 적용
                     */
                    _activeZRatio =
                        _cdRatio;

                    mappedTargetLocalPosition.z =
                        _targetStartLocalPosition.z +
                        rawDelta.z *
                        _cdRatio;
                }
            }
            else
            {
                _activeZRatio = 1f;
            }

            /*
             * 5. 최종 위치와 회전 적용
             */
            target.localPosition =
                mappedTargetLocalPosition;

            target.localRotation =
                _targetStartLocalRotation;

            /*
             * 6. Min/Max Constraint 적용
             */
            ConstrainTransform();

            /*
             * Constraint 적용 후 실제 손잡이 위치를 기준으로
             * 최종 매핑 이동량을 기록합니다.
             */
            MappedZDelta =
                target.localPosition.z -
                _targetStartLocalPosition.z;

            _previousRawTargetLocalPosition =
                rawTargetLocalPosition;

            DebugValues(target);
        }

        public void EndTransform()
        {
            RawZDelta = 0f;
            MappedZDelta = 0f;

            _currentZStep = 0f;
            _activeZRatio = 1f;

            _zMovementMode =
                ZMovementMode.Idle;
        }

        /// <summary>
        /// 현재 프레임의 Raw Z 이동 방향을 이용해
        /// 압축 또는 복귀 상태를 결정합니다.
        /// </summary>
        private void UpdateZMovementMode(
            Transform target,
            Vector3 rawTargetLocalPosition
        )
        {
            ZMovementMode nextMode =
                _zMovementMode;

            if (_currentZStep <
                -_zDirectionDeadzone)
            {
                nextMode =
                    ZMovementMode.Compressing;
            }
            else if (_currentZStep >
                     _zDirectionDeadzone)
            {
                nextMode =
                    ZMovementMode.Returning;
            }

            /*
             * 방향이 바뀌는 순간을 새 기준점으로 저장합니다.
             * 이를 통해 비율이 0.4 → 1.0처럼 바뀌어도
             * 손잡이 위치가 순간 이동하지 않습니다.
             */
            if (nextMode != _zMovementMode)
            {
                _zRawAnchor =
                    _previousRawTargetLocalPosition.z;

                _zMappedAnchor =
                    target.localPosition.z;

                _zMovementMode =
                    nextMode;
            }
        }

        private bool HasValidGrabPoint()
        {
            return
                _grabbable != null &&
                _grabbable.GrabPoints != null &&
                _grabbable.GrabPoints.Count > 0;
        }

        private void GenerateParentConstraints()
        {
            if (_constraints == null)
            {
                return;
            }

            if (!_constraints.ConstraintsAreRelative)
            {
                _parentConstraints =
                    _constraints;

                return;
            }

            _parentConstraints =
                new OneGrabTranslateCDConstraints
                {
                    ConstraintsAreRelative = true,

                    MinX = new FloatConstraint(),
                    MaxX = new FloatConstraint(),

                    MinY = new FloatConstraint(),
                    MaxY = new FloatConstraint(),

                    MinZ = new FloatConstraint(),
                    MaxZ = new FloatConstraint()
                };

            CopyRelativeConstraint(
                _constraints.MinX,
                _parentConstraints.MinX,
                _initialLocalPosition.x
            );

            CopyRelativeConstraint(
                _constraints.MaxX,
                _parentConstraints.MaxX,
                _initialLocalPosition.x
            );

            CopyRelativeConstraint(
                _constraints.MinY,
                _parentConstraints.MinY,
                _initialLocalPosition.y
            );

            CopyRelativeConstraint(
                _constraints.MaxY,
                _parentConstraints.MaxY,
                _initialLocalPosition.y
            );

            CopyRelativeConstraint(
                _constraints.MinZ,
                _parentConstraints.MinZ,
                _initialLocalPosition.z
            );

            CopyRelativeConstraint(
                _constraints.MaxZ,
                _parentConstraints.MaxZ,
                _initialLocalPosition.z
            );
        }

        private static void CopyRelativeConstraint(
            FloatConstraint source,
            FloatConstraint destination,
            float initialValue
        )
        {
            if (source == null ||
                destination == null ||
                !source.Constrain)
            {
                return;
            }

            destination.Constrain = true;

            destination.Value =
                initialValue +
                source.Value;
        }

        private void ConstrainTransform()
        {
            if (_parentConstraints == null ||
                _grabbable == null)
            {
                return;
            }

            Transform target =
                _grabbable.Transform;

            Vector3 position =
                target.localPosition;

            if (_parentConstraints.MinX.Constrain)
            {
                position.x =
                    Mathf.Max(
                        position.x,
                        _parentConstraints.MinX.Value
                    );
            }

            if (_parentConstraints.MaxX.Constrain)
            {
                position.x =
                    Mathf.Min(
                        position.x,
                        _parentConstraints.MaxX.Value
                    );
            }

            if (_parentConstraints.MinY.Constrain)
            {
                position.y =
                    Mathf.Max(
                        position.y,
                        _parentConstraints.MinY.Value
                    );
            }

            if (_parentConstraints.MaxY.Constrain)
            {
                position.y =
                    Mathf.Min(
                        position.y,
                        _parentConstraints.MaxY.Value
                    );
            }

            if (_parentConstraints.MinZ.Constrain)
            {
                position.z =
                    Mathf.Max(
                        position.z,
                        _parentConstraints.MinZ.Value
                    );
            }

            if (_parentConstraints.MaxZ.Constrain)
            {
                position.z =
                    Mathf.Min(
                        position.z,
                        _parentConstraints.MaxZ.Value
                    );
            }

            target.localPosition =
                position;
        }

        private void DebugValues(Transform target)
        {
            if (!_debugLog ||
                Time.unscaledTime < _nextDebugTime)
            {
                return;
            }

            _nextDebugTime =
                Time.unscaledTime +
                _debugInterval;

            Debug.Log(
                $"[Directional CD Translate] " +
                $"Mode:{_zMovementMode} | " +
                $"Configured Ratio:{_cdRatio:F2} | " +
                $"Active Ratio:{_activeZRatio:F2} | " +
                $"Raw Z Step:{_currentZStep:F4} | " +
                $"Raw Z Delta:{RawZDelta:F3} | " +
                $"Mapped Z Delta:{MappedZDelta:F3} | " +
                $"Final Local Z:{target.localPosition.z:F3}",
                this
            );
        }

        #region Inject

        public void InjectOptionalConstraints(
            OneGrabTranslateCDConstraints constraints
        )
        {
            _constraints = constraints;
            GenerateParentConstraints();
        }

        public void InjectOptionalCDRatio(
            float cdRatio
        )
        {
            CDRatio = cdRatio;
        }

        #endregion
    }
}