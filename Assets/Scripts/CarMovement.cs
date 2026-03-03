using System;
using System.Collections;
using UnityEngine;

public class CarMovement : MonoBehaviour
{
    [Header("Movement")]
    public bool moveAlongForward = true;
    public float turnSpeedDeg = 240f;

    [Header("Smoothing")]
    public float acceleration = 6.0f;
    public float deceleration = 9.0f;
    public float slowDownDistance = 2.0f;
    public float straightSlowDownDistance = 1.2f;
    public float arriveDistance = 0.06f;
    public float minCrawlSpeed = 0.35f;
    [Range(5f, 120f)] public float steeringSlowAngle = 65f;
    [Range(0f, 1f)] public float minTurnSpeedFactor = 0.25f;
    [Range(1f, 30f)] public float steeringResponsiveness = 10f;
    [Range(0.03f, 0.5f)] public float speedSmoothTime = 0.12f;
    [Range(0.01f, 0.05f)] public float maxDeltaTime = 0.033f;

    [Header("Physics")]
    public LayerMask blockerMask = ~0;

    [Header("Parking Area")]
    public ParkingArea parkingArea;
    public bool constrainToParkingArea = true;

    [Header("Speeds")]
    public float moveSpeedOverride = 0f;
    public float driveSpeed = 3.5f;
    public float rollbackSpeed = 8f;

    [Header("Collision")]
    public float stopBeforeObstacle = 0.25f;

    [Header("Debug")]
    public bool verboseDebug = false;
    public Color debugColor = Color.red;

    private Coroutine _co;
    private bool _didRollbackInRun;

    public bool DidRollbackInRun => _didRollbackInRun;

    private void StopCurrent()
    {
        if (_co != null) StopCoroutine(_co);
        _co = null;
    }

    // Start deterministic straight move. onComplete called when move finishes (no rollback).
    public void StartMoveStraight(Vector3 dest, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        StopCurrent();
        _didRollbackInRun = false;
        _co = StartCoroutine(MoveStraightTo(dest, startPos, startRot, onComplete));
    }

    // Start move to bump point then rollback. onComplete called when whole flow finishes.
    public void StartMoveToThenRollback(Vector3 bumpPoint, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        StopCurrent();
        _didRollbackInRun = false;
        _co = StartCoroutine(MoveToThenRollback(bumpPoint, startPos, startRot, onComplete));
    }

    private IEnumerator MoveStraightTo(Vector3 dest, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        float maxSpeed = moveSpeedOverride > 0f ? moveSpeedOverride : driveSpeed;
        Vector3 targetFlat = dest;
        targetFlat.y = transform.position.y;

        if (moveAlongForward)
        {
            Vector3 forwardFlat = transform.forward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = (targetFlat - transform.position).normalized;
            else forwardFlat.Normalize();

            float along = Vector3.Dot(targetFlat - transform.position, forwardFlat);
            if (along < 0f)
            {
                forwardFlat = -forwardFlat;
                along = -along;
            }

            bool wasInside = (parkingArea != null) ? parkingArea.IsInside(transform.position) : false;
            float currentSpeed = 0f;

            while (along > arriveDistance)
            {
                float dt = GetStableDeltaTime();
                if (constrainToParkingArea && wasInside && (parkingArea != null) && !parkingArea.IsInside(transform.position)) break;

                float distFactor = Mathf.Clamp01(along / Mathf.Max(0.01f, straightSlowDownDistance));
                float targetSpeed = Mathf.Lerp(minCrawlSpeed, maxSpeed, distFactor);
                float accelRate = targetSpeed > currentSpeed ? acceleration : deceleration;
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * dt);

                float step = Mathf.Min(currentSpeed * dt, along);
                if (step <= 0.0001f)
                {
                    yield return null;
                    continue;
                }

                if (Physics.Raycast(transform.position, forwardFlat, out RaycastHit hit, step + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    if (verboseDebug) Debug.Log($"[CarMovement][MoveStraightTo][Hit] collider={hit.collider.name} point={hit.point} dist={hit.distance}");
                    Vector3 stopPoint = hit.point - forwardFlat * stopBeforeObstacle;
                    transform.position = stopPoint;
                    yield return RollbackToStart(startPos, startRot);
                    onComplete?.Invoke();
                    yield break;
                }

                transform.position += forwardFlat * step;
                float rotLerp = 1f - Mathf.Exp(-steeringResponsiveness * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forwardFlat, Vector3.up), rotLerp);
                along -= step;
                yield return null;
            }

            if (!constrainToParkingArea || (parkingArea == null) || parkingArea.IsInside(transform.position))
            {
                transform.position = targetFlat;
                onComplete?.Invoke();
                yield break;
            }
        }

        // Outside parking area, steer like a car with frame-stable smoothing.
        yield return DriveLikeCarToTarget(targetFlat, maxSpeed, startPos, startRot);
        onComplete?.Invoke();
    }

    private IEnumerator MoveToThenRollback(Vector3 bumpPoint, Vector3 startPos, Quaternion startRot, Action onComplete)
    {
        float maxSpeed = moveSpeedOverride > 0f ? moveSpeedOverride : driveSpeed;
        Vector3 target = bumpPoint;
        target.y = transform.position.y;

        if (moveAlongForward)
        {
            Vector3 forwardFlat = transform.forward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = (target - transform.position).normalized;
            else forwardFlat.Normalize();

            float along = Vector3.Dot(target - transform.position, forwardFlat);
            if (along < 0f)
            {
                forwardFlat = -forwardFlat;
                along = -along;
            }

            bool wasInside = (parkingArea != null) ? parkingArea.IsInside(transform.position) : false;
            float currentSpeed = 0f;

            while (along > arriveDistance)
            {
                float dt = GetStableDeltaTime();
                if (constrainToParkingArea && wasInside && (parkingArea != null) && !parkingArea.IsInside(transform.position)) break;

                float distFactor = Mathf.Clamp01(along / Mathf.Max(0.01f, straightSlowDownDistance));
                float targetSpeed = Mathf.Lerp(minCrawlSpeed, maxSpeed, distFactor);
                float accelRate = targetSpeed > currentSpeed ? acceleration : deceleration;
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * dt);

                float step = Mathf.Min(currentSpeed * dt, along);
                if (step <= 0.0001f)
                {
                    yield return null;
                    continue;
                }

                if (Physics.Raycast(transform.position, forwardFlat, out RaycastHit hit, step + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 stopPoint = hit.point - forwardFlat * stopBeforeObstacle;
                    transform.position = stopPoint;
                    break;
                }

                transform.position += forwardFlat * step;
                float rotLerp = 1f - Mathf.Exp(-steeringResponsiveness * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forwardFlat, Vector3.up), rotLerp);
                along -= step;
                yield return null;
            }

            transform.position = new Vector3(transform.position.x, target.y, transform.position.z);
            if (constrainToParkingArea && (parkingArea != null) && parkingArea.IsInside(transform.position))
            {
                yield return RollbackToStart(startPos, startRot);
                onComplete?.Invoke();
                yield break;
            }
        }

        yield return DriveLikeCarToTarget(target, maxSpeed, startPos, startRot);
        yield return RollbackToStart(startPos, startRot);
        onComplete?.Invoke();
    }

    private IEnumerator DriveLikeCarToTarget(Vector3 target, float maxSpeed, Vector3 startPos, Quaternion startRot)
    {
        float currentSpeed = 0f;
        float speedVelocity = 0f;
        float stopSqr = arriveDistance * arriveDistance;

        while ((transform.position - target).sqrMagnitude > stopSqr)
        {
            float dt = GetStableDeltaTime();

            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float remain = toTarget.magnitude;
            if (remain <= arriveDistance) break;

            Vector3 desiredDir = toTarget / Mathf.Max(0.0001f, remain);

            Vector3 forwardFlat = transform.forward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = desiredDir;
            else forwardFlat.Normalize();

            float steerLerp = 1f - Mathf.Exp(-steeringResponsiveness * dt);
            Vector3 steerDir = Vector3.Slerp(forwardFlat, desiredDir, steerLerp);
            if (steerDir.sqrMagnitude < 1e-6f) steerDir = desiredDir;
            steerDir.Normalize();

            Quaternion desiredRot = Quaternion.LookRotation(steerDir, Vector3.up);
            float distTurnFactor = Mathf.Clamp01(remain / Mathf.Max(0.01f, slowDownDistance));
            float dynamicTurnSpeed = Mathf.Lerp(turnSpeedDeg * 0.45f, turnSpeedDeg, distTurnFactor);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, dynamicTurnSpeed * dt);

            forwardFlat = transform.forward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-6f) forwardFlat = steerDir;
            else forwardFlat.Normalize();

            float angle = Mathf.Abs(Vector3.SignedAngle(forwardFlat, desiredDir, Vector3.up));
            float angleFactor = Mathf.Clamp01(1f - (angle / Mathf.Max(1f, steeringSlowAngle)));
            angleFactor = Mathf.Lerp(minTurnSpeedFactor, 1f, angleFactor);

            float distFactor = Mathf.Clamp01(remain / Mathf.Max(0.01f, slowDownDistance));
            float speedFactor = Mathf.Min(distFactor, angleFactor);
            float targetSpeed = Mathf.Lerp(minCrawlSpeed, maxSpeed, speedFactor);

            currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedVelocity, speedSmoothTime, Mathf.Infinity, dt);

            float step = Mathf.Min(currentSpeed * dt, remain);
            if (step <= 0.0001f)
            {
                yield return null;
                continue;
            }

            if (Physics.Raycast(transform.position, forwardFlat, out RaycastHit hit, step + 0.01f, blockerMask, QueryTriggerInteraction.Ignore))
            {
                if (verboseDebug) Debug.Log($"[CarMovement][DriveLikeCarToTarget][Hit] collider={hit.collider.name} point={hit.point} dist={hit.distance}");
                Vector3 stopPoint = hit.point - forwardFlat * stopBeforeObstacle;
                transform.position = stopPoint;
                yield return RollbackToStart(startPos, startRot);
                yield break;
            }

            transform.position += forwardFlat * step;
            yield return null;
        }

        // Ease-in final centimeters to avoid visible position pop.
        float settle = 0f;
        while ((transform.position - target).sqrMagnitude > 0.0004f && settle < 0.2f)
        {
            float dt = GetStableDeltaTime();
            float settleSpeed = Mathf.Max(minCrawlSpeed, 0.5f);
            transform.position = Vector3.MoveTowards(transform.position, target, settleSpeed * dt);
            settle += dt;
            yield return null;
        }

        transform.position = target;
    }

    private IEnumerator RollbackToStart(Vector3 startPos, Quaternion startRot)
    {
        _didRollbackInRun = true;

        float spd = rollbackSpeed;
        while ((transform.position - startPos).sqrMagnitude > 0.0004f)
        {
            float dt = GetStableDeltaTime();
            transform.position = Vector3.MoveTowards(transform.position, startPos, spd * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, startRot, 12f * dt);
            yield return null;
        }

        transform.position = startPos;
        transform.rotation = startRot;
    }

    private float GetStableDeltaTime()
    {
        float dt = Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : Time.deltaTime;
        return Mathf.Clamp(dt, 0.0001f, maxDeltaTime);
    }
}
