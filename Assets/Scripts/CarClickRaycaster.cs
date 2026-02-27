using UnityEngine;

public class CarClickRaycaster : MonoBehaviour
{
    public Camera cam;

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (cam == null) { Debug.LogError("[CarClickRaycaster] cam is null"); return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
        {
            var car = hit.collider.GetComponentInParent<CarDriveController>();
            if (car != null)
            {
                if (CarMoveManager.I == null)
                {
                    Debug.LogError("[CarClickRaycaster] CarMoveManager not found in scene.");
                    return;
                }
                CarMoveManager.I.RequestMove(car);
            }
        }
    }
}
