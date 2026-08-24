using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LikertQuestion : MonoBehaviour
{
    [Header("7 Likert Buttons")]
    [SerializeField]
    private Button[] buttons = new Button[7];

    [Header("Optional Button Backgrounds")]
    [SerializeField]
    private Image[] buttonImages = new Image[7];

    [Header("Selection Colors")]
    [SerializeField]
    private Color normalColor = Color.white;

    [SerializeField]
    private Color selectedColor = new Color(0.35f, 0.55f, 1f);

    private int selectedValue = 0;

    public int Value => selectedValue;
    public bool HasAnswer => selectedValue >= 1 && selectedValue <= 7;


    private void Awake()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            int value = i + 1;

            if (buttons[i] != null)
            {
                buttons[i].onClick.AddListener(
                    () => SelectValue(value)
                );
            }
        }

        ResetAnswer();
    }


    public void SelectValue(int value)
    {
        selectedValue = Mathf.Clamp(value, 1, 7);

        RefreshVisual();
    }


    private void RefreshVisual()
    {
        if (buttonImages == null)
            return;

        for (int i = 0; i < buttonImages.Length; i++)
        {
            if (buttonImages[i] == null)
                continue;

            int value = i + 1;

            buttonImages[i].color =
                value == selectedValue
                ? selectedColor
                : normalColor;
        }
    }


    public void ResetAnswer()
    {
        selectedValue = 0;

        RefreshVisual();
    }
}