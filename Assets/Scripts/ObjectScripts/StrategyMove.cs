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

    [Header("Vignette")]
    public GameObject Vignett;

    private bool _lastAnyGrabbed = false;

    private void Update()
    {
        if (locomotor == null ||
            cartGrabbables == null ||
            cartGrabbables.Length == 0)
        {
            return;
        }

        bool anyGrabbed = false;

        for (int i = 0; i < cartGrabbables.Length; i++)
        {
            Grabbable g = cartGrabbables[i];

            if (g == null)
                continue;

            int grabCount = g.SelectingPointsCount;

            bool isGrabbed = requireTwoHands
                ? grabCount >= 2
                : grabCount > 0;

            if (isGrabbed)
            {
                anyGrabbed = true;
                break;
            }
        }

        // 잡기 상태가 변경된 순간에만 처리
        if (anyGrabbed == _lastAnyGrabbed)
            return;

        if (anyGrabbed)
        {
            /*
             * 기존 의도 유지:
             * 1. 이동 상태 및 속도 초기화
             * 2. Locomotor 전체를 꺼서 이동·회전·앉기 차단
             * 3. 카트 조작 중 Vignette 비활성화
             */
            locomotor.DisableMovement();
            locomotor.enabled = false;

            if (Vignett != null)
                Vignett.SetActive(false);
        }
        else
        {
            /*
             * 기존 의도 유지:
             * 1. Locomotor 다시 활성화
             * 2. 이동 허용
             * 3. Vignette 다시 활성화
             */
            locomotor.enabled = true;
            locomotor.EnableMovement();

            if (Vignett != null)
                Vignett.SetActive(true);
        }

        _lastAnyGrabbed = anyGrabbed;
    }
}