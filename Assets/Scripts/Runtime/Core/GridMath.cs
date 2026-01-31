using UnityEngine;

public static class GridMath
{
    // Convert world position to grid position (rounded)
    public static Vector2Int WorldToGrid(Vector2 world, GridConfig cfg)
    {
        Vector2 offset = world - cfg.originWorld;
        float invCellSize = 1f / cfg.cellSize;
        int x = Mathf.RoundToInt(offset.x * invCellSize);
        int y = Mathf.RoundToInt(offset.y * invCellSize);
        return new Vector2Int(x, y);
    }

    // Convert grid position to world position (center of cell)
    public static Vector2 GridToWorld(Vector2Int grid, GridConfig cfg)
    {
        float halfCell = cfg.cellSize * 0.5f;
        return new Vector2(
            cfg.originWorld.x + (grid.x * cfg.cellSize) + halfCell,
            cfg.originWorld.y + (grid.y * cfg.cellSize) + halfCell
        );
    }

    public static bool InBounds(Vector2Int p, GridConfig cfg)
    {
        return p.x >= 0 && p.x < cfg.width && p.y >= 0 && p.y < cfg.height;
    }
}
