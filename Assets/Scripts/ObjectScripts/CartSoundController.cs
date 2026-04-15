using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class CartSoundController : MonoBehaviour
{
    [Header("References")]
    public CartWeightController WeightController;
    public HandGrabInteractable HandleGrabbable;

    [Header("6DoF Sensitivity")]
    public float MaxVolume = 0.8f;
    public float MoveSensitivity = 1.5f;    // 이동 속도 민감도
    public float RotationSensitivity = 0.5f; // 회전 속도 민감도
    public float SmoothingSpeed = 10.0f;    // 사운드 변화 부드러움 정도

    [Header("Weight Timbre (Apples)")]
    [Tooltip("무거울수록 소리가 저음이 됩니다.")]
    public float PitchBase = 1.0f;
    public float PitchDropPerApple = 0.05f;

    private AudioSource _audioSource;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private float _currentVelocity;
    private float _currentAngularVelocity;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;

        // 스크립트 실행 시 자동으로 3D 사운드 설정
        _audioSource.spatialBlend = 1.0f;
        _audioSource.dopplerLevel = 0f; // 카트는 느리므로 도플러 효과는 끕니다.
    }

    void Start()
    {
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }

    void Update()
    {
        bool isGrabbing = HandleGrabbable != null && HandleGrabbable.SelectingInteractors.Count > 0;

        if (isGrabbing)
        {
            if (!_audioSource.isPlaying) _audioSource.Play();

            Calculate6DoFPhysics();
            ApplySpatialDynamics();
        }
        else
        {
            // 잡지 않을 때는 서서히 페이드 아웃
            _audioSource.volume = Mathf.Lerp(_audioSource.volume, 0, Time.deltaTime * SmoothingSpeed);
            if (_audioSource.volume < 0.001f) _audioSource.Stop();
        }
    }

    private void Calculate6DoFPhysics()
    {
        float dt = Time.deltaTime > 0 ? Time.deltaTime : 0.02f;

        // 1. 위치 변화량 (Linear Velocity)
        Vector3 moveDelta = (transform.position - _lastPosition) / dt;
        _currentVelocity = moveDelta.magnitude;

        // 2. 회전 변화량 (Angular Velocity)
        float angleDelta = Quaternion.Angle(transform.rotation, _lastRotation) / dt;
        _currentAngularVelocity = angleDelta;

        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }

    private void ApplySpatialDynamics()
    {
        // 1. 미세 움직임 무시 (Deadzone 강화)
        float effectiveMove = _currentVelocity > 0.1f ? _currentVelocity : 0; // 0.1m/s 이하 무시
        float effectiveRot = _currentAngularVelocity > 5.0f ? _currentAngularVelocity : 0; // 5도 이하 무시

        // 2. 민감도 적용 및 정규화
        float movementFactor = effectiveMove * MoveSensitivity;
        float rotationFactor = (effectiveRot / 180f) * RotationSensitivity;

        // 3. 비선형 볼륨 곡선 (작은 움직임엔 더 작게)
        float combinedFactor = Mathf.Clamp01(movementFactor + rotationFactor);
        float targetVolume = Mathf.Pow(combinedFactor, 2) * MaxVolume; // 제곱(Pow)을 쓰면 더 급격히 작아집니다.

        // 4. 적용
        _audioSource.volume = Mathf.Lerp(_audioSource.volume, targetVolume, Time.deltaTime * SmoothingSpeed);
    }
}