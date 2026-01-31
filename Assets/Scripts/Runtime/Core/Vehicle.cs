using System.Collections.Generic;
using UnityEngine;

public enum VehicleAxis
{
    Horizontal,
    Vertical,
    DiagNE, // /
    DiagNW  // \
}

public class Vehicle : MonoBehaviour
{
    [SerializeField] private VehicleConfig config;

    public int VehicleId { get; private set; }
    public VehicleAxis Axis { get; private set; }
    public Vector2Int AnchorCell { get; private set; } // "head" cell (define consistent)

    public int Length => config != null ? config.length : 2;

    public void Init(int vehicleId, Vector2Int anchor, VehicleAxis axis)
    {
        VehicleId = vehicleId;
        AnchorCell = anchor;
        Axis = axis;
    }

    public void SetAnchor(Vector2Int newAnchor)
    {
        AnchorCell = newAnchor;
    }

    // Return all occupied cells based on AnchorCell, Axis, Length
    public IEnumerable<Vector2Int> GetOccupiedCells()
    {
        Vector2Int direction;
        switch (Axis)
        {
            case VehicleAxis.Horizontal:
                direction = new Vector2Int(1, 0);
                break;
            case VehicleAxis.Vertical:
                direction = new Vector2Int(0, 1);
                break;
            case VehicleAxis.DiagNE:
                direction = new Vector2Int(1, 1);
                break;
            case VehicleAxis.DiagNW:
                direction = new Vector2Int(-1, 1);
                break;
            default:
                direction = new Vector2Int(1, 0);
                break;
        }

        for (int i = 0; i < Length; i++)
        {
            yield return AnchorCell + (direction * i);
        }
    }
}
