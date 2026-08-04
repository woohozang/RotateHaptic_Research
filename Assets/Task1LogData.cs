using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Task 1 전용 요약 로그.
///
/// 기록 방식:
/// 1. 양손으로 카트를 잡으면 시행 시작
/// 2. 설정된 주기로 데이터를 내부 누적
/// 3. 한 손이라도 놓으면 시행 종료
/// 4. 한 시행당 CSV에 한 행만 저장
///
/// Task 1은 무게중심이 동적으로 이동하지 않으므로
/// 무게중심 위치는 DynamicWeightTwoGrabPlaneTransformer의
/// CurrentTargetSide를 기준으로 기록합니다.
/// </summary>
public class Task1LogData : MonoBehaviour
{
    [Header("실험 정보")]
    [Tooltip("참가자 ID")]
    public string participantId = "P001";

    [Tooltip("피드백 조건: Baseline, Visual, Haptic, Combined")]
    public string conditionId = "Visual";

    [Tooltip("현재 시행 번호")]
    public int trialIndex = 1;

    [Tooltip("시행 종료 후 시행 번호를 자동으로 증가시킵니다.")]
    public bool autoIncreaseTrialIndex = true;

    [Header("모델 정보")]
    [Tooltip("CSV에 표시할 모델 이름. 비워두면 Model Object 이름을 사용합니다.")]
    public string modelName = "";

    [Tooltip("현재 실험에 사용되는 카트 모델 또는 프리팹의 루트 오브젝트")]
    public GameObject modelObject;

    [Header("양손 잡기 판정")]
    [Tooltip("BothHandle에 적용된 Grabbable")]
    public Grabbable grabbable;

    [Header("실제 컨트롤러")]
    [Tooltip("OVRCameraRig/TrackingSpace/LeftControllerAnchor")]
    public Transform leftController;

    [Tooltip("OVRCameraRig/TrackingSpace/RightControllerAnchor")]
    public Transform rightController;

    [Header("가상 손")]
    [Tooltip("화면에 표시되는 왼쪽 가상 손")]
    public Transform leftVirtualHand;

    [Tooltip("화면에 표시되는 오른쪽 가상 손")]
    public Transform rightVirtualHand;

    [Header("Task 1 C/D 모델")]
    [Tooltip("DynamicWeightTwoGrabPlaneTransformer")]
    public DynamicWeightTwoGrabPlaneTransformer cdTransformer;

    [Tooltip("지연되지 않는 손에 대응하는 즉시 추적 Follow 값")]
    public float instantFollowValue = 40f;

    [Tooltip(
        "지연 대상 손의 오프셋을 Transformer 내부 지연 거리로 측정합니다."
    )]
    public bool useTransformerLagDistance = true;

    [Header("Task 1 진동 모델")]
    [Tooltip("SingleVibrationWeightHapticController")]
    public SingleVibrationWeightHapticController hapticSource;

    [Header("측정 설정")]
    [Tooltip(
        "내부 데이터 측정 빈도입니다. " +
        "CSV에는 프레임별 데이터가 아닌 시행 요약 한 행만 저장됩니다."
    )]
    [Range(1f, 120f)]
    public float sampleRateHz = 30f;

    [Tooltip("이 시간보다 짧은 양손 잡기는 시행으로 저장하지 않습니다.")]
    [Min(0f)]
    public float minimumTrialDuration = 0.3f;

    [Tooltip("씬 실행 시 CSV 파일을 자동 생성합니다.")]
    public bool createFileOnStart = true;

    private StreamWriter _writer;
    private string _filePath;

    private bool _trialActive;
    private float _trialStartTime;
    private float _lastSampleTime;

    private Vector3 _previousLeftControllerPosition;
    private Vector3 _previousRightControllerPosition;

    private Quaternion _leftStartRotation;
    private Quaternion _rightStartRotation;

    // 좌우 컨트롤러 회전값
    private ScalarStatistics _leftRotationStats;
    private ScalarStatistics _rightRotationStats;

    // 좌우 컨트롤러 속도
    private ScalarStatistics _leftSpeedStats;
    private ScalarStatistics _rightSpeedStats;

    // 실제 컨트롤러와 가상 손 사이 거리
    private ScalarStatistics _leftVisualOffsetStats;
    private ScalarStatistics _rightVisualOffsetStats;

    // 좌우 진동 진폭
    private ScalarStatistics _leftHapticStats;
    private ScalarStatistics _rightHapticStats;

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
        if (grabbable == null)
        {
            return;
        }

        bool isTwoHandGrabbed =
            grabbable.SelectingPointsCount >= 2;

        // 양손 잡기 시작
        if (isTwoHandGrabbed && !_trialActive)
        {
            BeginTrial();
        }
        // 한 손 이상 놓음
        else if (!isTwoHandGrabbed && _trialActive)
        {
            EndTrial();
        }

        if (!_trialActive)
        {
            return;
        }

        float sampleInterval =
            1f / Mathf.Max(1f, sampleRateHz);

        if (Time.unscaledTime - _lastSampleTime <
            sampleInterval)
        {
            return;
        }

        SampleData();
    }

    /// <summary>
    /// 양손으로 잡았을 때 시행 측정을 시작합니다.
    /// </summary>
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

        ResetStatistics();

        _trialActive = true;
        _trialStartTime = Time.unscaledTime;
        _lastSampleTime = Time.unscaledTime;

        if (leftController != null)
        {
            _previousLeftControllerPosition =
                leftController.position;

            _leftStartRotation =
                leftController.rotation;
        }

        if (rightController != null)
        {
            _previousRightControllerPosition =
                rightController.position;

            _rightStartRotation =
                rightController.rotation;
        }

        Debug.Log(
            $"[Task1LogData] 시행 시작 | " +
            $"{participantId} | {conditionId} | " +
            $"Trial {trialIndex} | " +
            $"편향 {GetCurrentTargetSideKorean()}"
        );
    }

    /// <summary>
    /// 한 손 이상 놓았을 때 시행을 종료하고
    /// CSV에 요약 한 행을 저장합니다.
    /// </summary>
    public void EndTrial()
    {
        if (!_trialActive)
        {
            return;
        }

        float trialDuration =
            Time.unscaledTime - _trialStartTime;

        _trialActive = false;

        if (trialDuration < minimumTrialDuration)
        {
            Debug.LogWarning(
                $"[Task1LogData] 시행 시간이 너무 짧아 " +
                $"저장하지 않습니다: {trialDuration:F3}초"
            );

            return;
        }

        WriteTrialSummary(trialDuration);

        if (autoIncreaseTrialIndex)
        {
            trialIndex++;
        }
    }

    private void SampleData()
    {
        float currentTime = Time.unscaledTime;

        float dt = Mathf.Max(
            0.0001f,
            currentTime - _lastSampleTime
        );

        _lastSampleTime = currentTime;

        // =====================================================
        // 1. 좌우 컨트롤러 회전값 및 이동 속도
        // =====================================================

        if (leftController != null)
        {
            // 양손으로 잡은 시점 기준 회전 변화량
            float leftRotationValue =
                Quaternion.Angle(
                    _leftStartRotation,
                    leftController.rotation
                );

            float leftSpeed =
                Vector3.Distance(
                    _previousLeftControllerPosition,
                    leftController.position
                ) / dt;

            _leftRotationStats.Add(leftRotationValue);
            _leftSpeedStats.Add(leftSpeed);

            _previousLeftControllerPosition =
                leftController.position;
        }

        if (rightController != null)
        {
            float rightRotationValue =
                Quaternion.Angle(
                    _rightStartRotation,
                    rightController.rotation
                );

            float rightSpeed =
                Vector3.Distance(
                    _previousRightControllerPosition,
                    rightController.position
                ) / dt;

            _rightRotationStats.Add(rightRotationValue);
            _rightSpeedStats.Add(rightSpeed);

            _previousRightControllerPosition =
                rightController.position;
        }

        // =====================================================
        // 2. 실제 컨트롤러-가상 손 시각적 거리 차이
        // =====================================================

        float leftVisualOffset =
            CalculateLeftVisualOffset();

        float rightVisualOffset =
            CalculateRightVisualOffset();

        _leftVisualOffsetStats.Add(leftVisualOffset);
        _rightVisualOffsetStats.Add(rightVisualOffset);

        // =====================================================
        // 3. 좌우 진동 진폭
        // =====================================================

        float leftHapticAmplitude =
            hapticSource != null
                ? hapticSource.LeftHapticAmplitude
                : 0f;

        float rightHapticAmplitude =
            hapticSource != null
                ? hapticSource.RightHapticAmplitude
                : 0f;

        _leftHapticStats.Add(leftHapticAmplitude);
        _rightHapticStats.Add(rightHapticAmplitude);
    }

    private float CalculateLeftVisualOffset()
    {
        // Transformer에서 왼손이 지연 대상이면
        // 실제 손과 내부 지연 위치 사이 거리를 사용
        if (cdTransformer != null &&
            useTransformerLagDistance &&
            cdTransformer.CurrentTargetSide ==
            DynamicWeightTwoGrabPlaneTransformer
                .TargetLagSide.Left)
        {
            return Mathf.Abs(
                cdTransformer.GetCurrentLagDistance()
            );
        }

        // 지연 대상이 아니면 실제 손과 가상 손 Transform 거리 사용
        if (leftController != null &&
            leftVirtualHand != null)
        {
            return Vector3.Distance(
                leftController.position,
                leftVirtualHand.position
            );
        }

        return 0f;
    }

    private float CalculateRightVisualOffset()
    {
        if (cdTransformer != null &&
            useTransformerLagDistance &&
            cdTransformer.CurrentTargetSide ==
            DynamicWeightTwoGrabPlaneTransformer
                .TargetLagSide.Right)
        {
            return Mathf.Abs(
                cdTransformer.GetCurrentLagDistance()
            );
        }

        if (rightController != null &&
            rightVirtualHand != null)
        {
            return Vector3.Distance(
                rightController.position,
                rightVirtualHand.position
            );
        }

        return 0f;
    }

    private void WriteTrialSummary(float trialDuration)
    {
        if (_writer == null)
        {
            Debug.LogError(
                "[Task1LogData] CSV Writer가 없습니다."
            );

            return;
        }

        string currentTargetSide =
            GetCurrentTargetSideKorean();

        int currentBottleCount =
            GetCurrentBottleCount();

        float appliedPosFollow =
            cdTransformer != null
                ? cdTransformer.LeftPosFollow
                : 0f;

        float leftPosFollow = instantFollowValue;
        float rightPosFollow = instantFollowValue;

        if (cdTransformer != null)
        {
            if (cdTransformer.CurrentTargetSide ==
                DynamicWeightTwoGrabPlaneTransformer
                    .TargetLagSide.Left)
            {
                leftPosFollow =
                    cdTransformer.LeftPosFollow;
            }
            else
            {
                rightPosFollow =
                    cdTransformer.LeftPosFollow;
            }
        }

        StringBuilder row =
            new StringBuilder(2048);

        // 기본 실험 정보
        AddCsv(
            row,
            DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture
            )
        );

        AddCsv(row, participantId);
        AddCsv(row, "Task1");
        AddCsv(row, conditionId);

        // 사용 모델 정보
        AddCsv(row, GetAppliedModelName());
        AddCsv(row, GetCdModelScriptName());
        AddCsv(row, GetHapticModelScriptName());

        AddCsv(row, trialIndex);
        AddCsv(row, currentBottleCount);

        // Task 1 무게중심은 동적 위치가 아니라
        // CurrentTargetSide 설정값으로 기록
        AddCsv(row, currentTargetSide);

        AddCsv(row, trialDuration);
        AddCsv(row, GetSampleCount());

        // 왼손 컨트롤러
        AddCsv(row, _leftRotationStats.Mean);
        AddCsv(row, _leftRotationStats.Minimum);
        AddCsv(row, _leftRotationStats.Maximum);

        AddCsv(row, _leftSpeedStats.Mean);
        AddCsv(row, _leftSpeedStats.Minimum);
        AddCsv(row, _leftSpeedStats.Maximum);

        // 오른손 컨트롤러
        AddCsv(row, _rightRotationStats.Mean);
        AddCsv(row, _rightRotationStats.Minimum);
        AddCsv(row, _rightRotationStats.Maximum);

        AddCsv(row, _rightSpeedStats.Mean);
        AddCsv(row, _rightSpeedStats.Minimum);
        AddCsv(row, _rightSpeedStats.Maximum);

        // C/D 모델과 시각적 오프셋
        AddCsv(row, currentTargetSide);
        AddCsv(row, appliedPosFollow);
        AddCsv(row, leftPosFollow);
        AddCsv(row, rightPosFollow);

        AddCsv(row, _leftVisualOffsetStats.Mean);
        AddCsv(row, _leftVisualOffsetStats.Maximum);

        AddCsv(row, _rightVisualOffsetStats.Mean);
        AddCsv(row, _rightVisualOffsetStats.Maximum);

        // 진동 피드백
        AddCsv(row, _leftHapticStats.Mean);
        AddCsv(row, _leftHapticStats.Maximum);

        AddCsv(row, _rightHapticStats.Mean);
        AddCsv(row, _rightHapticStats.Maximum);

        // 현재 진동 주파수
        float frequency =
            hapticSource != null
                ? hapticSource.HapticFrequency
                : 0f;

        AddCsvLast(row, frequency);

        try
        {
            _writer.WriteLine(row.ToString());
            _writer.Flush();

            Debug.Log(
                $"[Task1LogData] 시행 저장 완료 | " +
                $"Trial {trialIndex} | " +
                $"적재 {currentBottleCount}개 | " +
                $"편향 {currentTargetSide}\n" +
                _filePath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[Task1LogData] CSV 저장 실패\n" +
                exception
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
                "Task1Logs"
            );

            Directory.CreateDirectory(folderPath);

            string participant =
                SanitizeFileName(participantId);

            string condition =
                SanitizeFileName(conditionId);

            string date =
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture
                );

            string fileName =
                $"{participant}_Task1_{condition}_{date}.csv";

            _filePath =
                Path.Combine(folderPath, fileName);

            _writer = new StreamWriter(
                _filePath,
                false,
                new UTF8Encoding(true)
            );

            WriteCsvHeader();

            Debug.Log(
                "[Task1LogData] CSV 파일 생성 완료\n" +
                _filePath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[Task1LogData] CSV 파일 생성 실패\n" +
                exception
            );

            CloseCsvFile();
        }
    }

    private void WriteCsvHeader()
    {
        if (_writer == null)
        {
            return;
        }

        _writer.WriteLine(
            "기록시간," +
            "참가자ID," +
            "Task," +
            "피드백조건," +

            "적용모델이름," +
            "C/D모델스크립트," +
            "진동모델스크립트," +

            "시행번호," +
            "적재개수," +
            "무게중심위치(CurrentTargetSide)," +
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

            "지연대상손(CurrentTargetSide)," +
            "적용_PosFollowLag," +
            "왼손_PosFollow," +
            "오른손_PosFollow," +

            "왼손_실제-가상손_평균거리차이(m)," +
            "왼손_실제-가상손_최대거리차이(m)," +
            "오른손_실제-가상손_평균거리차이(m)," +
            "오른손_실제-가상손_최대거리차이(m)," +

            "왼손_평균진동값," +
            "왼손_최대진동값," +
            "오른손_평균진동값," +
            "오른손_최대진동값," +
            "진동주파수"
        );

        _writer.Flush();
    }

    private void ResetStatistics()
    {
        _leftRotationStats.Reset();
        _rightRotationStats.Reset();

        _leftSpeedStats.Reset();
        _rightSpeedStats.Reset();

        _leftVisualOffsetStats.Reset();
        _rightVisualOffsetStats.Reset();

        _leftHapticStats.Reset();
        _rightHapticStats.Reset();
    }

    /// <summary>
    /// Task 1의 무게중심 위치를 CurrentTargetSide에서 가져옵니다.
    /// </summary>
    private string GetCurrentTargetSideKorean()
    {
        if (cdTransformer == null)
        {
            return "연결안됨";
        }

        return cdTransformer.CurrentTargetSide ==
               DynamicWeightTwoGrabPlaneTransformer
                   .TargetLagSide.Left
            ? "왼쪽"
            : "오른쪽";
    }

    /// <summary>
    /// 적재 개수는 진동 모델의 BottleCount 설정에서 가져옵니다.
    /// </summary>
    private int GetCurrentBottleCount()
    {
        if (hapticSource == null)
        {
            return 0;
        }

        return hapticSource.ConfiguredBottleCount;
    }

    private string GetAppliedModelName()
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            return modelName;
        }

        if (modelObject != null)
        {
            return modelObject.name;
        }

        if (cdTransformer != null)
        {
            return cdTransformer.gameObject.name;
        }

        return "모델이름없음";
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

    private static string SanitizeFileName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        foreach (
            char invalidCharacter
            in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(
                invalidCharacter,
                '_'
            );
        }

        return value.Replace(' ', '_');
    }

    private static void AddCsv(
        StringBuilder builder,
        string value)
    {
        if (builder.Length > 0)
        {
            builder.Append(',');
        }

        builder.Append(EscapeCsv(value));
    }

    private static void AddCsv(
        StringBuilder builder,
        int value)
    {
        if (builder.Length > 0)
        {
            builder.Append(',');
        }

        builder.Append(
            value.ToString(
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void AddCsv(
        StringBuilder builder,
        float value)
    {
        if (builder.Length > 0)
        {
            builder.Append(',');
        }

        builder.Append(
            value.ToString(
                "F6",
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void AddCsvLast(
        StringBuilder builder,
        float value)
    {
        AddCsv(builder, value);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n");

        if (!needsQuotes)
        {
            return value;
        }

        return "\"" +
               value.Replace("\"", "\"\"") +
               "\"";
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
                "[Task1LogData] CSV 종료 오류\n" +
                exception
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

    /// <summary>
    /// 평균, 최소, 최대 통계 누적 구조체.
    /// 모든 입력값은 절댓값으로 누적합니다.
    /// </summary>
    [Serializable]
    private struct ScalarStatistics
    {
        private double _sum;
        private float _minimum;
        private float _maximum;
        private int _count;

        public int Count => _count;

        public float Mean =>
            _count > 0
                ? (float)(_sum / _count)
                : 0f;

        public float Minimum =>
            _count > 0
                ? _minimum
                : 0f;

        public float Maximum =>
            _count > 0
                ? _maximum
                : 0f;

        public void Add(float value)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return;
            }

            float absoluteValue =
                Mathf.Abs(value);

            if (_count == 0)
            {
                _minimum = absoluteValue;
                _maximum = absoluteValue;
            }
            else
            {
                _minimum = Mathf.Min(
                    _minimum,
                    absoluteValue
                );

                _maximum = Mathf.Max(
                    _maximum,
                    absoluteValue
                );
            }

            _sum += absoluteValue;
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