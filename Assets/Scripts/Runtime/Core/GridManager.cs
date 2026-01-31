using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private GridConfig config;

    // Occupancy: map cell -> vehicleId
    private readonly Dictionary<Vector2Int, int> _occupiedBy = new();

    public GridConfig Config => config;

    public void ClearAll()
    {
        _occupiedBy.Clear();
    }

    public bool IsOccupied(Vector2Int cell)
    {
        return _occupiedBy.ContainsKey(cell);
    }

    public int GetOccupantId(Vector2Int cell)
    {
        return _occupiedBy.TryGetValue(cell, out var id) ? id : -1;
    }

    // Register a vehicle occupying multiple cells
    public void SetOccupied(IEnumerable<Vector2Int> cells, int vehicleId)
    {
        foreach (var cell in cells)
        {
            if (_occupiedBy.TryGetValue(cell, out var existingId))
            {
                if (existingId == vehicleId)
                {
                    _occupiedBy[cell] = vehicleId;
                }
            }
            else
            {
                _occupiedBy[cell] = vehicleId;
            }
        }
    }

    public void ClearOccupied(IEnumerable<Vector2Int> cells, int vehicleId)
    {
        foreach (var cell in cells)
        {
            if (_occupiedBy.TryGetValue(cell, out var existingId) && existingId == vehicleId)
            {
                _occupiedBy.Remove(cell);
            }
        }
    }

    // Validate all cells are free and inside bounds
    public bool AreCellsFree(IEnumerable<Vector2Int> cells, int ignoreVehicleId = -1)
    {
        foreach (var cell in cells)
        {
            if (!GridMath.InBounds(cell, config))
            {
                return false;
            }

            if (_occupiedBy.TryGetValue(cell, out var existingId) && existingId != ignoreVehicleId)
            {
                return false;
            }
        }

        return true;
    }
}
