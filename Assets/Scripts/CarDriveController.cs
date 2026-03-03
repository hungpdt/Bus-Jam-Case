using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CarDriveController : MonoBehaviour
{
    [Header("Destination (Fallback)")]
    public Transform parkingSpot; // fallback when ParkingSlotManager is not used

    [Header("Parking Slots")]
    public bool useParkingSlotManager = true;
    public ParkingSlotManager parkingSlotManager;
    public bool alignToSlotRotationOnPark = true;

    [Header("Behavior")]
    public float stopBeforeObstacle = 0.25f;  // how close to the obstacle we stop (looks like a bump)
    public float moveSpeedOverride = 0f;      // 0 = use driveSpeed
    public float stuckSpeedEps = 0.05f;

    [Header("Rollback")]
    public float rollbackSpeed = 8f;

    [Header("Drive (Outside Parking Area)")]
    public float driveSpeed = 3.5f;
    public float turnSpeedDeg = 240f;

    [Header("Movement")]
    public bool moveAlongForward = true; // if true, move along the car's forward (nose) direction

    [Header("Physics")]
    public LayerMask blockerMask = ~0; // set in Inspector to only include car layers

    [Header("Parking Area")]
    public ParkingArea parkingArea; // optional; when set used to constrain forward-only movement
    public bool constrainToParkingArea = true;

    [Header("Debug")]
    public bool verboseDebug = false;
    public Color debugColor = Color.red;

    private NavMeshAgent _agent;
    private NavMeshObstacle _obstacle;
    private CarMovement _movement;

    private Vector3 _startPos;
    private Quaternion _startRot;

    private bool _isBusy;
    private bool _blockedRun; // true if this run is "hit obstacle then rollback"

    private int _reservedSlotIndex = -1;
    private Quaternion _reservedSlotRotation = Quaternion.identity;
    private Vector3 _targetDestination;

    private Coroutine _co;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _obstacle = GetComponent<NavMeshObstacle>();

        _movement = GetComponent<CarMovement>();
        if (_movement == null) _movement = gameObject.AddComponent<CarMovement>();

        if (parkingSlotManager == null) parkingSlotManager = FindObjectOfType<ParkingSlotManager>();

        // copy important configurable values so inspector can keep using CarDriveController
        _movement.moveAlongForward = moveAlongForward;
        _movement.turnSpeedDeg = turnSpeedDeg;
        _movement.blockerMask = blockerMask;
        _movement.parkingArea = parkingArea;
        _movement.constrainToParkingArea = constrainToParkingArea;
        _movement.moveSpeedOverride = moveSpeedOverride;
        _movement.driveSpeed = driveSpeed;
        _movement.rollbackSpeed = rollbackSpeed;
        _movement.stopBeforeObstacle = stopBeforeObstacle;
        _movement.verboseDebug = verboseDebug;
        _movement.debugColor = debugColor;

        if (_obstacle == null)
            Debug.LogError($"[CarDriveController] Missing NavMeshObstacle on {name}. Add it and enable Carve.");
    }

    public bool IsBusy => _isBusy;

    public void OnClicked()
    {
        if (_isBusy)
        {
            if (verboseDebug) Debug.Log($"[CarDriveController] {name} busy - click ignored");
            return;
        }

        StartMove();
    }

    public void StartMove()
    {
        ReleaseReservedSlotIfAny();

        if (!TryResolveParkingTarget(out _targetDestination, out _reservedSlotRotation))
        {
            return;
        }

        if (verboseDebug)
        {
            Debug.Log($"[CarDriveController][StartMove] {name} startPos={transform.position} target={_targetDestination}");
        }

        // Record starting transform for rollback
        _startPos = transform.position;
        _startRot = transform.rotation;

        // Disable own obstacle carving while moving
        if (_obstacle != null) _obstacle.enabled = false;

        // Ensure parking is on navmesh (only for fallback path checks)
        if (!NavMesh.SamplePosition(_targetDestination, out var destHit, 2f, NavMesh.AllAreas))
        {
            Debug.LogError($"[CarDriveController] Target parking point is not on NavMesh.");
            ReleaseReservedSlotIfAny();
            StopAndBecomeObstacle();
            return;
        }

        Vector3 from = transform.position;
        Vector3 to = destHit.position;
        _targetDestination = to;

        // Check if the straight-line path is blocked by a PHYSICS collider
        _blockedRun = false;

        Vector3 physDir = to - from;
        physDir.y = 0f;
        float physDist = physDir.magnitude;
        if (physDir.sqrMagnitude < 0.0001f)
        {
            FinalizeSlotReservation(true);
            StopAndBecomeObstacle();
            return;
        }
        physDir.Normalize();

        if (Physics.Raycast(from, physDir, out RaycastHit physHit, physDist, blockerMask, QueryTriggerInteraction.Ignore))
        {
            if (verboseDebug) Debug.Log($"[CarDriveController][PhysCast] hit={physHit.collider.name} point={physHit.point} dist={physHit.distance}");

            var other = physHit.collider.GetComponentInParent<CarDriveController>();
            if (other != null && other != this)
            {
                // There is a physical car blocking the straight path -> stop near it and rollback
                Vector3 stopPoint = physHit.point - physDir * stopBeforeObstacle;
                if (!NavMesh.SamplePosition(stopPoint, out var stopHit, 1f, NavMesh.AllAreas))
                {
                    Debug.LogWarning("[CarDriveController] stopPoint not on NavMesh (physHit), using hit.position fallback.");
                    stopHit.position = physHit.point;
                }

                _blockedRun = true;
                _isBusy = true;

                Debug.Log($"[CarDriveController] {name} straight-line BLOCKED by PHYSICS -> bump then rollback.");
                if (verboseDebug) Debug.DrawLine(from, physHit.point, debugColor, 2f);
                if (_agent != null) _agent.enabled = false;
                _movement.StartMoveToThenRollback(stopHit.position, _startPos, _startRot, OnMovementComplete);
                return;
            }
        }

        // Fallback: Check if the straight-line path is blocked on NavMesh
        if (NavMesh.Raycast(from, to, out NavMeshHit hit, NavMesh.AllAreas))
        {
            if (verboseDebug) Debug.Log($"[CarDriveController][NavMeshCast] hitPos={hit.position}");
            Vector3 dir = (to - from);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                ReleaseReservedSlotIfAny();
                StopAndBecomeObstacle();
                return;
            }
            dir.Normalize();

            Vector3 stopPoint = hit.position - dir * stopBeforeObstacle;
            if (!NavMesh.SamplePosition(stopPoint, out var stopHit, 1f, NavMesh.AllAreas))
            {
                Debug.LogWarning("[CarDriveController] stopPoint not on NavMesh, using hit.position fallback.");
                stopHit.position = hit.position;
            }

            _blockedRun = true;
            _isBusy = true;

            Debug.Log($"[CarDriveController] {name} straight-line BLOCKED -> bump then rollback.");
            if (verboseDebug) Debug.DrawLine(from, hit.position, debugColor, 2f);
            if (_agent != null) _agent.enabled = false;
            _movement.StartMoveToThenRollback(stopHit.position, _startPos, _startRot, OnMovementComplete);
            return;
        }

        // Straight line is clear -> move straight to parking (manual, no pathfinding)
        _blockedRun = false;
        _isBusy = true;

        Debug.Log($"[CarDriveController] {name} straight-line CLEAR -> move straight to parking.");
        if (_agent != null) _agent.enabled = false;
        _movement.StartMoveStraight(to, _startPos, _startRot, OnMovementComplete);
    }

    private bool TryResolveParkingTarget(out Vector3 parkingTarget, out Quaternion desiredRotation)
    {
        parkingTarget = Vector3.zero;
        desiredRotation = transform.rotation;

        if (useParkingSlotManager && parkingSlotManager != null)
        {
            if (!parkingSlotManager.TryReserveNearestSlot(this, transform.position, out _reservedSlotIndex, out parkingTarget, out desiredRotation))
            {
                Debug.Log("Game over");
                return false;
            }

            return true;
        }

        if (parkingSpot == null)
        {
            Debug.LogError($"[CarDriveController] parkingSpot is null on {name}");
            return false;
        }

        parkingTarget = parkingSpot.position;
        _reservedSlotIndex = -1;
        return true;
    }

    private void OnMovementComplete()
    {
        bool movementSucceeded = (_movement == null) || !_movement.DidRollbackInRun;
        FinalizeSlotReservation(movementSucceeded);

        StopAndBecomeObstacle();
        _isBusy = false;
        _co = null;
    }

    private void FinalizeSlotReservation(bool movementSucceeded)
    {
        if (_reservedSlotIndex < 0 || parkingSlotManager == null)
        {
            _reservedSlotIndex = -1;
            return;
        }

        if (!movementSucceeded)
        {
            parkingSlotManager.ReleaseSlot(this, _reservedSlotIndex);
            _reservedSlotIndex = -1;
            return;
        }

        if (parkingSlotManager.ConfirmParked(this, _reservedSlotIndex, out Vector3 snappedPos, out Quaternion snappedRot))
        {
            transform.position = new Vector3(snappedPos.x, transform.position.y, snappedPos.z);
            if (alignToSlotRotationOnPark) transform.rotation = snappedRot;
        }

        _reservedSlotIndex = -1;
    }

    private void ReleaseReservedSlotIfAny()
    {
        if (_reservedSlotIndex >= 0 && parkingSlotManager != null)
        {
            parkingSlotManager.ReleaseSlot(this, _reservedSlotIndex);
        }

        _reservedSlotIndex = -1;
    }

    private void StopAndBecomeObstacle()
    {
        if (_obstacle != null) _obstacle.enabled = true;
        Debug.Log($"[CarDriveController] {name} stopped. blockedRun={_blockedRun}");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        // draw forward direction
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.0f);

        if (parkingSpot != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(parkingSpot.position, 0.08f);
            Gizmos.DrawLine(transform.position, parkingSpot.position);
        }

        if (parkingArea != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = parkingArea.transform.position;
            Vector3 size = new Vector3(parkingArea.size.x, 0.02f, parkingArea.size.y);
            Gizmos.DrawWireCube(center + Vector3.up * 0.01f, size);
        }
    }
}
