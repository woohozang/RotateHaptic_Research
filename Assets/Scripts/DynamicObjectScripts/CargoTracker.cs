using System.Collections.Generic;
using UnityEngine;

public class CargoTracker : MonoBehaviour
{
    public enum BalanceState { Left, Center, Right }

    [Header("Filter (optional)")]
    [Tooltip("비워두면 모든 Rigidbody를 추적. 태그를 쓰면 해당 태그만 추적")]
    public string cargoTag = "Cargo";

    [Tooltip("true면 cargoTag를 사용해서 필터링")]
    public bool useTagFilter = true;

    [Header("Balance 판단 기준 (CartPivot local X 기준)")]
    [Tooltip("중앙 데드존(미터). |x| <= deadZone면 Center")]
    public float centerDeadZone = 0.08f;

    [Tooltip("히스테리시스(깜빡임 방지). deadZone에서 이 값만큼 더 나가야 Left/Right로 전환")]
    public float hysteresis = 0.03f;

    [Header("Debug")]
    public bool drawDebugGizmos = true;

    private readonly HashSet<Rigidbody> _cargos = new HashSet<Rigidbody>();
    private BalanceState _state = BalanceState.Center;

    private Vector3 _lastCom;

    private void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        if (useTagFilter)
        {
            if (!rb.CompareTag(cargoTag)) return;
        }

        _cargos.Add(rb);
    }

    private void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        _cargos.Remove(rb);
    }

    public bool HasCargo()
    {
        CleanupNulls();
        return _cargos.Count > 0;
    }

    public Vector3 GetCenterOfMass()
    {
        CleanupNulls();

        if (_cargos.Count == 0)
        {
            return _lastCom; // 없으면 마지막값 유지(옵션)
        }

        float totalMass = 0f;
        Vector3 sum = Vector3.zero;

        foreach (var rb in _cargos)
        {
            if (rb == null) continue;
            float m = Mathf.Max(0.0001f, rb.mass);
            totalMass += m;
            sum += rb.worldCenterOfMass * m;
        }

        if (totalMass <= 0f)
            return _lastCom;

        _lastCom = sum / totalMass;
        return _lastCom;
    }

    /// <summary>
    /// CartPivot 기준 local X로 Left/Center/Right 상태를 반환.
    /// 히스테리시스로 경계 깜빡임을 줄임.
    /// </summary>
    public BalanceState GetBalanceState(Transform cartPivot)
    {
        if (cartPivot == null) return BalanceState.Center;

        Vector3 com = GetCenterOfMass();
        float x = cartPivot.InverseTransformPoint(com).x;

        float enter = centerDeadZone + hysteresis; // Left/Right로 "들어갈" 임계
        float exit = centerDeadZone;              // Center로 "나올" 임계

        switch (_state)
        {
            case BalanceState.Center:
                if (x < -enter) _state = BalanceState.Left;
                else if (x > enter) _state = BalanceState.Right;
                break;

            case BalanceState.Left:
                if (x > -exit) _state = BalanceState.Center;
                break;

            case BalanceState.Right:
                if (x < exit) _state = BalanceState.Center;
                break;
        }

        return _state;
    }

    private void CleanupNulls()
    {
        _cargos.RemoveWhere(rb => rb == null);
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(_lastCom, 0.03f);
    }
}
