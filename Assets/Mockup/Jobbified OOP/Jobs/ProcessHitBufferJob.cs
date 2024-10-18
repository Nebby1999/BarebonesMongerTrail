using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public struct ProcessHitBufferJob : IJobParallelFor
{
    public int numberOfRaycastToProcess;

    [ReadOnly]
    public NativeArray<RaycastHit> raycastHits;

    [ReadOnly]
    public NativeHashMap<int, int> colliderIDToMockupMovementID;

    public NativeArray<int> output;

    public void Execute(int index)
    {
        int startIndex = index * numberOfRaycastToProcess;
        for(int num = 0; num < numberOfRaycastToProcess; num++)
        {
            int raycastIndex = startIndex + num;
            var hit = raycastHits[raycastIndex];

            if (hit.colliderInstanceID == 0)//the hit itself is null collider, so break early since it means the other entries are garbage data.
                break;

            if(!colliderIDToMockupMovementID.TryGetValue(hit.colliderInstanceID, out var mockupMovementID))
            {
                continue;
            }

            output[raycastIndex] = mockupMovementID;
        }
    }
}