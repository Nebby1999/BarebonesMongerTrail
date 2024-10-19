
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct FilterManagerIndicesThatDidntCollideWithAnythingJob : IJobParallelFor
{
    [ReadOnly]
    public NativeList<MongerManager_Jobbified.ManagerIndex> input;
    public NativeArray<RaycastHit> raycastHits;
    [WriteOnly]
    public NativeList<MongerManager_Jobbified.ManagerIndex>.ParallelWriter output;

    public void Execute(int index)
    {
        if (raycastHits[index].colliderInstanceID != 0)
        {
            output.AddNoResize(input[index]);
        }
    }
}