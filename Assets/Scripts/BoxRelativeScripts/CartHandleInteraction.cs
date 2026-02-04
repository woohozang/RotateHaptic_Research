using UnityEngine;
using Oculus.Interaction;

public class CartHandleInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CartWeightSimulator weightSimulator;
    [SerializeField] private Grabbable leftHandGrabbable;
    [SerializeField] private Grabbable rightHandGrabbable;

    private bool leftGrabbed = false;
    private bool rightGrabbed = false;

    private void Start()
    {
        if (weightSimulator == null)
        {
            weightSimulator = GetComponentInParent<CartWeightSimulator>();
        }

        SetupEvents();
    }

    private void SetupEvents()
    {
        if (leftHandGrabbable != null)
        {
            leftHandGrabbable.WhenPointerEventRaised += OnLeftHandEvent;
        }

        if (rightHandGrabbable != null)
        {
            rightHandGrabbable.WhenPointerEventRaised += OnRightHandEvent;
        }
    }

    private void OnLeftHandEvent(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                leftGrabbed = true;
                CheckBothHands();
                break;
            case PointerEventType.Unselect:
                leftGrabbed = false;
                weightSimulator?.OnHandsReleased();
                break;
        }
    }

    private void OnRightHandEvent(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                rightGrabbed = true;
                CheckBothHands();
                break;
            case PointerEventType.Unselect:
                rightGrabbed = false;
                weightSimulator?.OnHandsReleased();
                break;
        }
    }

    private void CheckBothHands()
    {
        if (leftGrabbed && rightGrabbed)
        {
            weightSimulator?.OnBothHandsGrabbed();
        }
    }

    private void OnDestroy()
    {
        if (leftHandGrabbable != null)
        {
            leftHandGrabbable.WhenPointerEventRaised -= OnLeftHandEvent;
        }

        if (rightHandGrabbable != null)
        {
            rightHandGrabbable.WhenPointerEventRaised -= OnRightHandEvent;
        }
    }
}