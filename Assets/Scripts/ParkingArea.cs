using UnityEngine;

[ExecuteAlways]
public class ParkingArea : MonoBehaviour
{
    public Vector2 size = new Vector2(5f, 5f); // X=width, Y=depth (Z)
    public Color gizmoColor = new Color(0f, 1f, 0f, 0.2f);

    public bool IsInside(Vector3 worldPos)
    {
        Vector3 center = transform.position;
        float halfX = size.x * 0.5f;
        float halfZ = size.y * 0.5f;
        return (worldPos.x >= center.x - halfX && worldPos.x <= center.x + halfX &&
                worldPos.z >= center.z - halfZ && worldPos.z <= center.z + halfZ);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;
        Vector3 center = transform.position;
        Vector3 ext = new Vector3(size.x, 0.01f, size.y);
        Gizmos.DrawCube(center, ext);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, ext);
    }
}
