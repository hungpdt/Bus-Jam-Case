using UnityEngine;

[CreateAssetMenu(menuName = "BusOut/GridConfig")]
public class GridConfig : ScriptableObject
{
    public int width = 8;
    public int height = 10;
    public float cellSize = 1f;
    public Vector2 originWorld = Vector2.zero;

    // Optional: allow diagonal lanes like / or \
    public bool allowDiagonalLanes = true;
}
