using UnityEngine;

public class WheelBumpTrigger : MonoBehaviour
{
    public CartVisualBumpTilt bumpTiltController;

    [Header("This Wheel Group Role")]
    public bool reactsToLeftBump = true;
    public bool reactsToRightBump = false;

    private void OnTriggerEnter(Collider other)
    {
        if (bumpTiltController == null) return;

        if (reactsToLeftBump && other.CompareTag("LeftBump"))
        {
            bumpTiltController.EnterLeftBump();
        }

        if (reactsToRightBump && other.CompareTag("RightBump"))
        {
            bumpTiltController.EnterRightBump();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (bumpTiltController == null) return;

        if (reactsToLeftBump && other.CompareTag("LeftBump"))
        {
            bumpTiltController.ExitLeftBump();
        }

        if (reactsToRightBump && other.CompareTag("RightBump"))
        {
            bumpTiltController.ExitRightBump();
        }
    }
}