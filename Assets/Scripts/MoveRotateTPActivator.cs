using UnityEngine;
using Oculus.Interaction;

public class MoveRotateTPActivator : MonoBehaviour
{
    public Grabbable grabbable;          // BothHandle의 Grabbable
    public MonoBehaviour targetScript;   // MoveRotateTP 스크립트

    void Update()
    {
        if (grabbable == null || targetScript == null)
            return;

        bool isGrabbed = grabbable.SelectingPointsCount > 0;

        if (targetScript.enabled != isGrabbed)
        {
            targetScript.enabled = isGrabbed;
        }
    }
}