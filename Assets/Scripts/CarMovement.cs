using System;
using System.Collections;
using UnityEngine;

public class CarMovement : MonoBehaviour
{
    [Header("Movement")]
    public bool moveAlongForward = true;

    [Header("Physics")]
    public LayerMask blockerMask = ~0;

    [Header("Parking Area")]
    public ParkingArea parkingArea;
    public bool constrainToParkingArea = true;

    [Header("Speeds")]
    public float moveSpeedOverride = 0f;
    public float rollbackSpeed = 8f;

    [Header("Collision")]
    public float stopBeforeObstacle = 0.25f;

    [Header("Debug")]
    public bool verboseDebug = false;
    public Color debugColor = Color.red;

    private Coroutine _co;

    private void StopCurrent()
    {
        if (_co != null) StopCoroutine(_co);
        _co = null;
    }

    // Start deterministic straight move. onComplete called when move finishes (no rollback).
    public void StartMoveStraight(Vector3 dest, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        StopCurrent();
        _co = StartCoroutine(MoveStraightTo(dest, startPos, startRot, onComplete));
    }

    // Start move to bump point then rollback. onComplete called when whole flow finishes.
    public void StartMoveToThenRollback(Vector3 bumpPoint, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        StopCurrent();
        _co = StartCoroutine(MoveToThenRollback(bumpPoint, startPos, startRot, onComplete));
    }

    private IEnumerator MoveStraightTo(Vector3 dest, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        float spd = moveSpeedOverride > 0f ? moveSpeedOverride : rollbackSpeed;
        Vector3 targetFlat = dest; targetFlat.y = transform.position.y;

        if (moveAlongForward)
        {
            Vector3 forwardFlat = transform.forward; forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = (targetFlat - transform.position).normalized; else forwardFlat.Normalize();

            float along = Vector3.Dot(targetFlat - transform.position, forwardFlat);
            if (along < 0f) { forwardFlat = -forwardFlat; along = -along; }

            bool wasInside = (parkingArea != null) ? parkingArea.IsInside(transform.position) : false;
            while (along > 0.0004f)
            {
                float step = Mathf.Min(spd * Time.deltaTime, along);
                if (constrainToParkingArea && wasInside && (parkingArea != null) && !parkingArea.IsInside(transform.position)) break;

                if (Physics.Raycast(transform.position, forwardFlat, out RaycastHit hit, step + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    if (verboseDebug) Debug.Log($"[CarMovement][MoveStraightTo][Hit] collider={hit.collider.name} point={hit.point} dist={hit.distance}");
                    Vector3 stopPoint = hit.point - forwardFlat * stopBeforeObstacle;
                    transform.position = stopPoint;
                    // rollback
                    yield return RollbackToStart(startPos, startRot);
                    onComplete?.Invoke();
                    yield break;
                }

                transform.position += forwardFlat * step;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forwardFlat, Vector3.up), 12f * Time.deltaTime);
                along -= step;
                yield return null;
            }

            if (!constrainToParkingArea || (parkingArea == null) || parkingArea.IsInside(transform.position))
            {
                transform.position = targetFlat;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forwardFlat, Vector3.up), 12f * Time.deltaTime);
                onComplete?.Invoke();
                yield break;
            }
        }

        // fallback free move
        while ((transform.position - targetFlat).sqrMagnitude > 0.0004f)
        {
            Vector3 cur = transform.position;
            Vector3 next = Vector3.MoveTowards(cur, targetFlat, spd * Time.deltaTime);
            Vector3 moveDelta = next - cur; float moveDist = moveDelta.magnitude;
            if (moveDist > 0.0001f)
            {
                if (Physics.Raycast(cur, moveDelta.normalized, out RaycastHit hit, moveDist + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    if (verboseDebug) Debug.Log($"[CarMovement][MoveStraightTo][Hit] collider={hit.collider.name} point={hit.point} dist={hit.distance}");
                    Vector3 stopPoint = hit.point - moveDelta.normalized * stopBeforeObstacle;
                    transform.position = stopPoint;
                    yield return RollbackToStart(startPos, startRot);
                    onComplete?.Invoke();
                    yield break;
                }
            }
            transform.position = next; yield return null;
        }

        transform.position = targetFlat;
        onComplete?.Invoke();
    }

    private IEnumerator MoveToThenRollback(Vector3 bumpPoint, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        float spd = moveSpeedOverride > 0f ? moveSpeedOverride : rollbackSpeed;
        Vector3 target = bumpPoint; target.y = transform.position.y;

        if (moveAlongForward)
        {
            Vector3 forwardFlat = transform.forward; forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = (target - transform.position).normalized; else forwardFlat.Normalize();
            float along = Vector3.Dot(target - transform.position, forwardFlat);
            if (along < 0f) { forwardFlat = -forwardFlat; along = -along; }

            bool wasInside = (parkingArea != null) ? parkingArea.IsInside(transform.position) : false;
            while (along > 0.0004f)
            {
                float step = Mathf.Min(spd * Time.deltaTime, along);
                if (constrainToParkingArea && wasInside && (parkingArea != null) && !parkingArea.IsInside(transform.position)) break;

                if (Physics.Raycast(transform.position, forwardFlat, out RaycastHit hit, step + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 stopPoint = hit.point - forwardFlat * stopBeforeObstacle;
                    transform.position = stopPoint; break;
                }

                transform.position += forwardFlat * step;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forwardFlat, Vector3.up), 12f * Time.deltaTime);
                along -= step; yield return null;
            }

            transform.position = new Vector3(transform.position.x, target.y, transform.position.z);
            if (constrainToParkingArea && (parkingArea != null) && parkingArea.IsInside(transform.position))
            {
                yield return RollbackToStart(startPos, startRot);
                onComplete?.Invoke();
                yield break;
            }
        }

        while ((transform.position - target).sqrMagnitude > 0.0004f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, spd * Time.deltaTime);
            yield return null;
        }

        transform.position = target;
        yield return RollbackToStart(startPos, startRot);
        onComplete?.Invoke();
    }

    private IEnumerator RollbackToStart(Vector3 startPos, Quaternion startRot)
    {
        float spd = rollbackSpeed;
        while ((transform.position - startPos).sqrMagnitude > 0.0004f)
        {
            transform.position = Vector3.MoveTowards(transform.position, startPos, spd * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, startRot, 12f * Time.deltaTime);
            yield return null;
        }
        transform.position = startPos; transform.rotation = startRot;
    }
}
