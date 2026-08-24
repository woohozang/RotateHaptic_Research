using System;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BarbellPilotSurveyManager : MonoBehaviour
{
    public enum PilotGroup
    {
        A_RightHeavy,
        B_LeftHeavy
    }

    [Header("Participant")]
    [SerializeField]
    private string participantID = "P01";

    [SerializeField]
    private PilotGroup group = PilotGroup.A_RightHeavy;


    [Header("Current Trial")]
    [Tooltip("현재 참가자가 몇 번째 조건을 수행 중인지")]
    [SerializeField]
    private int trialIndex = 1;


    [Header("Condition Controller")]
    [SerializeField]
    private BarbellPilotConditionController conditionController;


    [Header("Questions")]
    [SerializeField]
    private LikertQuestion q1;

    [SerializeField]
    private LikertQuestion q2;

    [SerializeField]
    private LikertQuestion q3;

    [SerializeField]
    private LikertQuestion q4;

    [SerializeField]
    private LikertQuestion q5;


    [Header("UI")]
    [SerializeField]
    private Button submitButton;

    [SerializeField]
    private TMP_Text warningText;

    [SerializeField]
    private TMP_Text conditionProgressText;

    [SerializeField]
    private GameObject surveyCanvas;


    [Header("Submit Behavior")]
    [Tooltip("Submit 후 설문 Canvas를 자동으로 숨김")]
    [SerializeField]
    private bool hideSurveyAfterSubmit = true;


    private bool isSubmitting = false;

    private string saveDirectory;
    private string csvPath;


    private void Awake()
    {
        InitializeSavePath();

        if (submitButton != null)
        {
            submitButton.onClick.AddListener(
                SubmitSurvey
            );
        }

        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }

        UpdateProgressText();
    }


    // ======================================================
    // 저장 경로
    // ======================================================

    private void InitializeSavePath()
    {
#if UNITY_EDITOR

        // Unity Project Root
        // Assets 폴더의 한 단계 위
        string projectRoot =
            Directory.GetParent(
                Application.dataPath
            ).FullName;

        saveDirectory =
            Path.Combine(
                projectRoot,
                "PilotSurveyData"
            );

#else

        // Quest / Android Build
        saveDirectory =
            Path.Combine(
                Application.persistentDataPath,
                "PilotSurveyData"
            );

#endif

        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(
                saveDirectory
            );
        }


        csvPath =
            Path.Combine(
                saveDirectory,
                "BarbellPilotSurvey.csv"
            );


        Debug.Log(
            "[Pilot Survey] Save Path: "
            + csvPath
        );


        CreateCSVIfNeeded();
    }


    // ======================================================
    // CSV 최초 생성
    // ======================================================

    private void CreateCSVIfNeeded()
    {
        if (File.Exists(csvPath))
            return;


        // Excel 한글 깨짐 방지용 UTF-8 BOM
        UTF8Encoding utf8BOM =
            new UTF8Encoding(true);


        using (StreamWriter writer =
               new StreamWriter(
                   csvPath,
                   false,
                   utf8BOM))
        {
            writer.WriteLine(
                "Timestamp," +
                "ParticipantID," +
                "Group," +
                "Trial," +
                "FeedbackCondition," +
                "WeightSide," +
                "Q1_WeightBias," +
                "Q2_WeightDifference," +
                "Q3_Resistance," +
                "Q4_PerceptualValidity," +
                "Q5_Immediacy"
            );
        }
    }


    // ======================================================
    // Submit
    // ======================================================

    public void SubmitSurvey()
    {
        if (isSubmitting)
            return;


        // ------------------------------
        // 모든 문항 응답 여부 검사
        // ------------------------------

        if (!AllQuestionsAnswered())
        {
            ShowWarning(
                "모든 문항에 응답해 주세요."
            );

            return;
        }


        isSubmitting = true;


        SaveCurrentResponse();


        Debug.Log(
            $"[Pilot Survey] " +
            $"{participantID} / Trial {trialIndex} 저장 완료."
        );


        // 다음 Trial 번호
        trialIndex++;


        ResetSurvey();


        if (hideSurveyAfterSubmit &&
            surveyCanvas != null)
        {
            surveyCanvas.SetActive(false);
        }


        isSubmitting = false;
    }


    // ======================================================
    // 모든 응답 확인
    // ======================================================

    private bool AllQuestionsAnswered()
    {
        return
            q1 != null &&
            q2 != null &&
            q3 != null &&
            q4 != null &&
            q5 != null &&

            q1.HasAnswer &&
            q2.HasAnswer &&
            q3.HasAnswer &&
            q4.HasAnswer &&
            q5.HasAnswer;
    }


    // ======================================================
    // CSV 저장
    // ======================================================

    private void SaveCurrentResponse()
    {
        string conditionName = "Unknown";
        string weightSide = GetGroupWeightSide();


        if (conditionController != null)
        {
            conditionName =
                conditionController
                    .CurrentCondition
                    .ToString();

            weightSide =
                conditionController
                    .CurrentWeightSide
                    .ToString();
        }


        string timestamp =
            DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture
            );


        string line =
            EscapeCSV(timestamp) + "," +
            EscapeCSV(participantID) + "," +
            EscapeCSV(group.ToString()) + "," +
            trialIndex.ToString(
                CultureInfo.InvariantCulture
            ) + "," +
            EscapeCSV(conditionName) + "," +
            EscapeCSV(weightSide) + "," +
            q1.Value + "," +
            q2.Value + "," +
            q3.Value + "," +
            q4.Value + "," +
            q5.Value;


        UTF8Encoding utf8BOM =
            new UTF8Encoding(true);


        using (StreamWriter writer =
               new StreamWriter(
                   csvPath,
                   true,
                   utf8BOM))
        {
            writer.WriteLine(line);
        }
    }


    // ======================================================
    // A/B Group에 따른 Heavy Side
    // ======================================================

    private string GetGroupWeightSide()
    {
        switch (group)
        {
            case PilotGroup.A_RightHeavy:

                return "RightHeavy";


            case PilotGroup.B_LeftHeavy:

                return "LeftHeavy";
        }


        return "Unknown";
    }


    // ======================================================
    // CSV 문자열 처리
    // ======================================================

    private string EscapeCSV(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";


        if (value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n"))
        {
            value =
                value.Replace(
                    "\"",
                    "\"\""
                );

            return "\"" + value + "\"";
        }


        return value;
    }


    // ======================================================
    // 설문 초기화
    // ======================================================

    public void ResetSurvey()
    {
        if (q1 != null)
            q1.ResetAnswer();

        if (q2 != null)
            q2.ResetAnswer();

        if (q3 != null)
            q3.ResetAnswer();

        if (q4 != null)
            q4.ResetAnswer();

        if (q5 != null)
            q5.ResetAnswer();


        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }


        UpdateProgressText();
    }


    // ======================================================
    // 설문 표시
    // ======================================================

    public void ShowSurvey()
    {
        ResetSurvey();

        if (surveyCanvas != null)
        {
            surveyCanvas.SetActive(true);
        }
    }


    // ======================================================
    // 경고
    // ======================================================

    private void ShowWarning(string message)
    {
        if (warningText == null)
            return;


        warningText.text = message;
        warningText.gameObject.SetActive(true);
    }


    // ======================================================
    // Trial Progress
    // ======================================================

    private void UpdateProgressText()
    {
        if (conditionProgressText == null)
            return;


        conditionProgressText.text =
            $"조건 {Mathf.Clamp(trialIndex, 1, 6)} / 6";
    }


    // ======================================================
    // 외부에서 Participant 변경
    // ======================================================

    public void SetParticipant(
        string id,
        PilotGroup newGroup)
    {
        participantID = id;
        group = newGroup;
    }


    // ======================================================
    // 테스트용
    // ======================================================

    [ContextMenu("Print Save Path")]
    private void PrintSavePath()
    {
        Debug.Log(
            "[Pilot Survey] "
            + csvPath
        );
    }
}