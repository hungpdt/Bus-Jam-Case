using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CarDriveController : MonoBehaviour
{
    [Header("Destination")]
    public Transform parkingSpot; // assign in Inspector

    [Header("Behavior")]
    public float stopBeforeObstacle = 0.25f;  // how close to the obstacle we stop (looks like a bump)
    public float moveSpeedOverride = 0f;      // 0 = use agent speed; >0 overrides rollback speed
    public float stuckSpeedEps = 0.05f;

    [Header("Rollback")]
    public float rollbackSpeed = 8f;

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

    private Coroutine _co;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _obstacle = GetComponent<NavMeshObstacle>();

        _movement = GetComponent<CarMovement>();
        if (_movement == null) _movement = gameObject.AddComponent<CarMovement>();
        // copy important configurable values so inspector can keep using CarDriveController
        _movement.moveAlongForward = moveAlongForward;
        _movement.blockerMask = blockerMask;
        _movement.parkingArea = parkingArea;
        _movement.constrainToParkingArea = constrainToParkingArea;
        _movement.moveSpeedOverride = moveSpeedOverride;
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
        if (parkingSpot == null)
        {
            Debug.LogError($"[CarDriveController] parkingSpot is null on {name}");
            return;
        }

        if (verboseDebug) Debug.Log($"[CarDriveController][StartMove] {name} startPos={transform.position} parkingSpot={parkingSpot.position}");

        // Record starting transform for rollback
        _startPos = transform.position;
        _startRot = transform.rotation;

        // Disable own obstacle carving while moving
        if (_obstacle != null) _obstacle.enabled = false;

        // Ensure parking is on navmesh (only for fallback path checks)
        if (!NavMesh.SamplePosition(parkingSpot.position, out var destHit, 2f, NavMesh.AllAreas))
        {
            Debug.LogError($"[CarDriveController] Parking spot not on NavMesh: {parkingSpot.name}");
            StopAndBecomeObstacle();
            return;
        }

        Vector3 from = transform.position;
        Vector3 to = parkingSpot.position;

        // Check if the straight-line path is blocked by a PHYSICS collider
        _blockedRun = false;

        Vector3 physDir = to - from;
        physDir.y = 0f;
        float physDist = physDir.magnitude;
        if (physDir.sqrMagnitude < 0.0001f)
        {
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
                    Debug.LogWarning($"[CarDriveController] stopPoint not on NavMesh (physHit), using hit.position fallback.");
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
                StopAndBecomeObstacle();
                return;
            }
            dir.Normalize();

            Vector3 stopPoint = hit.position - dir * stopBeforeObstacle;
            if (!NavMesh.SamplePosition(stopPoint, out var stopHit, 1f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[CarDriveController] stopPoint not on NavMesh, using hit.position fallback.");
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

    private void OnMovementComplete()
    {
        StopAndBecomeObstacle();
        _isBusy = false;
        _co = null;
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
