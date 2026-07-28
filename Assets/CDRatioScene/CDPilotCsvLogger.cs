using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// 양손 C/D Ratio 파일럿 실험의 Trial별 결과를 CSV로 저장합니다.
///
/// 최종 기록 항목:
/// - ParticipantId
/// - TrialIndex
/// - ConditionId
/// - RecordedAt
/// - LeftCD
/// - RightCD
/// - DeltaCD
/// - LeftHandControllerOffsetM
/// - RightHandControllerOffsetM
///
/// HandControllerOffsetM은 단순한 월드 위치 차이가 아니라,
/// Trial 시작 위치를 보정한 실제 컨트롤러와 가상 손의
/// 상대 Z축 이동량 차이 중 최댓값입니다.
///
/// 예:
/// 실제 컨트롤러 상대 이동량 = -0.10m
/// 가상 손 상대 이동량       = -0.06m
/// 오프셋                    =  0.04m
/// </summary>
[DefaultExecutionOrder(15000)]
public class CDPilotCsvLogger : MonoBehaviour
{
    [Header("실험 정보")]

    [Tooltip("참가자 식별 번호입니다. 예: P01")]
    [SerializeField]
    private string participantId = "P01";

    [Tooltip("현재 조건 이름입니다. 예: C0, C1, C2")]
    [SerializeField]
    private string conditionId = "C0";

    [Tooltip("CSV에 기록할 첫 Trial 번호입니다.")]
    [Min(1)]
    [SerializeField]
    private int trialIndex = 1;

    [Header("C/D Transformer")]

    [Tooltip("왼쪽 손잡이의 OneGrabTranslateCDTransformer")]
    [SerializeField]
    private OneGrabTranslateCDTransformer leftCDTransformer;

    [Tooltip("오른쪽 손잡이의 OneGrabTranslateCDTransformer")]
    [SerializeField]
    private OneGrabTranslateCDTransformer rightCDTransformer;

    [Header("실제 컨트롤러 Transform")]

    [Tooltip(
        "OVRCameraRig의 LeftControllerAnchor를 연결하세요."
    )]
    [SerializeField]
    private Transform leftController;

    [Tooltip(
        "OVRCameraRig의 RightControllerAnchor를 연결하세요."
    )]
    [SerializeField]
    private Transform rightController;

    [Header("가상 손 Visual Transform")]

    [Tooltip(
        "참가자에게 보이는 왼쪽 가상 손의 손목 또는 손바닥 기준점입니다. " +
        "C/D 또는 Visual Damping이 실제로 반영되는 Transform을 연결하세요."
    )]
    [SerializeField]
    private Transform leftVirtualHand;

    [Tooltip(
        "참가자에게 보이는 오른쪽 가상 손의 손목 또는 손바닥 기준점입니다. " +
        "C/D 또는 Visual Damping이 실제로 반영되는 Transform을 연결하세요."
    )]
    [SerializeField]
    private Transform rightVirtualHand;

    [Header("측정 좌표계")]

    [Tooltip(
        "이 Transform의 Local Z축을 기준으로 이동량을 측정합니다. " +
        "스프링 장치 전체의 공통 Root를 연결하세요. " +
        "비워 두면 World Z축을 기준으로 측정합니다."
    )]
    [SerializeField]
    private Transform measurementSpace;

    [Header("오프셋 측정 설정")]

    [Tooltip(
        "활성화하면 -Z 방향으로 당기는 압축 상태에서만 " +
        "가상 손-컨트롤러 오프셋을 측정합니다."
    )]
    [SerializeField]
    private bool measureOnlyDuringCompression = true;

    [Tooltip(
        "이 값보다 작은 오프셋은 추적 노이즈로 간주하여 0으로 처리합니다. " +
        "0.001은 1mm입니다."
    )]
    [Min(0f)]
    [SerializeField]
    private float offsetNoiseThreshold = 0.001f;

    [Header("CSV 설정")]

    [Tooltip(
        "Application.persistentDataPath 아래에 생성할 폴더 이름"
    )]
    [SerializeField]
    private string folderName = "CDPilotLogs";

    [Tooltip("CSV 파일 이름 앞부분")]
    [SerializeField]
    private string fileNamePrefix = "CDPilotLog";

    [Header("에디터 테스트")]

    [Tooltip(
        "활성화하면 B키로 Trial을 시작하고 " +
        "E키로 Trial을 종료하여 저장합니다."
    )]
    [SerializeField]
    private bool enableKeyboardTest;

    [Header("디버그")]

    [Tooltip("측정 중 현재 오프셋을 Console에 출력합니다.")]
    [SerializeField]
    private bool showRealtimeLog;

    [Tooltip("실시간 디버그 출력 간격")]
    [Min(0.05f)]
    [SerializeField]
    private float realtimeLogInterval = 0.25f;

    private bool _isRecording;

    /*
     * Trial 시작 시점의 실제 컨트롤러 Z 위치
     */
    private float _leftControllerStartZ;
    private float _rightControllerStartZ;

    /*
     * Trial 시작 시점의 가상 손 Z 위치
     */
    private float _leftVirtualHandStartZ;
    private float _rightVirtualHandStartZ;

    /*
     * Trial 중 측정한 최대 상대 이동 오프셋
     */
    private float _leftMaxHandControllerOffset;
    private float _rightMaxHandControllerOffset;

    private float _currentLeftOffset;
    private float _currentRightOffset;

    private float _nextRealtimeLogTime;

    private string _csvFilePath;

    /// <summary>
    /// 현재 Trial 기록 여부입니다.
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// Trial 중 측정된 왼쪽 컨트롤러와
    /// 왼쪽 가상 손 사이의 최대 상대 Z 이동 오프셋입니다.
    /// 단위는 meter입니다.
    /// </summary>
    public float LeftHandControllerOffsetM =>
        _leftMaxHandControllerOffset;

    /// <summary>
    /// Trial 중 측정된 오른쪽 컨트롤러와
    /// 오른쪽 가상 손 사이의 최대 상대 Z 이동 오프셋입니다.
    /// 단위는 meter입니다.
    /// </summary>
    public float RightHandControllerOffsetM =>
        _rightMaxHandControllerOffset;

    /// <summary>
    /// 현재 생성된 CSV 파일 경로입니다.
    /// </summary>
    public string CsvFilePath => _csvFilePath;

    private void Start()
    {
        Debug.Log(
            "[CDPilotCsvLogger] Persistent Data Path:\n" +
            Application.persistentDataPath,
            this
        );
    }

    private void Update()
    {
        if (!enableKeyboardTest)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            BeginTrial();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            EndTrialAndSave();
        }
    }

    /*
     * 컨트롤러와 가상 손 Visual 위치가 모두 갱신된 뒤
     * 측정하기 위해 LateUpdate를 사용합니다.
     */
    private void LateUpdate()
    {
        if (!_isRecording)
        {
            return;
        }

        SampleHandControllerOffsets();
        PrintRealtimeDebugLog();
    }

    /// <summary>
    /// 새로운 Trial의 측정을 시작합니다.
    /// </summary>
    [ContextMenu("Trial 측정 시작")]
    public void BeginTrial()
    {
        if (_isRecording)
        {
            Debug.LogWarning(
                "[CDPilotCsvLogger] 이미 Trial을 측정 중입니다.",
                this
            );

            return;
        }

        if (!ValidateReferences())
        {
            return;
        }

        if (!EnsureCsvFileCreated())
        {
            return;
        }

        /*
         * 실제 컨트롤러의 Trial 시작 위치
         */
        _leftControllerStartZ =
            GetMeasurementSpaceZ(leftController);

        _rightControllerStartZ =
            GetMeasurementSpaceZ(rightController);

        /*
         * 가상 손 Visual의 Trial 시작 위치
         */
        _leftVirtualHandStartZ =
            GetMeasurementSpaceZ(leftVirtualHand);

        _rightVirtualHandStartZ =
            GetMeasurementSpaceZ(rightVirtualHand);

        /*
         * 이전 Trial의 측정값 초기화
         */
        _leftMaxHandControllerOffset = 0f;
        _rightMaxHandControllerOffset = 0f;

        _currentLeftOffset = 0f;
        _currentRightOffset = 0f;

        _nextRealtimeLogTime =
            Time.unscaledTime;

        _isRecording = true;

        Debug.Log(
            $"[CDPilotCsvLogger] Trial 시작\n" +
            $"Participant: {participantId}\n" +
            $"Trial: {trialIndex}\n" +
            $"Condition: {conditionId}\n" +
            $"Left CD: {leftCDTransformer.CDRatio:F2}\n" +
            $"Right CD: {rightCDTransformer.CDRatio:F2}",
            this
        );
    }

    /// <summary>
    /// 현재 Trial을 종료하고 CSV에 한 행을 저장합니다.
    /// </summary>
    [ContextMenu("Trial 측정 종료 및 저장")]
    public void EndTrialAndSave()
    {
        if (!_isRecording)
        {
            Debug.LogWarning(
                "[CDPilotCsvLogger] 현재 측정 중인 Trial이 없습니다.",
                this
            );

            return;
        }

        /*
         * Trial 종료 직전의 위치도 마지막으로 측정합니다.
         */
        SampleHandControllerOffsets();

        float leftCD =
            leftCDTransformer.CDRatio;

        float rightCD =
            rightCDTransformer.CDRatio;

        float deltaCD =
            Mathf.Abs(leftCD - rightCD);

        string recordedAt =
            DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture
            );

        string csvLine =
            string.Join(
                ",",

                EscapeCsv(participantId),

                trialIndex.ToString(
                    CultureInfo.InvariantCulture
                ),

                EscapeCsv(conditionId),

                EscapeCsv(recordedAt),

                leftCD.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),

                rightCD.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),

                deltaCD.ToString(
                    "F4",
                    CultureInfo.InvariantCulture
                ),

                _leftMaxHandControllerOffset.ToString(
                    "F6",
                    CultureInfo.InvariantCulture
                ),

                _rightMaxHandControllerOffset.ToString(
                    "F6",
                    CultureInfo.InvariantCulture
                )
            );

        try
        {
            File.AppendAllText(
                _csvFilePath,
                csvLine + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[CDPilotCsvLogger] CSV 저장 실패\n" +
                exception.Message,
                this
            );

            return;
        }

        _isRecording = false;

        Debug.Log(
            $"[CDPilotCsvLogger] Trial 저장 완료\n" +
            $"Left Hand-Controller Offset: " +
            $"{_leftMaxHandControllerOffset:F4}m\n" +
            $"Right Hand-Controller Offset: " +
            $"{_rightMaxHandControllerOffset:F4}m\n" +
            $"파일 경로: {_csvFilePath}",
            this
        );

        trialIndex++;
    }

    /// <summary>
    /// 현재 Trial을 저장하지 않고 취소합니다.
    /// </summary>
    [ContextMenu("현재 Trial 취소")]
    public void CancelCurrentTrial()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;

        Debug.Log(
            "[CDPilotCsvLogger] 현재 Trial 측정을 취소했습니다.",
            this
        );
    }

    /// <summary>
    /// 실제 컨트롤러와 가상 손의 상대 Z 이동량을 계산하고,
    /// Trial 중 발생한 최대 오프셋을 저장합니다.
    /// </summary>
    private void SampleHandControllerOffsets()
    {
        float currentLeftControllerZ =
            GetMeasurementSpaceZ(leftController);

        float currentRightControllerZ =
            GetMeasurementSpaceZ(rightController);

        float currentLeftVirtualHandZ =
            GetMeasurementSpaceZ(leftVirtualHand);

        float currentRightVirtualHandZ =
            GetMeasurementSpaceZ(rightVirtualHand);

        /*
         * Trial 시작 위치를 기준으로 한
         * 실제 컨트롤러 상대 이동량
         */
        float leftControllerMovement =
            currentLeftControllerZ -
            _leftControllerStartZ;

        float rightControllerMovement =
            currentRightControllerZ -
            _rightControllerStartZ;

        /*
         * Trial 시작 위치를 기준으로 한
         * 가상 손 Visual 상대 이동량
         */
        float leftVirtualHandMovement =
            currentLeftVirtualHandZ -
            _leftVirtualHandStartZ;

        float rightVirtualHandMovement =
            currentRightVirtualHandZ -
            _rightVirtualHandStartZ;

        /*
         * 초기 위치 차이를 제거한 상대 이동량 오프셋
         */
        _currentLeftOffset =
            Mathf.Abs(
                leftControllerMovement -
                leftVirtualHandMovement
            );

        _currentRightOffset =
            Mathf.Abs(
                rightControllerMovement -
                rightVirtualHandMovement
            );

        /*
         * 작은 추적 노이즈 제거
         */
        if (_currentLeftOffset < offsetNoiseThreshold)
        {
            _currentLeftOffset = 0f;
        }

        if (_currentRightOffset < offsetNoiseThreshold)
        {
            _currentRightOffset = 0f;
        }

        /*
         * 압축 중에만 측정하도록 설정한 경우
         * 각 손의 Transformer 상태를 확인합니다.
         */
        bool measureLeft =
            ShouldMeasureOffset(
                leftCDTransformer
            );

        bool measureRight =
            ShouldMeasureOffset(
                rightCDTransformer
            );

        if (measureLeft)
        {
            _leftMaxHandControllerOffset =
                Mathf.Max(
                    _leftMaxHandControllerOffset,
                    _currentLeftOffset
                );
        }

        if (measureRight)
        {
            _rightMaxHandControllerOffset =
                Mathf.Max(
                    _rightMaxHandControllerOffset,
                    _currentRightOffset
                );
        }
    }

    /// <summary>
    /// 현재 해당 손의 오프셋을 측정할 상태인지 반환합니다.
    /// </summary>
    private bool ShouldMeasureOffset(
        OneGrabTranslateCDTransformer transformer
    )
    {
        if (!measureOnlyDuringCompression)
        {
            return true;
        }

        return
            transformer.CurrentZMovementMode ==
            OneGrabTranslateCDTransformer
                .ZMovementMode
                .Compressing;
    }

    /// <summary>
    /// Measurement Space의 Local Z축을 기준으로
    /// 대상 Transform의 Z 위치를 반환합니다.
    ///
    /// Measurement Space가 없으면 World Z를 사용합니다.
    /// </summary>
    private float GetMeasurementSpaceZ(
        Transform target
    )
    {
        if (measurementSpace == null)
        {
            return target.position.z;
        }

        Vector3 localPosition =
            measurementSpace.InverseTransformPoint(
                target.position
            );

        return localPosition.z;
    }

    /// <summary>
    /// 외부 실험 매니저에서 참가자 번호를 설정합니다.
    /// 첫 Trial 시작 전에 호출하는 것이 좋습니다.
    /// </summary>
    public void SetParticipantId(
        string newParticipantId
    )
    {
        if (string.IsNullOrWhiteSpace(newParticipantId))
        {
            Debug.LogWarning(
                "[CDPilotCsvLogger] Participant ID가 비어 있습니다.",
                this
            );

            return;
        }

        participantId =
            newParticipantId.Trim();
    }

    /// <summary>
    /// 외부 실험 매니저에서 조건 이름을 설정합니다.
    /// </summary>
    public void SetConditionId(
        string newConditionId
    )
    {
        if (string.IsNullOrWhiteSpace(newConditionId))
        {
            Debug.LogWarning(
                "[CDPilotCsvLogger] Condition ID가 비어 있습니다.",
                this
            );

            return;
        }

        conditionId =
            newConditionId.Trim();
    }

    /// <summary>
    /// CSV 폴더 및 파일을 생성하고 헤더를 작성합니다.
    /// </summary>
    private bool EnsureCsvFileCreated()
    {
        if (!string.IsNullOrEmpty(_csvFilePath))
        {
            return true;
        }

        string directoryPath =
            Path.Combine(
                Application.persistentDataPath,
                folderName
            );

        try
        {
            Directory.CreateDirectory(
                directoryPath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[CDPilotCsvLogger] 로그 폴더 생성 실패\n" +
                exception.Message,
                this
            );

            return false;
        }

        string sessionTime =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture
            );

        string safeParticipantId =
            MakeSafeFileName(participantId);

        string fileName =
            $"{fileNamePrefix}_" +
            $"{safeParticipantId}_" +
            $"{sessionTime}.csv";

        _csvFilePath =
            Path.Combine(
                directoryPath,
                fileName
            );

        string header =
            "ParticipantId," +
            "TrialIndex," +
            "ConditionId," +
            "RecordedAt," +
            "LeftCD," +
            "RightCD," +
            "DeltaCD," +
            "LeftHandControllerOffsetM," +
            "RightHandControllerOffsetM";

        try
        {
            File.WriteAllText(
                _csvFilePath,
                header + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[CDPilotCsvLogger] CSV 파일 생성 실패\n" +
                exception.Message,
                this
            );

            _csvFilePath = null;

            return false;
        }

        Debug.Log(
            "[CDPilotCsvLogger] CSV 파일 생성 완료\n" +
            _csvFilePath,
            this
        );

        return true;
    }

    /// <summary>
    /// 필수 Inspector 참조를 확인합니다.
    /// </summary>
    private bool ValidateReferences()
    {
        bool valid =
            leftCDTransformer != null &&
            rightCDTransformer != null &&
            leftController != null &&
            rightController != null &&
            leftVirtualHand != null &&
            rightVirtualHand != null;

        if (!valid)
        {
            Debug.LogError(
                "[CDPilotCsvLogger] 참조가 누락되었습니다.\n" +
                "다음 필드를 확인하세요.\n" +
                "- Left CD Transformer\n" +
                "- Right CD Transformer\n" +
                "- Left Controller\n" +
                "- Right Controller\n" +
                "- Left Virtual Hand\n" +
                "- Right Virtual Hand",
                this
            );
        }

        return valid;
    }

    private void PrintRealtimeDebugLog()
    {
        if (!showRealtimeLog ||
            Time.unscaledTime < _nextRealtimeLogTime)
        {
            return;
        }

        _nextRealtimeLogTime =
            Time.unscaledTime +
            realtimeLogInterval;

        Debug.Log(
            $"[CD Pilot Offset]\n" +
            $"Current Offset | " +
            $"L:{_currentLeftOffset:F4}m, " +
            $"R:{_currentRightOffset:F4}m\n" +
            $"Maximum Offset | " +
            $"L:{_leftMaxHandControllerOffset:F4}m, " +
            $"R:{_rightMaxHandControllerOffset:F4}m",
            this
        );
    }

    private static string EscapeCsv(
        string value
    )
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool requiresQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!requiresQuotes)
        {
            return value;
        }

        return
            "\"" +
            value.Replace("\"", "\"\"") +
            "\"";
    }

    private static string MakeSafeFileName(
        string value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UnknownParticipant";
        }

        string result =
            value.Trim();

        foreach (
            char invalidCharacter
            in Path.GetInvalidFileNameChars()
        )
        {
            result =
                result.Replace(
                    invalidCharacter,
                    '_'
                );
        }

        return result;
    }
}