using UnityEngine;

public class AsymmetricCartAudio : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource leftWheelSource;
    public AudioSource rightWheelSource;
    public AudioClip rollingClip; // 반복되는 카트 구르는 소리

    [Header("Parameters")]
    public Transform cartRoot;
    public Transform cargoObject;
    public Transform cartPivot;

    [Header("Audio Tuning")]
    public float maxVolume = 0.7f;
    public float minPitch = 0.85f; // 무거울 때
    public float maxPitch = 1.15f; // 가벼울 때
    public float velocitySensitivity = 0.5f;

    private Vector3 _lastPos;

    void Start()
    {
        // 양쪽 소스 초기 설정
        SetupSource(leftWheelSource);
        SetupSource(rightWheelSource);
        _lastPos = cartRoot.position;
    }

    void SetupSource(AudioSource source)
    {
        source.clip = rollingClip;
        source.loop = true;
        source.spatialBlend = 1.0f; // 3D 음향 활성화
        source.playOnAwake = true;
        source.volume = 0;
        source.Play();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 velocity = (cartRoot.position - _lastPos) / dt;
        float speed = velocity.magnitude;
        _lastPos = cartRoot.position;

        // 1. 무게 편향 계산 (기존 로직 공유)
        float localX = cartPivot.InverseTransformPoint(cargoObject.position).x;
        float bias = Mathf.Clamp(localX * 5.5f, -1f, 1f); // 왼쪽 -1, 오른쪽 1

        // 2. 속도에 따른 베이스 볼륨 (멈추면 소리 안 남)
        float baseVolume = Mathf.Clamp01(speed * velocitySensitivity);

        // 3. 🔥 비대칭 사운드 매핑
        // 무게가 쏠린 쪽은 더 크게, 더 낮은 피치로
        float leftWeight = Mathf.Clamp01(-bias);
        float rightWeight = Mathf.Clamp01(bias);

        // 왼쪽 휠 설정
        leftWheelSource.volume = Mathf.Lerp(baseVolume * 0.5f, baseVolume * 1.0f, leftWeight) * maxVolume;
        leftWheelSource.pitch = Mathf.Lerp(1.0f, minPitch, leftWeight);

        // 오른쪽 휠 설정
        rightWheelSource.volume = Mathf.Lerp(baseVolume * 0.5f, baseVolume * 1.0f, rightWeight) * maxVolume;
        rightWheelSource.pitch = Mathf.Lerp(1.0f, minPitch, rightWeight);

        // 반대쪽(가벼운 쪽) 피치 올려주기 (선택 사항)
        if (bias > 0) leftWheelSource.pitch = Mathf.Lerp(1.0f, maxPitch, rightWeight);
        else rightWheelSource.pitch = Mathf.Lerp(1.0f, maxPitch, leftWeight);
    }
}