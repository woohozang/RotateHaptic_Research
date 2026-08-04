using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Oculus.Interaction;

public class SingleVibrationWeightHapticController : MonoBehaviour
{
    public enum WeightSide { Left, Right }
    public enum BottleCount { Two = 2, Four = 4, Six = 6 }

    [Header("References")]
    [SerializeField] private Grabbable _grabbable;

    [Header("Experiment Condition")]
    public WeightSide weightSide = WeightSide.Left;
    public BottleCount bottleCount = BottleCount.Two;

    [Header("Anti-Shake Settings")]
    public float speedDeadZone = 0.06f;

    [Range(0.01f, 1f)]
    public float hapticSmoothing = 0.18f;

    [Header("Speed Response Settings")]
    [Tooltip("이 속도부터 진동이 시작됩니다.")]
    public float speedGateStart = 0.06f;

    [Tooltip("이 속도 이상부터 무게 진동이 100% 반영됩니다.")]
    public float speedGateFull = 0.45f;

    [Tooltip("속도 변화 체감을 강화합니다.")]
    public float speedCurvePower = 1.8f;

    [Header("Speed Haptic Settings")]
    public float speedSensitivity = 0.9f;

    [Range(0f, 1f)]
    public float maxSpeedHaptic = 0.35f;

    [Header("Bottle Weight Haptic Settings")]
    [Range(0f, 1f)] public float bottle2Haptic = 0.12f;
    [Range(0f, 1f)] public float bottle4Haptic = 0.32f;
    [Range(0f, 1f)] public float bottle6Haptic = 0.55f;

    [Header("Asymmetric Haptic Settings")]
    [Range(0f, 2f)]
    public float strongSideMultiplier = 1.2f;

    [Range(0f, 1f)]
    public float weakSideMultiplier = 0.05f;

    [Header("Output Limits")]
    [Range(0f, 1f)]
    public float maxTotalHaptic = 0.9f;

    [Range(0f, 1f)]
    public float hapticFrequency = 0.18f;

    [Header("Weak Side")]
    [Range(0f, 1f)]
    public float weakSideConstantAmp = 0.08f;

    [Header("Log Settings")]
    [Tooltip("CSV 로그 저장 여부")]
    public bool saveLogFile = true;

    [Tooltip("Unity Console 출력 여부")]
    public bool showConsoleLog = false;

    [Tooltip("로그 기록 간격. 0이면 매 프레임 기록")]
    [Min(0f)]
    public float logInterval = 0.05f;

    [Tooltip("파일 버퍼를 실제 파일에 저장하는 간격")]
    [Min(0.1f)]
    public float flushInterval = 1f;

    private float _smoothedLeftAmp;
    private float _smoothedRightAmp;

    // 로그용 변수
    private StreamWriter _logWriter;
    private string _logFilePath;

    private float _logStartTime;
    private float _nextLogTime;
    private float _nextFlushTime;

    public string LogFilePath => _logFilePath;

    //로그 데이터
    public float LeftHapticAmplitude => _smoothedLeftAmp;
    public float RightHapticAmplitude => _smoothedRightAmp;
    public float HapticFrequency => hapticFrequency;
    public int ConfiguredBottleCount => (int)bottleCount;

    private void Start()
    {
        if (saveLogFile)
        {
            CreateLogFile();
        }
    }

    private void Update()
    {
        if (_grabbable == null || _grabbable.SelectingPointsCount == 0)
        {
            StopHaptics();
            return;
        }

        float lSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.LTouch
            ).magnitude;

        float rSpeed =
            OVRInput.GetLocalControllerVelocity(
                OVRInput.Controller.RTouch
            ).magnitude;

        float activeSpeed = Mathf.Max(lSpeed, rSpeed);

        if (activeSpeed < speedDeadZone)
        {
            SmoothToZero();

            WriteLogIfNeeded(
                state: "DeadZone",
                leftSpeed: lSpeed,
                rightSpeed: rSpeed,
                leftAmplitude: _smoothedLeftAmp,
                rightAmplitude: _smoothedRightAmp
            );

            FlushLogIfNeeded();
            return;
        }

        // 속도 게이트
        float speedGate = Mathf.InverseLerp(
            speedGateStart,
            speedGateFull,
            activeSpeed
        );

        speedGate = Mathf.Pow(
            Mathf.Clamp01(speedGate),
            speedCurvePower
        );

        // 각 손의 속도 정규화
        float lSpeedNorm = Mathf.InverseLerp(
            speedGateStart,
            speedGateFull,
            lSpeed
        );

        float rSpeedNorm = Mathf.InverseLerp(
            speedGateStart,
            speedGateFull,
            rSpeed
        );

        lSpeedNorm = Mathf.Pow(
            Mathf.Clamp01(lSpeedNorm),
            speedCurvePower
        );

        rSpeedNorm = Mathf.Pow(
            Mathf.Clamp01(rSpeedNorm),
            speedCurvePower
        );

        float lSpeedHaptic = Mathf.Clamp(
            lSpeedNorm * speedSensitivity,
            0f,
            maxSpeedHaptic
        );

        float rSpeedHaptic = Mathf.Clamp(
            rSpeedNorm * speedSensitivity,
            0f,
            maxSpeedHaptic
        );

        // 적재물 기본 진동
        float weightHaptic =
            GetBottleWeightHaptic() * speedGate;

        float targetLeftAmp;
        float targetRightAmp;

        if (weightSide == WeightSide.Left)
        {
            targetLeftAmp =
                (weightHaptic * strongSideMultiplier)
                + lSpeedHaptic;

            targetRightAmp =
                weakSideConstantAmp;
        }
        else
        {
            targetLeftAmp =
                weakSideConstantAmp;

            targetRightAmp =
                (weightHaptic * strongSideMultiplier)
                + rSpeedHaptic;
        }

        targetLeftAmp = Mathf.Clamp(
            targetLeftAmp,
            0f,
            maxTotalHaptic
        );

        targetRightAmp = Mathf.Clamp(
            targetRightAmp,
            0f,
            maxTotalHaptic
        );

        _smoothedLeftAmp = Mathf.Lerp(
            _smoothedLeftAmp,
            targetLeftAmp,
            hapticSmoothing
        );

        _smoothedRightAmp = Mathf.Lerp(
            _smoothedRightAmp,
            targetRightAmp,
            hapticSmoothing
        );

        TriggerHaptics(
            _smoothedLeftAmp,
            _smoothedRightAmp
        );

        WriteLogIfNeeded(
            state: "Active",
            leftSpeed: lSpeed,
            rightSpeed: rSpeed,
            leftAmplitude: _smoothedLeftAmp,
            rightAmplitude: _smoothedRightAmp
        );

        FlushLogIfNeeded();
    }

    private float GetBottleWeightHaptic()
    {
        switch (bottleCount)
        {
            case BottleCount.Two:
                return bottle2Haptic;

            case BottleCount.Four:
                return bottle4Haptic;

            case BottleCount.Six:
                return bottle6Haptic;

            default:
                return bottle2Haptic;
        }
    }

    private void SmoothToZero()
    {
        _smoothedLeftAmp = Mathf.Lerp(
            _smoothedLeftAmp,
            0f,
            hapticSmoothing
        );

        _smoothedRightAmp = Mathf.Lerp(
            _smoothedRightAmp,
            0f,
            hapticSmoothing
        );

        TriggerHaptics(
            _smoothedLeftAmp,
            _smoothedRightAmp
        );
    }

    private void TriggerHaptics(
        float leftAmp,
        float rightAmp
    )
    {
        OVRInput.SetControllerVibration(
            hapticFrequency,
            leftAmp,
            OVRInput.Controller.LTouch
        );

        OVRInput.SetControllerVibration(
            hapticFrequency,
            rightAmp,
            OVRInput.Controller.RTouch
        );
    }

    private void StopHaptics()
    {
        _smoothedLeftAmp = 0f;
        _smoothedRightAmp = 0f;

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

    // =========================================================
    // CSV 로그
    // =========================================================

    private void CreateLogFile()
    {
        try
        {
            string folderPath = Path.Combine(
                Application.persistentDataPath,
                "SingleVibrationHapticLogs"
            );

            Directory.CreateDirectory(folderPath);

            string fileName =
                "SingleVibrationHaptic_" +
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

            // Excel 한글 깨짐 방지를 위한 UTF-8 BOM
            _logWriter.Write('\uFEFF');

            _logWriter.WriteLine(
                "DateTime," +
                "ElapsedTime_sec," +
                "Frame," +
                "State," +
                "WeightSide," +
                "BottleCount," +
                "LeftAmplitude," +
                "RightAmplitude," +
                "HapticFrequency," +
                "LeftControllerSpeed_mps," +
                "RightControllerSpeed_mps"
            );

            _logWriter.Flush();

            _logStartTime = Time.unscaledTime;
            _nextLogTime = Time.unscaledTime;
            _nextFlushTime =
                Time.unscaledTime + flushInterval;

            Debug.Log(
                "[SingleVibrationWeightHapticController] " +
                "CSV 로그 파일 생성\n" +
                _logFilePath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[SingleVibrationWeightHapticController] " +
                "CSV 파일 생성 실패\n" +
                exception
            );

            CloseLogFile();
        }
    }

    private void WriteLogIfNeeded(
        string state,
        float leftSpeed,
        float rightSpeed,
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
                "[Single Haptic Log] " +
                $"Side={weightSide}, " +
                $"Bottles={(int)bottleCount}, " +
                $"L Amp={leftAmplitude:F4}, " +
                $"R Amp={rightAmplitude:F4}, " +
                $"L Speed={leftSpeed:F4} m/s, " +
                $"R Speed={rightSpeed:F4} m/s"
            );
        }

        if (!saveLogFile || _logWriter == null)
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
                state,
                weightSide.ToString(),
                ((int)bottleCount).ToString(
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
                hapticFrequency.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                leftSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),
                rightSpeed.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                )
            );

            _logWriter.WriteLine(line);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[SingleVibrationWeightHapticController] " +
                "CSV 데이터 기록 실패\n" +
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
                "[SingleVibrationWeightHapticController] " +
                "CSV Flush 실패\n" +
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
                "[SingleVibrationWeightHapticController] " +
                "CSV 파일 종료 중 오류\n" +
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