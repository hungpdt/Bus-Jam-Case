using System.Collections.Generic;
using UnityEngine;

public class ParkingSlotManager : MonoBehaviour
{
    [System.Serializable]
    public class ParkingSlot
    {
        public string slotName = "Slot";
        public Transform anchor;
        public Vector2 size = new Vector2(2.5f, 5f); // X = width, Y = length (Z)
        public bool isLocked;

        [HideInInspector] public bool isOccupied;
        [HideInInspector] public CarDriveController occupant;
    }

    [Header("Slots")]
    [Range(1, 5)] public int maxSlots = 5;
    public List<ParkingSlot> slots = new List<ParkingSlot>(5);
    public float edgePadding = 0.05f;

    [Header("Debug")]
    public bool verboseDebug = false;
    public Color openColor = new Color(0f, 1f, 0f, 0.22f);
    public Color lockedColor = new Color(1f, 0f, 0f, 0.22f);
    public Color occupiedColor = new Color(1f, 0.6f, 0f, 0.28f);

    public int Capacity => Mathf.Min(maxSlots, slots.Count);

    private void OnValidate()
    {
        maxSlots = Mathf.Clamp(maxSlots, 1, 5);

        if (slots == null) slots = new List<ParkingSlot>(5);
        if (slots.Count > 5) slots.RemoveRange(5, slots.Count - 5);

        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].size.x = Mathf.Max(0.1f, slots[i].size.x);
            slots[i].size.y = Mathf.Max(0.1f, slots[i].size.y);
        }

        edgePadding = Mathf.Max(0f, edgePadding);
    }

    public bool TryReserveNearestSlot(CarDriveController car, Vector3 fromPos, out int slotIndex, out Vector3 parkPos, out Quaternion parkRot)
    {
        slotIndex = -1;
        parkPos = Vector3.zero;
        parkRot = Quaternion.identity;

        int capacity = Capacity;
        float bestDistSqr = float.MaxValue;

        for (int i = 0; i < capacity; i++)
        {
            ParkingSlot slot = slots[i];
            if (slot == null || slot.anchor == null) continue;
            if (slot.isLocked || slot.isOccupied) continue;
            if (!CanCarFitInSlot(car, slot)) continue;

            float d = (slot.anchor.position - fromPos).sqrMagnitude;
            if (d < bestDistSqr)
            {
                bestDistSqr = d;
                slotIndex = i;
            }
        }

        if (slotIndex < 0)
        {
            return false;
        }

        ParkingSlot chosen = slots[slotIndex];
        chosen.isOccupied = true;
        chosen.occupant = car;

        parkPos = GetClampedPointInsideSlot(slotIndex, chosen.anchor.position);
        parkRot = chosen.anchor.rotation;

        if (verboseDebug)
        {
            Debug.Log($"[ParkingSlotManager] Reserved slot {slotIndex} for {(car != null ? car.name : "null")}");
        }

        return true;
    }

    public bool ConfirmParked(CarDriveController car, int slotIndex, out Vector3 snappedPos, out Quaternion snappedRot)
    {
        snappedPos = Vector3.zero;
        snappedRot = Quaternion.identity;

        if (!IsValidSlotIndex(slotIndex)) return false;

        ParkingSlot slot = slots[slotIndex];
        if (slot == null || slot.anchor == null) return false;

        slot.isOccupied = true;
        slot.occupant = car;

        snappedPos = GetClampedPointInsideSlot(slotIndex, slot.anchor.position);
        snappedRot = slot.anchor.rotation;

        return true;
    }

    public bool ReleaseSlot(CarDriveController car, int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex)) return false;

        ParkingSlot slot = slots[slotIndex];
        if (slot == null) return false;

        if (slot.occupant != null && car != null && slot.occupant != car)
        {
            return false;
        }

        slot.isOccupied = false;
        slot.occupant = null;

        if (verboseDebug)
        {
            Debug.Log($"[ParkingSlotManager] Released slot {slotIndex}");
        }

        return true;
    }

    public Vector3 GetClampedPointInsideSlot(int slotIndex, Vector3 worldPoint)
    {
        if (!IsValidSlotIndex(slotIndex)) return worldPoint;

        ParkingSlot slot = slots[slotIndex];
        if (slot == null || slot.anchor == null) return worldPoint;

        Vector3 local = slot.anchor.InverseTransformPoint(worldPoint);
        float halfX = slot.size.x * 0.5f;
        float halfZ = slot.size.y * 0.5f;

        float padX = Mathf.Min(edgePadding, Mathf.Max(0f, halfX - 0.001f));
        float padZ = Mathf.Min(edgePadding, Mathf.Max(0f, halfZ - 0.001f));

        local.x = Mathf.Clamp(local.x, -halfX + padX, halfX - padX);
        local.z = Mathf.Clamp(local.z, -halfZ + padZ, halfZ - padZ);

        return slot.anchor.TransformPoint(local);
    }

    public bool HasFreeUnlockedSlot()
    {
        int capacity = Capacity;
        for (int i = 0; i < capacity; i++)
        {
            ParkingSlot slot = slots[i];
            if (slot == null || slot.anchor == null) continue;
            if (!slot.isLocked && !slot.isOccupied) return true;
        }

        return false;
    }

    private bool CanCarFitInSlot(CarDriveController car, ParkingSlot slot)
    {
        if (slot == null) return false;
        if (car == null) return true;

        if (!TryGetCarFootprint(car, out float carWidth, out float carLength))
        {
            return true;
        }

        float requiredWidth = carWidth + edgePadding * 2f;
        float requiredLength = carLength + edgePadding * 2f;

        return slot.size.x >= requiredWidth && slot.size.y >= requiredLength;
    }

    private bool TryGetCarFootprint(CarDriveController car, out float width, out float length)
    {
        width = 0f;
        length = 0f;

        if (car == null) return false;

        Bounds bounds = new Bounds(car.transform.position, Vector3.zero);
        bool found = false;

        Collider[] colliders = car.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!colliders[i].enabled) continue;

            if (!found)
            {
                bounds = colliders[i].bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
        }

        if (!found)
        {
            Renderer[] renderers = car.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled) continue;

                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
        }

        if (!found) return false;

        float sx = bounds.size.x;
        float sz = bounds.size.z;
        width = Mathf.Min(sx, sz);
        length = Mathf.Max(sx, sz);
        return true;
    }

    private bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < Capacity;
    }

    private void OnDrawGizmosSelected()
    {
        int capacity = Capacity;

        for (int i = 0; i < capacity; i++)
        {
            ParkingSlot slot = slots[i];
            if (slot == null || slot.anchor == null) continue;

            Vector3 center = slot.anchor.position;
            Vector3 size = new Vector3(slot.size.x, 0.02f, slot.size.y);

            Gizmos.color = slot.isLocked ? lockedColor : (slot.isOccupied ? occupiedColor : openColor);
            Gizmos.matrix = Matrix4x4.TRS(center, slot.anchor.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, size);

            Gizmos.color = slot.isLocked ? Color.red : (slot.isOccupied ? new Color(1f, 0.5f, 0f) : Color.green);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * (slot.size.y * 0.5f));
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}
