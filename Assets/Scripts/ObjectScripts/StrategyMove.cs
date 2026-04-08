using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;

public class StrategyMove : MonoBehaviour
{
    [Header("References")]
    public Grabbable[] cartGrabbables;
    public FirstPersonLocomotor locomotor;

    [Header("Settings")]
    public bool requireTwoHands = false;

    private bool _lastAnyGrabbed = false;

    void Update()
    {
        if (locomotor == null || cartGrabbables == null || cartGrabbables.Length == 0)
            return;

        bool anyGrabbed = false;

        for (int i = 0; i < cartGrabbables.Length; i++)
        {
            Grabbable g = cartGrabbables[i];
            if (g == null) continue;

            int grabCount = g.SelectingPointsCount;
            bool isGrabbed = requireTwoHands ? (grabCount >= 2) : (grabCount > 0);

            if (isGrabbed)
            {
                anyGrabbed = true;
                break;
            }
        }

        if (anyGrabbed != _lastAnyGrabbed)
        {
            if (anyGrabbed)
            {
                // 핵심: 이동 완전 차단 + 속도 초기화
                locomotor.DisableMovement();
            }
            else
            {
                //  다시 활성화 (자동으로 velocity 0됨)
                locomotor.EnableMovement();
            }

            _lastAnyGrabbed = anyGrabbed;
        }
    }
}