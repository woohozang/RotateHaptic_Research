/*
 * Custom C/D Ratio Transformer
 * Based on Meta Interaction SDK OneGrabTranslateTransformer.
 */

using System;
using UnityEngine;

using static Oculus.Interaction.TransformerUtils;

namespace Oculus.Interaction
{
    /// <summary>
    /// OneGrab Translate 이동에 C/D Ratio를 적용합니다.
    ///
    /// 실제 손 이동량 × C/D Ratio
    /// = 가상 손잡이 이동량
    /// </summary>
    [DisallowMultipleComponent]
    public class OneGrabTranslateCDTransformer :
        MonoBehaviour,
        ITransformer
    {
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
            "실제 손 이동량에 곱할 비율입니다. " +
            "1.0은 일반 이동, 값이 낮을수록 손잡이가 덜 움직입니다."
        )]
        [Range(0.05f, 1f)]
        [SerializeField]
        private float _cdRatio = 0.8f;

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

        // 현재 Grab을 시작한 순간의 손잡이 Transform
        private Vector3 _targetStartLocalPosition;
        private Quaternion _targetStartLocalRotation;

        private float _nextDebugTime;

        public float CDRatio
        {
            get => _cdRatio;
            set => _cdRatio = Mathf.Clamp(value, 0.05f, 1f);
        }

        /// <summary>
        /// C/D 적용 전 SDK가 계산한 Z 이동량입니다.
        /// </summary>
        public float RawZDelta { get; private set; }

        /// <summary>
        /// C/D 적용 후의 Z 이동량입니다.
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
             * SDK 원본과 동일하게,
             * Grab 시 손과 Target 사이의 위치·회전 오프셋을 저장합니다.
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
             * 1. SDK 원본 OneGrabTranslateTransformer와 동일하게
             *    현재 손 위치를 기준으로 원래 목표 Pose를 계산합니다.
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
             * 2. 원래 목표 World Position을
             *    부모 Local Position으로 변환합니다.
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

            /*
             * 3. Grab 시작 위치 기준으로
             *    원래 손의 이동량을 계산합니다.
             */
            Vector3 rawDelta =
                rawTargetLocalPosition -
                _targetStartLocalPosition;

            RawZDelta = rawDelta.z;

            /*
             * 4. 지정한 축에만 C/D Ratio를 적용합니다.
             */
            Vector3 mappedDelta =
                rawDelta;

            if (_applyCDToX)
            {
                mappedDelta.x *= _cdRatio;
            }

            if (_applyCDToY)
            {
                mappedDelta.y *= _cdRatio;
            }

            if (_applyCDToZ)
            {
                mappedDelta.z *= _cdRatio;
            }

            MappedZDelta = mappedDelta.z;

            /*
             * 5. 감쇠된 이동량을 Target에 적용합니다.
             */
            target.localPosition =
                _targetStartLocalPosition +
                mappedDelta;

            // Translate 전용이므로 회전은 Grab 시작값으로 유지
            target.localRotation =
                _targetStartLocalRotation;

            /*
             * 6. 최종 위치에 Min/Max Constraint 적용
             */
            ConstrainTransform();

            DebugValues(target);
        }

        public void EndTransform()
        {
            RawZDelta = 0f;
            MappedZDelta = 0f;
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
                position.x = Mathf.Max(
                    position.x,
                    _parentConstraints.MinX.Value
                );
            }

            if (_parentConstraints.MaxX.Constrain)
            {
                position.x = Mathf.Min(
                    position.x,
                    _parentConstraints.MaxX.Value
                );
            }

            if (_parentConstraints.MinY.Constrain)
            {
                position.y = Mathf.Max(
                    position.y,
                    _parentConstraints.MinY.Value
                );
            }

            if (_parentConstraints.MaxY.Constrain)
            {
                position.y = Mathf.Min(
                    position.y,
                    _parentConstraints.MaxY.Value
                );
            }

            if (_parentConstraints.MinZ.Constrain)
            {
                position.z = Mathf.Max(
                    position.z,
                    _parentConstraints.MinZ.Value
                );
            }

            if (_parentConstraints.MaxZ.Constrain)
            {
                position.z = Mathf.Min(
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
                $"[CD Translate] " +
                $"Ratio:{_cdRatio:F2} | " +
                $"Raw Z:{RawZDelta:F3} | " +
                $"Mapped Z:{MappedZDelta:F3} | " +
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

        public void InjectOptionalCDRatio(float cdRatio)
        {
            CDRatio = cdRatio;
        }

        #endregion
    }
}