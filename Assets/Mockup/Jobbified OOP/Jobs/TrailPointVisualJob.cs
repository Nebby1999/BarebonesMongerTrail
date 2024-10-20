using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct TrailPointVisualJob : IJobParallelForTransform
{
    public float totalLifetime;
    public float3 maxSize;
    [ReadOnly]
    public NativeArray<MongerManager_Jobbified.TarPoint> tarPoints;
    [ReadOnly]
    public NativeList<MongerManager_Jobbified.ManagerIndex> activeTarPoints;

    public void Execute(int index, TransformAccess transform)
    {
        //Geet the manager index, which we use to get the actual point to modify.
        var managerIndex = (int)activeTarPoints[index];
        var point = tarPoints[managerIndex];

        //If transform is not valid, it means its from one that isnt active. i dont think this should happen?
        if(!point.isValid || !transform.isValid)
        {
            return;
        }

        transform.localScale = math.lerp(float3.zero, maxSize, point.remappedLifetime0to1);
    }
}