using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Task 2(제자리 회전) 전용 시행 요약 로그입니다.
///
/// - 양손으로 잡으면 시행 시작
/// - 한 손이라도 놓으면 시행 종료
/// - 한 시행의 평균/최소/최대값을 CSV 한 행으로 저장
/// - 한글 CSV 헤더와 UTF-8 BOM을 사용하므로 Excel에서 한글이 깨지지 않음
///
/// C/D Ratio 정의:
/// 실제 컨트롤러의 누적 회전량 / 가상 손의 누적 회전량
/// </summary>
public class Task2LogData : MonoBehaviour
{
    [Header("실험 정보")]
    [Tooltip("참가자 ID")]
    public string participantId = "P001";

    [Tooltip("피드백 조건: Baseline, Visual, Haptic, Combined 등")]
    public string conditionId = "Combined";

    [Tooltip("현재 시행 번호")]
    public int trialIndex = 1;

    [Tooltip("현재 조건의 적재 개수")]
    public int bottleCount = 0;

    [Tooltip("시행 종료 후 시행 번호를 자동 증가시킵니다.")]
    public bool autoIncreaseTrialIndex = true;

    [Header("적용 모델 정보")]
    [Tooltip("CSV에 표시할 모델 이름")]
    public string modelName = "Task2_DGrabFinal_PhysicalForceHaptics";

    [Tooltip("Task 2에서 사용하는 C/D Transformer")]
    public DGrab_Final cdTransformer;

    [Tooltip("Task 2에서 사용하는 진동 모델")]
    public PhysicalForceHaptics hapticSource;

    [Header("양손 잡기 판정")]
    [Tooltip("BothHandle의 Grabbable")]
    public Grabbable grabbable;

    [Tooltip("두 손으로 잡은 동안에만 기록합니다.")]
    public bool logOnlyWhileTwoHandGrabbed = true;

    [Header("실제 컨트롤러 Transform")]
    [Tooltip("OVRCameraRig/TrackingSpace/LeftControllerAnchor")]
    public Transform leftController;

    [Tooltip("OVRCameraRig/TrackingSpace/RightControllerAnchor")]
    public Transform rightController;

    [Header("가상 손 Transform")]
    [Tooltip("실제 왼쪽 컨트롤러와 대응하는 가상 왼손 기준점")]
    public Transform leftVirtualHand;

    [Tooltip("실제 오른쪽 컨트롤러와 대응하는 가상 오른손 기준점")]
    public Transform rightVirtualHand;

    [Header("PosFollowLag 설정값")]
    [Tooltip("DGrab_Final의 Left Pos Follow와 같은 값으로 설정")]
    public float leftPosFollowLag = 3.5f;

    [Tooltip("DGrab_Final의 Right Pos Follow와 같은 값으로 설정")]
    public float rightPosFollowLag = 3.5f;

    [Tooltip("DGrab_Final의 Yaw Follow Base와 같은 값으로 설정")]
    public float yawFollowLag = 4.0f;

    [Header("카트 / 무게중심")]
    [Tooltip("카트 중앙 기준점")]
    public Transform cartPivot;

    [Tooltip("동적으로 좌우 이동하는 Weight 오브젝트")]
    public Transform cargoObject;

    [Tooltip("이 값 이내의 X 위치는 중앙으로 판정합니다.")]
    [Min(0f)]
    public float centerDeadZone = 0.01f;

    [Header("기록 설정")]
    [Range(1f, 120f)]
    [Tooltip("초당 측정 횟수. CSV에는 시행별 요약 한 행만 기록됩니다.")]
    public float sampleRateHz = 30f;

    [Min(0f)]
    [Tooltip("이 시간보다 짧은 양손 잡기는 저장하지 않습니다.")]
    public float minimumTrialDuration = 0.3f;

    [Tooltip("씬 시작 시 CSV 파일을 생성합니다.")]
    public bool createFileOnStart = true;

    private StreamWriter _writer;
    private string _filePath;
    private bool _trialActive;
    private float _trialStartTime;
    private float _lastSampleTime;

    private Vector3 _previousLeftPosition;
    private Vector3 _previousRightPosition;
    private Quaternion _leftStartRotation;
    private Quaternion _rightStartRotation;
    private Quaternion _previousLeftRotation;
    private Quaternion _previousRightRotation;
    private Quaternion _previousLeftVirtualRotation;
    private Quaternion _previousRightVirtualRotation;

    private double _leftActualRotationPath;
    private double _rightActualRotationPath;
    private double _leftVirtualRotationPath;
    private double _rightVirtualRotationPath;

    private int _leftBiasSamples;
    private int _rightBiasSamples;
    private int _centerBiasSamples;

    private ScalarStatistics _leftRotationStats;
    private ScalarStatistics _rightRotationStats;
    private ScalarStatistics _leftSpeedStats;
    private ScalarStatistics _rightSpeedStats;
    private ScalarStatistics _leftVisualOffsetStats;
    private ScalarStatistics _rightVisualOffsetStats;
    private ScalarStatistics _leftHapticStats;
    private ScalarStatistics _rightHapticStats;
    private ScalarStatistics _weightXStats;

    public string FilePath => _filePath;
    public bool IsTrialActive => _trialActive;

    private void Start()
    {
        if (createFileOnStart)
        {
            CreateCsvFile();
        }
    }

    private void Update()
    {
        bool canRecord = grabbable != null;
        bool isTwoHandGrabbed =
            canRecord && grabbable.SelectingPointsCount >= 2;

        bool shouldRecord = logOnlyWhileTwoHandGrabbed
            ? isTwoHandGrabbed
            : canRecord && grabbable.SelectingPointsCount > 0;

        if (shouldRecord && !_trialActive)
        {
            BeginTrial();
        }
        else if (!shouldRecord && _trialActive)
        {
            EndTrial();
        }

        if (!_trialActive)
        {
            return;
        }

        float sampleInterval = 1f / Mathf.Max(1f, sampleRateHz);
        if (Time.unscaledTime - _lastSampleTime >= sampleInterval)
        {
            SampleData();
        }
    }

    public void BeginTrial()
    {
        if (_trialActive)
        {
            return;
        }

        if (_writer == null)
        {
            CreateCsvFile();
        }

        if (_writer == null)
        {
            return;
        }

        ResetTrialData();
        _trialActive = true;
        _trialStartTime = Time.unscaledTime;
        _lastSampleTime = Time.unscaledTime;

        if (leftController != null)
        {
            _previousLeftPosition = leftController.position;
            _leftStartRotation = leftController.rotation;
            _previousLeftRotation = leftController.rotation;
        }

        if (rightController != null)
        {
            _previousRightPosition = rightController.position;
            _rightStartRotation = rightController.rotation;
            _previousRightRotation = rightController.rotation;
        }

        if (leftVirtualHand != null)
        {
            _previousLeftVirtualRotation = leftVirtualHand.rotation;
        }

        if (rightVirtualHand != null)
        {
            _previousRightVirtualRotation = rightVirtualHand.rotation;
        }

        Debug.Log(
            $"[Task2LogData] 시행 시작 | {participantId} | " +
            $"{conditionId} | Trial {trialIndex}"
        );
    }

    public void EndTrial()
    {
        if (!_trialActive)
        {
            return;
        }

        float duration = Time.unscaledTime - _trialStartTime;
        _trialActive = false;

        if (duration < minimumTrialDuration || GetSampleCount() == 0)
        {
            Debug.LogWarning(
                $"[Task2LogData] 너무 짧거나 샘플이 없는 시행은 저장하지 않습니다. " +
                $"시간: {duration:F3}초"
            );
            return;
        }

        WriteTrialSummary(duration);

        if (autoIncreaseTrialIndex)
        {
            trialIndex++;
        }
    }

    private void SampleData()
    {
        float now = Time.unscaledTime;
        float dt = Mathf.Max(0.0001f, now - _lastSampleTime);
        _lastSampleTime = now;

        SampleLeftController(dt);
        SampleRightController(dt);
        SampleVisualOffsetsAndCdRatio();
        SampleHaptics();
        SampleCenterOfMass();
    }

    private void SampleLeftController(float dt)
    {
        if (leftController == null)
        {
            return;
        }

        float rotationFromStart = Quaternion.Angle(
            _leftStartRotation,
            leftController.rotation
        );

        float transformSpeed = Vector3.Distance(
            _previousLeftPosition,
            leftController.position
        ) / dt;

        Vector3 ovrVelocity = OVRInput.GetLocalControllerVelocity(
            OVRInput.Controller.LTouch
        );

        float speed = ovrVelocity.sqrMagnitude > 0.000001f
            ? ovrVelocity.magnitude
            : transformSpeed;

        _leftRotationStats.Add(rotationFromStart);
        _leftSpeedStats.Add(speed);
        _leftActualRotationPath += Quaternion.Angle(
            _previousLeftRotation,
            leftController.rotation
        );

        _previousLeftPosition = leftController.position;
        _previousLeftRotation = leftController.rotation;
    }

    private void SampleRightController(float dt)
    {
        if (rightController == null)
        {
            return;
        }

        float rotationFromStart = Quaternion.Angle(
            _rightStartRotation,
            rightController.rotation
        );

        float transformSpeed = Vector3.Distance(
            _previousRightPosition,
            rightController.position
        ) / dt;

        Vector3 ovrVelocity = OVRInput.GetLocalControllerVelocity(
            OVRInput.Controller.RTouch
        );

        float speed = ovrVelocity.sqrMagnitude > 0.000001f
            ? ovrVelocity.magnitude
            : transformSpeed;

        _rightRotationStats.Add(rotationFromStart);
        _rightSpeedStats.Add(speed);
        _rightActualRotationPath += Quaternion.Angle(
            _previousRightRotation,
            rightController.rotation
        );

        _previousRightPosition = rightController.position;
        _previousRightRotation = rightController.rotation;
    }

    private void SampleVisualOffsetsAndCdRatio()
    {
        if (leftController != null && leftVirtualHand != null)
        {
            _leftVisualOffsetStats.Add(Vector3.Distance(
                leftController.position,
                leftVirtualHand.position
            ));

            _leftVirtualRotationPath += Quaternion.Angle(
                _previousLeftVirtualRotation,
                leftVirtualHand.rotation
            );

            _previousLeftVirtualRotation = leftVirtualHand.rotation;
        }

        if (rightController != null && rightVirtualHand != null)
        {
            _rightVisualOffsetStats.Add(Vector3.Distance(
                rightController.position,
                rightVirtualHand.position
            ));

            _rightVirtualRotationPath += Quaternion.Angle(
                _previousRightVirtualRotation,
                rightVirtualHand.rotation
            );

            _previousRightVirtualRotation = rightVirtualHand.rotation;
        }
    }

    private void SampleHaptics()
    {
        float leftAmplitude = hapticSource != null
            ? hapticSource.LeftHapticAmplitude
            : 0f;

        float rightAmplitude = hapticSource != null
            ? hapticSource.RightHapticAmplitude
            : 0f;

        _leftHapticStats.Add(leftAmplitude);
        _rightHapticStats.Add(rightAmplitude);
    }

    private void SampleCenterOfMass()
    {
        if (cartPivot == null || cargoObject == null)
        {
            return;
        }

        float localX = cartPivot.InverseTransformPoint(
            cargoObject.position
        ).x;

        _weightXStats.AddSigned(localX);

        if (localX < -centerDeadZone)
        {
            _leftBiasSamples++;
        }
        else if (localX > centerDeadZone)
        {
            _rightBiasSamples++;
        }
        else
        {
            _centerBiasSamples++;
        }
    }

    private float GetLeftCdRatio()
    {
        return SafeRatio(
            _leftActualRotationPath,
            _leftVirtualRotationPath
        );
    }

    private float GetRightCdRatio()
    {
        return SafeRatio(
            _rightActualRotationPath,
            _rightVirtualRotationPath
        );
    }

    private static float SafeRatio(double numerator, double denominator)
    {
        return denominator > 0.0001d
            ? (float)(numerator / denominator)
            : 0f;
    }

    private string GetDominantBiasDirection()
    {
        if (_leftBiasSamples > _rightBiasSamples &&
            _leftBiasSamples > _centerBiasSamples)
        {
            return "왼쪽";
        }

        if (_rightBiasSamples > _leftBiasSamples &&
            _rightBiasSamples > _centerBiasSamples)
        {
            return "오른쪽";
        }

        if (_centerBiasSamples > _leftBiasSamples &&
            _centerBiasSamples > _rightBiasSamples)
        {
            return "중앙";
        }

        return "혼합";
    }

    private string GetFinalBiasDirection()
    {
        if (cartPivot == null || cargoObject == null)
        {
            return "연결안됨";
        }

        float localX = cartPivot.InverseTransformPoint(
            cargoObject.position
        ).x;

        if (localX < -centerDeadZone)
        {
            return "왼쪽";
        }

        if (localX > centerDeadZone)
        {
            return "오른쪽";
        }

        return "중앙";
    }

    private void WriteTrialSummary(float duration)
    {
        StringBuilder row = new StringBuilder(2048);

        AddCsv(row, DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture
        ));

        AddCsv(row, participantId);
        AddCsv(row, "Task2");
        AddCsv(row, conditionId);
        AddCsv(row, modelName);
        AddCsv(row, GetCdModelScriptName());
        AddCsv(row, GetHapticModelScriptName());
        AddCsv(row, trialIndex);
        AddCsv(row, bottleCount);
        AddCsv(row, duration);
        AddCsv(row, GetSampleCount());

        AddStatistics(row, _leftRotationStats);
        AddStatistics(row, _leftSpeedStats);
        AddStatistics(row, _rightRotationStats);
        AddStatistics(row, _rightSpeedStats);

        AddCsv(row, GetLeftCdRatio());
        AddCsv(row, GetRightCdRatio());
        AddCsv(row, leftPosFollowLag);
        AddCsv(row, rightPosFollowLag);
        AddCsv(row, yawFollowLag);

        AddCsv(row, _leftVisualOffsetStats.Mean);
        AddCsv(row, _leftVisualOffsetStats.Maximum);
        AddCsv(row, _rightVisualOffsetStats.Mean);
        AddCsv(row, _rightVisualOffsetStats.Maximum);

        AddCsv(row, _leftHapticStats.Mean);
        AddCsv(row, _leftHapticStats.Maximum);
        AddCsv(row, _rightHapticStats.Mean);
        AddCsv(row, _rightHapticStats.Maximum);
        AddCsv(row, hapticSource != null
            ? hapticSource.HapticFrequency
            : 0f);

        AddCsv(row, _weightXStats.Mean);
        AddCsv(row, _weightXStats.Minimum);
        AddCsv(row, _weightXStats.Maximum);
        AddCsv(row, GetDominantBiasDirection());
        AddCsv(row, GetFinalBiasDirection());

        try
        {
            _writer.WriteLine(row.ToString());
            _writer.Flush();

            Debug.Log(
                $"[Task2LogData] 시행 저장 완료 | Trial {trialIndex} | " +
                $"대표 편향: {GetDominantBiasDirection()}\n{_filePath}"
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[Task2LogData] CSV 저장 실패\n" + exception
            );
        }
    }

    private void CreateCsvFile()
    {
        CloseCsvFile();

        try
        {
            string folderPath = Path.Combine(
                Application.persistentDataPath,
                "Task2Logs"
            );

            Directory.CreateDirectory(folderPath);

            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}_Task2_{1}_{2}.csv",
                SanitizeFileName(participantId),
                SanitizeFileName(conditionId),
                DateTime.Now.ToString("yyyyMMdd_HHmmss")
            );

            _filePath = Path.Combine(folderPath, fileName);
            _writer = new StreamWriter(
                _filePath,
                false,
                new UTF8Encoding(true)
            );

            WriteCsvHeader();

            Debug.Log(
                "[Task2LogData] CSV 파일 생성 완료\n" + _filePath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[Task2LogData] CSV 파일 생성 실패\n" + exception
            );
            CloseCsvFile();
        }
    }

    private void WriteCsvHeader()
    {
        _writer.WriteLine(
            "기록시간," +
            "참가자ID," +
            "Task," +
            "피드백조건," +
            "적용모델명," +
            "C/D모델스크립트," +
            "진동모델스크립트," +
            "시행번호," +
            "적재개수," +
            "양손조작시간(초)," +
            "측정샘플수," +

            "왼손_평균회전값(도)," +
            "왼손_최소회전값(도)," +
            "왼손_최대회전값(도)," +
            "왼손_평균속도(m/s)," +
            "왼손_최소속도(m/s)," +
            "왼손_최대속도(m/s)," +

            "오른손_평균회전값(도)," +
            "오른손_최소회전값(도)," +
            "오른손_최대회전값(도)," +
            "오른손_평균속도(m/s)," +
            "오른손_최소속도(m/s)," +
            "오른손_최대속도(m/s)," +

            "왼손_C/D_Ratio(실제누적회전/가상누적회전)," +
            "오른손_C/D_Ratio(실제누적회전/가상누적회전)," +
            "왼손_PosFollowLag," +
            "오른손_PosFollowLag," +
            "YawFollowLag," +

            "왼손_실제-가상손_평균거리차이(m)," +
            "왼손_실제-가상손_최대거리차이(m)," +
            "오른손_실제-가상손_평균거리차이(m)," +
            "오른손_실제-가상손_최대거리차이(m)," +

            "왼쪽컨트롤러_평균진동값," +
            "왼쪽컨트롤러_최대진동값," +
            "오른쪽컨트롤러_평균진동값," +
            "오른쪽컨트롤러_최대진동값," +
            "진동주파수," +

            "무게중심_평균X위치(m)," +
            "무게중심_최소X위치(m)," +
            "무게중심_최대X위치(m)," +
            "무게중심_대표편향방향," +
            "무게중심_종료시편향방향"
        );

        _writer.Flush();
    }

    private string GetCdModelScriptName()
    {
        return cdTransformer != null
            ? cdTransformer.GetType().Name
            : "연결안됨";
    }

    private string GetHapticModelScriptName()
    {
        return hapticSource != null
            ? hapticSource.GetType().Name
            : "연결안됨";
    }

    private int GetSampleCount()
    {
        return Mathf.Max(
            _leftRotationStats.Count,
            _rightRotationStats.Count
        );
    }

    private void ResetTrialData()
    {
        _leftRotationStats.Reset();
        _rightRotationStats.Reset();
        _leftSpeedStats.Reset();
        _rightSpeedStats.Reset();
        _leftVisualOffsetStats.Reset();
        _rightVisualOffsetStats.Reset();
        _leftHapticStats.Reset();
        _rightHapticStats.Reset();
        _weightXStats.Reset();

        _leftActualRotationPath = 0d;
        _rightActualRotationPath = 0d;
        _leftVirtualRotationPath = 0d;
        _rightVirtualRotationPath = 0d;

        _leftBiasSamples = 0;
        _rightBiasSamples = 0;
        _centerBiasSamples = 0;
    }

    private static void AddStatistics(
        StringBuilder row,
        ScalarStatistics statistics)
    {
        AddCsv(row, statistics.Mean);
        AddCsv(row, statistics.Minimum);
        AddCsv(row, statistics.Maximum);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace(' ', '_');
    }

    private static void AddCsv(StringBuilder row, string value)
    {
        if (row.Length > 0)
        {
            row.Append(',');
        }

        row.Append(EscapeCsv(value));
    }

    private static void AddCsv(StringBuilder row, int value)
    {
        AddCsv(row, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddCsv(StringBuilder row, float value)
    {
        AddCsv(row, value.ToString(
            "F6",
            CultureInfo.InvariantCulture
        ));
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes = value.Contains(",") ||
                           value.Contains("\"") ||
                           value.Contains("\n") ||
                           value.Contains("\r");

        return needsQuotes
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private void CloseCsvFile()
    {
        if (_writer == null)
        {
            return;
        }

        try
        {
            _writer.Flush();
            _writer.Close();
            _writer.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[Task2LogData] CSV 종료 오류\n" + exception
            );
        }
        finally
        {
            _writer = null;
        }
    }

    private void OnDisable()
    {
        if (_trialActive)
        {
            EndTrial();
        }

        CloseCsvFile();
    }

    private void OnApplicationQuit()
    {
        if (_trialActive)
        {
            EndTrial();
        }

        CloseCsvFile();
    }

    [Serializable]
    private struct ScalarStatistics
    {
        private double _sum;
        private float _minimum;
        private float _maximum;
        private int _count;

        public int Count => _count;
        public float Mean => _count > 0
            ? (float)(_sum / _count)
            : 0f;
        public float Minimum => _count > 0 ? _minimum : 0f;
        public float Maximum => _count > 0 ? _maximum : 0f;

        public void Add(float value)
        {
            AddInternal(Mathf.Abs(value));
        }

        public void AddSigned(float value)
        {
            AddInternal(value);
        }

        private void AddInternal(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }

            if (_count == 0)
            {
                _minimum = value;
                _maximum = value;
            }
            else
            {
                _minimum = Mathf.Min(_minimum, value);
                _maximum = Mathf.Max(_maximum, value);
            }

            _sum += value;
            _count++;
        }

        public void Reset()
        {
            _sum = 0d;
            _minimum = 0f;
            _maximum = 0f;
            _count = 0;
        }
    }
}