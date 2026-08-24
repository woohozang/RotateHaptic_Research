using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Barbell Pilot의 실험 조건 관리.
///
/// Visual Condition
/// - CD_1_1
/// - CD_1_075
/// - CD_1_05
///
/// Vib Condition
/// - Vib_Base   : 0.20 / 0.20
/// - Vib_Medium : Heavy = 0.45
/// - Vib_Strong : Heavy = 0.60
///
/// Weight Side
/// - Equal
/// - LeftHeavy
/// - RightHeavy
/// </summary>
public class BarbellPilotConditionController : MonoBehaviour
{
    public enum FeedbackCondition
    {
        CD_1_1,
        CD_1_075,
        CD_1_05,

        Vib_Base,
        Vib_Medium,
        Vib_Strong
    }


    public enum WeightSide
    {
        Equal,
        LeftHeavy,
        RightHeavy
    }


    [Header("Current Trial")]

    [SerializeField]
    private FeedbackCondition _condition =
        FeedbackCondition.CD_1_1;

    [SerializeField]
    private WeightSide _weightSide =
        WeightSide.Equal;


    [Header("References")]

    [SerializeField]
    private AsymmetricBarbellTwoGrabTransformer
        _cdTransformer;

    [SerializeField]
    private BarbellPilotHaptics
        _haptics;


    // ============================================
    // C/D Parameters
    // ============================================

    [Header("C/D Settings")]

    [Tooltip("C/D Medium 조건")]
    [Range(0.5f, 1f)]
    [SerializeField]
    private float _cdMedium = 0.75f;

    [Tooltip("C/D Strong 조건")]
    [Range(0.5f, 1f)]
    [SerializeField]
    private float _cdStrong = 0.50f;


    // ============================================
    // Vibration Parameters
    // ============================================

    [Header("Vibration Settings")]

    [Tooltip("가벼운 쪽 및 Baseline 진동")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _vibBase = 0.20f;

    [Tooltip("Medium 조건에서 무거운 쪽 진동")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _vibMedium = 0.45f;

    [Tooltip("Strong 조건에서 무거운 쪽 진동")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _vibStrong = 0.60f;


    [Header("Debug")]

    [SerializeField]
    private bool _showDebugLog = true;


    // ============================================
    // 외부 접근용
    // SurveyManager에서도 사용 가능
    // ============================================

    public FeedbackCondition CurrentCondition
        => _condition;

    public WeightSide CurrentWeightSide
        => _weightSide;


    private void Start()
    {
        ApplyCurrentTrial();
    }


    // ============================================
    // Inspector Context Menu
    // ============================================

    [ContextMenu("Apply Current Trial")]
    public void ApplyCurrentTrial()
    {
        // 기본값
        float leftCD = 1f;
        float rightCD = 1f;

        float leftVib = 0f;
        float rightVib = 0f;

        bool vibrationEnabled = false;


        // ========================================
        // C/D CONDITIONS
        // ========================================

        switch (_condition)
        {
            case FeedbackCondition.CD_1_1:
            case FeedbackCondition.CD_1_075:
            case FeedbackCondition.CD_1_05:
                {
                    // C/D 조건에서는 진동 OFF
                    vibrationEnabled = false;

                    leftVib = 0f;
                    rightVib = 0f;


                    float heavyCD = 1f;


                    switch (_condition)
                    {
                        case FeedbackCondition.CD_1_1:

                            heavyCD = 1f;

                            break;


                        case FeedbackCondition.CD_1_075:

                            heavyCD =
                                _cdMedium;

                            break;


                        case FeedbackCondition.CD_1_05:

                            heavyCD =
                                _cdStrong;

                            break;
                    }


                    // --------------------------------
                    // 어느 쪽이 Heavy인지에 따라
                    // C/D 적용
                    // --------------------------------

                    switch (_weightSide)
                    {
                        case WeightSide.Equal:

                            leftCD = 1f;
                            rightCD = 1f;

                            break;


                        case WeightSide.LeftHeavy:

                            leftCD =
                                heavyCD;

                            rightCD =
                                1f;

                            break;


                        case WeightSide.RightHeavy:

                            leftCD =
                                1f;

                            rightCD =
                                heavyCD;

                            break;
                    }


                    break;
                }


            // ====================================
            // VIBRATION CONDITIONS
            // ====================================

            case FeedbackCondition.Vib_Base:
            case FeedbackCondition.Vib_Medium:
            case FeedbackCondition.Vib_Strong:
                {
                    // Vib 조건에서는
                    // C/D를 반드시 1:1
                    leftCD = 1f;
                    rightCD = 1f;

                    vibrationEnabled = true;


                    float heavyAmplitude =
                        _vibBase;


                    switch (_condition)
                    {
                        case FeedbackCondition.Vib_Base:

                            heavyAmplitude =
                                _vibBase;

                            break;


                        case FeedbackCondition.Vib_Medium:

                            heavyAmplitude =
                                _vibMedium;

                            break;


                        case FeedbackCondition.Vib_Strong:

                            heavyAmplitude =
                                _vibStrong;

                            break;
                    }


                    // --------------------------------
                    // Equal은 Medium / Strong이라도
                    // 양쪽 모두 Base
                    // --------------------------------

                    switch (_weightSide)
                    {
                        case WeightSide.Equal:

                            leftVib =
                                _vibBase;

                            rightVib =
                                _vibBase;

                            break;


                        case WeightSide.LeftHeavy:

                            leftVib =
                                heavyAmplitude;

                            rightVib =
                                _vibBase;

                            break;


                        case WeightSide.RightHeavy:

                            leftVib =
                                _vibBase;

                            rightVib =
                                heavyAmplitude;

                            break;
                    }


                    break;
                }
        }


        // ========================================
        // C/D 적용
        // ========================================

        if (_cdTransformer != null)
        {
            _cdTransformer.SetCDRatio(
                leftCD,
                rightCD
            );
        }
        else
        {
            Debug.LogWarning(
                "[Barbell Pilot] " +
                "C/D Transformer가 연결되지 않았습니다."
            );
        }


        // ========================================
        // Vibration 적용
        // ========================================

        if (_haptics != null)
        {
            _haptics.SetFeedback(
                vibrationEnabled,
                leftVib,
                rightVib
            );
        }
        else
        {
            Debug.LogWarning(
                "[Barbell Pilot] " +
                "Haptics가 연결되지 않았습니다."
            );
        }


        // ========================================
        // Debug
        // ========================================

        if (_showDebugLog)
        {
            Debug.Log(
                "\n========== BARBELL PILOT ==========\n" +
                $"Condition : {_condition}\n" +
                $"WeightSide: {_weightSide}\n" +
                $"C/D       : L={leftCD:F2}, R={rightCD:F2}\n" +
                $"Vibration : L={leftVib:F2}, R={rightVib:F2}\n" +
                $"Vib ON    : {vibrationEnabled}\n" +
                "==================================="
            );
        }
    }


    // ============================================
    // 외부에서 Trial 전체 변경
    // ============================================

    public void SetTrial(
        FeedbackCondition condition,
        WeightSide weightSide)
    {
        _condition =
            condition;

        _weightSide =
            weightSide;


        ApplyCurrentTrial();
    }


    // ============================================
    // Condition만 변경
    // ============================================

    public void SetCondition(
        FeedbackCondition condition)
    {
        _condition =
            condition;

        ApplyCurrentTrial();
    }


    // ============================================
    // Heavy Side만 변경
    // ============================================

    public void SetWeightSide(
        WeightSide weightSide)
    {
        _weightSide =
            weightSide;

        ApplyCurrentTrial();
    }


    // ============================================
    // 실험 종료 등에서 모든 피드백 OFF
    // ============================================

    public void DisableAllFeedback()
    {
        if (_cdTransformer != null)
        {
            _cdTransformer.SetCDRatio(
                1f,
                1f
            );
        }


        if (_haptics != null)
        {
            _haptics.SetFeedback(
                false,
                0f,
                0f
            );

            _haptics.StopImmediately();
        }


        if (_showDebugLog)
        {
            Debug.Log(
                "[Barbell Pilot] " +
                "All feedback disabled."
            );
        }
    }
}