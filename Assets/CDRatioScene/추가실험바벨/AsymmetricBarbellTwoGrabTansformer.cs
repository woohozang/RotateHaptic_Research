using UnityEngine;

namespace Oculus.Interaction
{
    /// <summary>
    /// 바벨용 비대칭 Upward-Lag Transformer
    ///
    /// Up:
    /// - 무거운 쪽만 느리게 따라오도록 Lag 적용
    ///
    /// Down:
    /// - Follow/Lerp를 사용하지 않음
    /// - 실제 손의 하강 이동량을 그대로 1:1 적용
    /// - 따라서 내려갈 때 지연이나 catch-up이 발생하지 않음
    ///
    /// Hold:
    /// - 현재 좌우 비대칭 상태 유지
    /// </summary>
    public class AsymmetricBarbellTwoGrabTransformer :
        MonoBehaviour, ITransformer
    {
        [Header("Virtual Grab Points")]
        [SerializeField]
        private Transform _leftGrabPoint;

        [SerializeField]
        private Transform _rightGrabPoint;


        [Header("C/D Condition")]
        [Range(0.5f, 1f)]
        [SerializeField]
        private float _leftCD = 1f;

        [Range(0.5f, 1f)]
        [SerializeField]
        private float _rightCD = 1f;


        [Header("Upward Lag")]

        [Tooltip("C/D 0.75 조건의 상승 추종 속도")]
        [SerializeField]
        private float _mediumFollowSpeed = 8f;

        [Tooltip("C/D 0.5 조건의 상승 추종 속도. 낮을수록 강한 저항")]
        [SerializeField]
        private float _strongFollowSpeed = 3.5f;

        [Tooltip("무거운 쪽이 실제 손보다 아래로 처질 수 있는 최대 거리")]
        [SerializeField]
        private float _maxVerticalLag = 0.15f;


        [Header("Direction Detection")]

        [Tooltip("상승/하강 판정을 위한 수직 이동 Dead Zone")]
        [SerializeField]
        private float _verticalDeadZone = 0.0007f;


        private IGrabbable _grabbable;

        private int _leftIndex;
        private int _rightIndex;


        // 실제 손과 바벨 Grip 사이 초기 Offset
        private Vector3 _leftInitialOffset;
        private Vector3 _rightInitialOffset;


        // 이전 Frame 실제 손 위치
        private Vector3 _previousLeftHand;
        private Vector3 _previousRightHand;


        // 가상의 좌/우 Grab Point
        private Vector3 _virtualLeft;
        private Vector3 _virtualRight;


        // 바벨 자세 계산용
        private Vector3 _centerOffset;
        private Vector3 _initialDirection;
        private Quaternion _initialRotation;


        private enum VerticalState
        {
            Hold,
            Up,
            Down
        }

        private VerticalState _state =
            VerticalState.Hold;


        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
        }


        public void BeginTransform()
        {
            if (_grabbable == null ||
                _grabbable.GrabPoints == null ||
                _grabbable.GrabPoints.Count < 2)
            {
                return;
            }


            AssignLeftRight();


            Pose leftPose =
                _grabbable.GrabPoints[_leftIndex];

            Pose rightPose =
                _grabbable.GrabPoints[_rightIndex];


            _previousLeftHand =
                leftPose.position;

            _previousRightHand =
                rightPose.position;


            _virtualLeft =
                _leftGrabPoint.position;

            _virtualRight =
                _rightGrabPoint.position;


            // 실제 Controller와 Grab Point의 초기 차이
            _leftInitialOffset =
                _virtualLeft -
                leftPose.position;

            _rightInitialOffset =
                _virtualRight -
                rightPose.position;


            Transform target =
                _grabbable.Transform;


            _initialRotation =
                target.rotation;


            Vector3 midpoint =
                (_virtualLeft +
                 _virtualRight) * 0.5f;


            _centerOffset =
                target.position -
                midpoint;


            Vector3 direction =
                _virtualRight -
                _virtualLeft;


            if (direction.sqrMagnitude <
                0.000001f)
            {
                direction =
                    target.right;
            }


            _initialDirection =
                direction.normalized;


            _state =
                VerticalState.Hold;
        }


        public void UpdateTransform()
        {
            if (_grabbable == null ||
                _grabbable.GrabPoints == null ||
                _grabbable.GrabPoints.Count < 2)
            {
                return;
            }


            Pose leftPose =
                _grabbable.GrabPoints[_leftIndex];

            Pose rightPose =
                _grabbable.GrabPoints[_rightIndex];


            Vector3 currentLeftHand =
                leftPose.position;

            Vector3 currentRightHand =
                rightPose.position;


            // =========================================
            // 1. 이번 Frame의 실제 손 이동량
            // =========================================

            Vector3 leftFrameDelta =
                currentLeftHand -
                _previousLeftHand;

            Vector3 rightFrameDelta =
                currentRightHand -
                _previousRightHand;


            float averageDeltaY =
                (leftFrameDelta.y +
                 rightFrameDelta.y) * 0.5f;


            // =========================================
            // 2. Up / Down / Hold 판정
            // =========================================

            if (averageDeltaY >
                _verticalDeadZone)
            {
                _state =
                    VerticalState.Up;
            }
            else if (averageDeltaY <
                     -_verticalDeadZone)
            {
                _state =
                    VerticalState.Down;
            }
            else
            {
                _state =
                    VerticalState.Hold;
            }


            // 실제 손에 완전히 붙는 경우의 목표 위치
            Vector3 desiredLeft =
                currentLeftHand +
                _leftInitialOffset;

            Vector3 desiredRight =
                currentRightHand +
                _rightInitialOffset;


            // =========================================
            // 3. X / Z는 항상 완전한 1:1
            // =========================================

            _virtualLeft.x =
                desiredLeft.x;

            _virtualLeft.z =
                desiredLeft.z;


            _virtualRight.x =
                desiredRight.x;

            _virtualRight.z =
                desiredRight.z;


            // =========================================
            // 4. Y축 처리
            // =========================================

            if (_state ==
                VerticalState.Up)
            {
                // -------------------------------
                // 올라갈 때만 Lag 적용
                // -------------------------------

                UpdateUpward(
                    ref _virtualLeft.y,
                    desiredLeft.y,
                    _leftCD
                );


                UpdateUpward(
                    ref _virtualRight.y,
                    desiredRight.y,
                    _rightCD
                );
            }

            else if (_state ==
                     VerticalState.Down)
            {
                // =================================
                // 내려갈 때는 Lag / Lerp 없음
                //
                // 실제 Controller의 이번 Frame
                // 이동량을 그대로 1:1 적용
                // =================================

                _virtualLeft.y +=
                    leftFrameDelta.y;

                _virtualRight.y +=
                    rightFrameDelta.y;
            }

            // Hold에서는 현재 Y값 유지


            // =========================================
            // 5. 상승 중 최대 Lag 제한
            // =========================================

            if (_state ==
                VerticalState.Up)
            {
                _virtualLeft.y =
                    LimitUpwardLag(
                        _virtualLeft.y,
                        desiredLeft.y
                    );


                _virtualRight.y =
                    LimitUpwardLag(
                        _virtualRight.y,
                        desiredRight.y
                    );
            }


            // =========================================
            // 6. Virtual Grab Point로 Barbell Pose 계산
            // =========================================

            Vector3 virtualMidpoint =
                (_virtualLeft +
                 _virtualRight) * 0.5f;


            Vector3 virtualDirection =
                _virtualRight -
                _virtualLeft;


            Quaternion deltaRotation =
                Quaternion.identity;


            if (virtualDirection.sqrMagnitude >
                0.000001f)
            {
                deltaRotation =
                    Quaternion.FromToRotation(
                        _initialDirection,
                        virtualDirection.normalized
                    );
            }


            Quaternion targetRotation =
                deltaRotation *
                _initialRotation;


            Vector3 targetPosition =
                virtualMidpoint +
                deltaRotation *
                _centerOffset;


            _grabbable.Transform.SetPositionAndRotation(
                targetPosition,
                targetRotation
            );


            // =========================================
            // 7. 현재 실제 손 위치 저장
            // =========================================

            _previousLeftHand =
                currentLeftHand;

            _previousRightHand =
                currentRightHand;
        }


        public void EndTransform()
        {
            _state =
                VerticalState.Hold;
        }


        // =============================================
        // 상승 동작
        // =============================================

        private void UpdateUpward(
            ref float virtualY,
            float desiredY,
            float cd)
        {
            // 1:1 조건은 완전 추종
            if (cd >= 0.99f)
            {
                virtualY =
                    desiredY;

                return;
            }


            float speed =
                GetFollowSpeed(cd);


            float t =
                1f -
                Mathf.Exp(
                    -speed *
                    Time.deltaTime
                );


            virtualY =
                Mathf.Lerp(
                    virtualY,
                    desiredY,
                    t
                );
        }


        // =============================================
        // 최대 상승 Lag 제한
        // =============================================

        private float LimitUpwardLag(
            float virtualY,
            float desiredY)
        {
            // 가상 Grip이 실제 Grip보다
            // 최대 maxVerticalLag까지만 아래로 처짐
            float minimumY =
                desiredY -
                _maxVerticalLag;


            // 위로 앞서가지는 못하게 함
            float maximumY =
                desiredY;


            return Mathf.Clamp(
                virtualY,
                minimumY,
                maximumY
            );
        }


        // =============================================
        // C/D 조건 → 상승 Follow Speed
        // =============================================

        private float GetFollowSpeed(float cd)
        {
            if (cd >= 0.99f)
            {
                return 1000f;
            }


            // 0.75
            if (cd >= 0.70f)
            {
                return _mediumFollowSpeed;
            }


            // 0.5
            return _strongFollowSpeed;
        }


        // =============================================
        // Condition Controller 호환
        // =============================================

        public void SetCDRatio(
            float left,
            float right)
        {
            _leftCD =
                Mathf.Clamp(
                    left,
                    0.5f,
                    1f
                );

            _rightCD =
                Mathf.Clamp(
                    right,
                    0.5f,
                    1f
                );
        }


        // =============================================
        // Left / Right 판별
        // =============================================

        private void AssignLeftRight()
        {
            Pose p0 =
                _grabbable.GrabPoints[0];

            Pose p1 =
                _grabbable.GrabPoints[1];


            float normal =
                Vector3.Distance(
                    p0.position,
                    _leftGrabPoint.position
                )
                +
                Vector3.Distance(
                    p1.position,
                    _rightGrabPoint.position
                );


            float swapped =
                Vector3.Distance(
                    p1.position,
                    _leftGrabPoint.position
                )
                +
                Vector3.Distance(
                    p0.position,
                    _rightGrabPoint.position
                );


            if (normal <= swapped)
            {
                _leftIndex = 0;
                _rightIndex = 1;
            }
            else
            {
                _leftIndex = 1;
                _rightIndex = 0;
            }
        }
    }
}