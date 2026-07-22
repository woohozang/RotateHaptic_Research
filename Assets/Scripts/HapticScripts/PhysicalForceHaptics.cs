using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Oculus.Interaction;

public class PhysicalForceHaptics : MonoBehaviour
{
    [Header("References")]
    public Grabbable grabbable;
    public Transform cartRoot;
    public Transform cartPivot;
    public Transform cargoObject;

    [Header("Weight Position Mapping")]
    [Tooltip("Weight x 위치가 이 값에 도달하면 최대 편향으로 간주합니다.")]
    public float maxWeightOffsetX = 0.2f;

    [Tooltip("중앙 근처에서는 좌우 편향 진동은 줄이고, 기본 양손 진동만 제공합니다.")]
    public float centerDeadZone = 0.015f;

    [Tooltip("1이면 선형에 가깝고, 낮을수록 초반부터 더 잘 느껴집니다.")]
    public float biasCurvePower = 0.9f;

    [Header("Controller Speed Mapping")]
    [Tooltip("이 속도 이하에서는 움직임 기반 추가 진동을 거의 내지 않습니다.")]
    public float controllerSpeedDeadZone = 0.03f;

    [Tooltip("이 컨트롤러 속도에서 최대 진동 비율에 도달합니다.")]
    public float controllerSpeedForMax = 1.1f;

    [Tooltip("컨트롤러 속도 외에 카트 이동 속도도 보조로 반영합니다.")]
    public float cartSpeedForMax = 0.7f;

    [Tooltip("제자리 회전에서도 피드백이 나오도록 yaw 속도를 보조로 반영합니다.")]
    public float yawSpeedForMax = 90f;

    [Range(0f, 1f)]
    public float yawContribution = 0.45f;

    [Header("Default Center Haptics")]
    [Tooltip("무게추가 중앙에 있어도 양손에 기본 진동을 줄지 여부")]
    public bool useDefaultCenterHaptics = true;

    [Tooltip("양손에 항상 들어가는 기본 진동 세기입니다.")]
    [Range(0f, 1f)]
    public float centerDefaultAmp = 0.12f;

    [Tooltip("움직임이 있을 때 기본 진동에 추가되는 양입니다.")]
    [Range(0f, 1f)]
    public float centerMotionBoost = 0.06f;

    [Header("Asymmetry Tuning")]
    [Tooltip("반대쪽 손의 진동 비율입니다. 낮을수록 치우친 쪽이 명확합니다.")]
    [Range(0f, 1f)]
    public float weakSideRatio = 0.22f;

    [Header("Haptic Limits")]
    [Tooltip("편향이 있을 때 추가되는 최소 진동 세기")]
    [Range(0f, 1f)]
    public float minForceAmp = 0.08f;

    [Tooltip("편향이 있을 때 추가되는 최대 진동 세기")]
    [Range(0f, 1f)]
    public float maxForceAmp = 0.95f;

    [Tooltip("전체 진동 배율입니다. 전체적으로 약하면 1.2~1.5 사이로 올리세요.")]
    [Range(0.5f, 3f)]
    public float globalAmpMultiplier = 1.35f;

    [Tooltip("진동 질감 고정. Quest 기준 0.2~0.35가 묵직하게 느껴집니다.")]
    public float fixedFrequency = 0.30f;

    [Tooltip("값이 높을수록 진동 변화가 빠르게 따라옵니다.")]
    [Range(0.01f, 1f)]
    public float hapticSmoothing = 0.22f;

    [Header("CSV Log Settings")]
    [Tooltip("CSV 파일 저장 여부")]
    public bool saveCsvLog = true;

    [Tooltip("Unity Console에도 로그를 출력할지 여부")]
    public bool showConsoleLog = false;

    [Tooltip("CSV 기록 간격입니다. 0이면 매 프레임 기록합니다.")]
    [Min(0f)]
    public float logInterval = 0.05f;

    [Tooltip("파일 내용을 실제 디스크에 반영하는 간격입니다.")]
    [Min(0.1f)]
    public float flushInterval = 1f;




    private Vector3 _lastPosition;
    private float _lastYaw;

    private float _currentLeftAmp;
    private float _currentRightAmp;

    // 컨트롤러 속도 로그용
    private float _currentLeftControllerSpeed;
    private float _currentRightControllerSpeed;

    // CSV 로그용
    private StreamWriter _logWriter;
    private string _logFilePath;

    private float _logStartTime;
    private float _nextLogTime;
    private float _nextFlushTime;

    public string LogFilePath => _logFilePath;

    void Start()
    {
        ResetMotionReference();

        if (saveCsvLog)
        {
            CreateLogFile();
        }
    }

    void Update()
    {
        if (grabbable == null || grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            ResetMotionReference();
            return;
        }

        UpdateContinuousHaptics();
    }

    private void UpdateContinuousHaptics()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f ||
            cartRoot == null ||
            cartPivot == null ||
            cargoObject == null)
        {
            return;
        }

        // 1. 카트 이동 속도
        float cartSpeed =
            (cartRoot.position - _lastPosition).magnitude / dt;

        _lastPosition = cartRoot.position;

        // 2. 카트 회전 속도
        float currentYaw = cartRoot.eulerAngles.y;

        float yawSpeed =
            Mathf.Abs(
                Mathf.DeltaAngle(
                    _lastYaw,
                    currentYaw
                )
            ) / dt;

        _lastYaw = currentYaw;

        // 3. 무게추 좌우 편향 계산
        float localX =
            cartPivot.InverseTransformPoint(
                cargoObject.position
            ).x;

        float effectiveMaxWeightOffsetX =
            maxWeightOffsetX > 0.001f
                ? maxWeightOffsetX
                : 0.2f;

        float bias = Mathf.Clamp(
            localX / effectiveMaxWeightOffsetX,
            -1f,
            1f
        );

        float absBias = Mathf.Abs(bias);

        float biasNorm = Mathf.InverseLerp(
            Mathf.Clamp01(centerDeadZone),
            1f,
            absBias
        );

        biasNorm = Mathf.Pow(
            Mathf.Clamp01(biasNorm),
            Mathf.Max(0.01f, biasCurvePower)
        );

        // 4. 움직임 강도 계산
        float controllerSpeed = GetControllerSpeed();

        float controllerNorm = Mathf.InverseLerp(
            controllerSpeedDeadZone,
            Mathf.Max(
                controllerSpeedDeadZone + 0.01f,
                controllerSpeedForMax
            ),
            controllerSpeed
        );

        float cartNorm = Mathf.InverseLerp(
            0f,
            Mathf.Max(0.01f, cartSpeedForMax),
            cartSpeed
        );

        float yawNorm = Mathf.InverseLerp(
            0f,
            Mathf.Max(0.01f, yawSpeedForMax),
            yawSpeed
        );

        float motionNorm = Mathf.Max(
            Mathf.Clamp01(controllerNorm),
            Mathf.Clamp01(cartNorm)
        );

        float effectiveYawContribution =
            yawContribution > 0f
                ? yawContribution
                : 0.45f;

        motionNorm = Mathf.Clamp01(
            Mathf.Lerp(
                motionNorm,
                Mathf.Max(
                    motionNorm,
                    Mathf.Clamp01(yawNorm)
                ),
                Mathf.Clamp01(
                    effectiveYawContribution
                )
            )
        );

        // 5. 중앙 기본 진동
        float baseAmp = 0f;

        if (useDefaultCenterHaptics)
        {
            baseAmp = centerDefaultAmp;
            baseAmp += centerMotionBoost * motionNorm;
        }

        baseAmp = Mathf.Clamp01(baseAmp);

        // 6. 편향 추가 진동
        float additionalStrongAmp = 0f;
        float additionalWeakAmp = 0f;

        if (biasNorm > 0.001f)
        {
            additionalStrongAmp = Mathf.Lerp(
                minForceAmp,
                maxForceAmp,
                biasNorm
            );

            float effectiveMotion = Mathf.Lerp(
                0.35f,
                1f,
                motionNorm
            );

            additionalStrongAmp *= effectiveMotion;

            additionalWeakAmp =
                additionalStrongAmp *
                Mathf.Clamp01(weakSideRatio);
        }

        // 7. 좌우 목표 진동 결정
        float targetLeftAmp = baseAmp;
        float targetRightAmp = baseAmp;

        if (bias < -centerDeadZone)
        {
            targetLeftAmp += additionalStrongAmp;
            targetRightAmp += additionalWeakAmp;
        }
        else if (bias > centerDeadZone)
        {
            targetLeftAmp += additionalWeakAmp;
            targetRightAmp += additionalStrongAmp;
        }
        else
        {
            targetLeftAmp = baseAmp;
            targetRightAmp = baseAmp;
        }

        // 8. 전체 진동 세기 증폭
        targetLeftAmp *= globalAmpMultiplier;
        targetRightAmp *= globalAmpMultiplier;

        targetLeftAmp = Mathf.Clamp01(targetLeftAmp);
        targetRightAmp = Mathf.Clamp01(targetRightAmp);

        // 9. 진동 변화 부드럽게 적용
        float smoothing =
            hapticSmoothing > 0f
                ? Mathf.Clamp01(hapticSmoothing)
                : 0.22f;

        _currentLeftAmp = Mathf.Lerp(
            _currentLeftAmp,
            targetLeftAmp,
            smoothing
        );

        _currentRightAmp = Mathf.Lerp(
            _currentRightAmp,
            targetRightAmp,
            smoothing
        );

        ApplyHaptics(
            _currentLeftAmp,
            _currentRightAmp
        );

        // 10. CSV 로그 기록
        WriteLogIfNeeded(
            localX,
            bias,
            biasNorm,
            cartSpeed,
            yawSpeed,
            motionNorm,
            _currentLeftControllerSpeed,
            _currentRightControllerSpeed,
            _currentLeftAmp,
            _currentRightAmp
        );

        FlushLogIfNeeded();
    }

    private float GetControllerSpeed()
    {
        _currentLeftControllerSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.LTouch
            ).magnitude;

        _currentRightControllerSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.RTouch
            ).magnitude;

        return Mathf.Max(
            _currentLeftControllerSpeed,
            _currentRightControllerSpeed
        );
    }

    private void ApplyHaptics(float l, float r)
    {
        OVRInput.SetControllerVibration(
            fixedFrequency,
            Mathf.Clamp01(l),
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            fixedFrequency,
            Mathf.Clamp01(r),
            OVRInput.Controller.RTouch
        );
    }

    private void StopHaptics()
    {
        _currentLeftAmp = 0f;
        _currentRightAmp = 0f;

        OVRInput.SetControllerVibration(
            0f,
            0f,
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            0f,
            0f,
            OVRInput.Controller.RTouch
        );
    }

    private void ResetMotionReference()
    {
        if (cartRoot != null)
        {
            _lastPosition = cartRoot.position;
            _lastYaw = cartRoot.eulerAngles.y;
        }
    }

    // =========================================================
    // CSV 로그
    // =========================================================

    private void CreateLogFile()
    {
        try
        {
            string folderPath = Path.Combine(
                Application.persistentDataPath,
                "PhysicalForceHapticLogs"
            );

            Directory.CreateDirectory(folderPath);

            string fileName =
                "PhysicalForceHaptic_" +
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture
                ) +
                ".csv";

            _logFilePath = Path.Combine(
                folderPath,
                fileName
            );

            _logWriter = new StreamWriter(
                _logFilePath,
                false
            );

            // Excel 한글 깨짐 방지용 UTF-8 BOM
            _logWriter.Write('\uFEFF');

            _logWriter.WriteLine(
                "DateTime," +
                "ElapsedTime_sec," +
                "Frame," +
                "CargoLocalX_m," +
                "Bias," +
                "BiasNormalized," +
                "LeftAmplitude," +
                "RightAmplitude," +
                "HapticFrequency," +
                "LeftControllerSpeed_mps," +
                "RightControllerSpeed_mps," +
                "CartSpeed_mps," +
                "YawSpeed_degps," +
                "MotionNormalized"
            );

            _logWriter.Flush();

            _logStartTime = Time.unscaledTime;
            _nextLogTime = Time.unscaledTime;
            _nextFlushTime =
                Time.unscaledTime + flushInterval;

            Debug.Log(
                "[PhysicalForceHaptics] CSV 로그 파일 생성\n" +
                _logFilePath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[PhysicalForceHaptics] CSV 파일 생성 실패\n" +
                exception
            );

            CloseLogFile();
        }
    }

    private void WriteLogIfNeeded(
        float cargoLocalX,
        float bias,
        float biasNormalized,
        float cartSpeed,
        float yawSpeed,
        float motionNormalized,
        float leftControllerSpeed,
        float rightControllerSpeed,
        float leftAmplitude,
        float rightAmplitude
    )
    {
        if (!ShouldWriteLog())
        {
            return;
        }

        float elapsedTime =
            Time.unscaledTime - _logStartTime;

        if (showConsoleLog)
        {
            Debug.Log(
                "[PhysicalForceHaptics Log] " +
                $"CargoX={cargoLocalX:F4}, " +
                $"Bias={bias:F3}, " +
                $"L Amp={leftAmplitude:F3}, " +
                $"R Amp={rightAmplitude:F3}, " +
                $"L Speed={leftControllerSpeed:F3} m/s, " +
                $"R Speed={rightControllerSpeed:F3} m/s"
            );
        }

        if (!saveCsvLog || _logWriter == null)
        {
            return;
        }

        try
        {
            string line = string.Join(
                ",",
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture
                ),
                elapsedTime.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                Time.frameCount.ToString(
                    CultureInfo.InvariantCulture
                ),
                cargoLocalX.ToString(
                    "F5",
                    CultureInfo.InvariantCulture
                ),
                bias.ToString(
                    "F5",
                    CultureInfo.InvariantCulture
                ),
                biasNormalized.ToString(
                    "F5",
                    CultureInfo.InvariantCulture
                ),
                leftAmplitude.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                rightAmplitude.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                fixedFrequency.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                leftControllerSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                rightControllerSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                cartSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                yawSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                motionNormalized.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                )
            );

            _logWriter.WriteLine(line);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[PhysicalForceHaptics] CSV 데이터 기록 실패\n" +
                exception
            );

            CloseLogFile();
        }
    }

    private bool ShouldWriteLog()
    {
        if (logInterval <= 0f)
        {
            return true;
        }

        if (Time.unscaledTime < _nextLogTime)
        {
            return false;
        }

        _nextLogTime =
            Time.unscaledTime + logInterval;

        return true;
    }

    private void FlushLogIfNeeded()
    {
        if (_logWriter == null)
        {
            return;
        }

        if (Time.unscaledTime < _nextFlushTime)
        {
            return;
        }

        _nextFlushTime =
            Time.unscaledTime + flushInterval;

        try
        {
            _logWriter.Flush();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[PhysicalForceHaptics] CSV Flush 실패\n" +
                exception
            );

            CloseLogFile();
        }
    }

    private void CloseLogFile()
    {
        if (_logWriter == null)
        {
            return;
        }

        try
        {
            _logWriter.Flush();
            _logWriter.Close();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[PhysicalForceHaptics] CSV 종료 오류\n" +
                exception
            );
        }
        finally
        {
            _logWriter = null;
        }
    }

    private void OnDisable()
    {
        StopHaptics();
        CloseLogFile();
    }

    private void OnApplicationQuit()
    {
        StopHaptics();
        CloseLogFile();
    }
}