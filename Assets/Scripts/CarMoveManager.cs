using UnityEngine;

public class CarMoveManager : MonoBehaviour
{
    public static CarMoveManager I;

    private CarDriveController _moving;

    void Awake()
    {
        I = this;
    }

    public void RequestMove(CarDriveController car)
    {
        if (car == null) return;

        // If another car is moving, stop it first
        if (_moving != null && _moving != car)
        {
            //_moving.StopHere("interrupt");
        }

        _moving = car;
        // Use OnClicked which respects the car's busy state
        car.OnClicked();
    }
}
