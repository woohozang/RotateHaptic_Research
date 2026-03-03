using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class CartSoundController : MonoBehaviour
{
    [Header("References")]
    public CartWeightController WeightController;
    public HandGrabInteractable HandleGrabbable; //

    [Header("Volume Settings (Speed Based)")]
    public float MaxVolume = 0.8f;
    public float MoveSpeedThreshold = 2.0f;    // 볼륨이 최대가 되는 속도 기준
    public float RotationSpeedThreshold = 50f; // 볼륨이 최대가 되는 회전 속도 기준
    public float VolumeSensitivity = 5.0f;     // 속도에 따른 볼륨 반응 민감도

    [Header("Weight Impact (Apples)")]
    public float VolumeIncreasePerApple = 0.05f; // 사과가 많을수록 기본 볼륨 증가
    public float PitchDropPerApple = 0.04f;      // 사과가 많을수록 음정 저하
    public float MinPitch = 0.4f;

    private AudioSource _audioSource;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.volume = 0;
    }

    void Start()
    {
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }

    void Update()
    {
        // 1. 핸들을 잡고 있는지 확인
        if (HandleGrabbable != null && HandleGrabbable.SelectingInteractors.Count > 0)
        {
            if (!_audioSource.isPlaying) _audioSource.Play();

            // 2. 이동 및 회전 속도 계산
            float deltaTime = Time.deltaTime > 0 ? Time.deltaTime : 0.01f;
            float moveSpeed = (transform.position - _lastPosition).magnitude / deltaTime;
            float rotSpeed = Quaternion.Angle(transform.rotation, _lastRotation) / deltaTime;

            // 3. 속도에 따른 볼륨 인자 계산 (0 ~ 1 사이 값)
            float speedFactor = Mathf.Clamp01((moveSpeed / MoveSpeedThreshold) + (rotSpeed / RotationSpeedThreshold));

            // 4. 사과 개수에 따른 가중치 계산
            int appleCount = WeightController.GetCurrentAppleCount();
            float weightVolumeBonus = appleCount * VolumeIncreasePerApple;
            float targetPitch = Mathf.Max(1.0f - (appleCount * PitchDropPerApple), MinPitch);

            // 5. 최종 사운드 적용
            // $Volume = \text{speedFactor} \times (\text{BaseVolume} + \text{weightVolumeBonus})$
            _audioSource.volume = Mathf.Lerp(_audioSource.volume, speedFactor * (0.2f + weightVolumeBonus), Time.deltaTime * VolumeSensitivity);
            _audioSource.pitch = targetPitch;
        }
        else
        {
            // 잡지 않으면 볼륨을 빠르게 줄인 후 정지
            _audioSource.volume = Mathf.Lerp(_audioSource.volume, 0, Time.deltaTime * 10f);
            if (_audioSource.volume < 0.01f && _audioSource.isPlaying) _audioSource.Stop();
        }

        // 위치 및 회전 기록 갱신
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }
}