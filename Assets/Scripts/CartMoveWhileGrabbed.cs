using UnityEngine;
using Oculus.Interaction;

public class CartMoveWhileGrabbed : MonoBehaviour
{
    public Grabbable grabbable;
    public Transform cartRoot;
    public float moveSpeed = 1f;

    void Update()
    {
        if (grabbable != null && cartRoot != null && grabbable.SelectingPointsCount > 0)
        {
            cartRoot.position += Vector3.forward * moveSpeed * Time.deltaTime;
        }
    }
}