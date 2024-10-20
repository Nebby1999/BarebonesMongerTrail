using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct KillPointsJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<MongerManager_Jobbified.TarPoint> points;
    [WriteOnly]
    public NativeList<MongerManager_Jobbified.TarPoint>.ParallelWriter pointsToKill;

    public void Execute(int index)
    {
        var point = points[index];
        if(point.pointLifetime <= 0)
        {
            pointsToKill.AddNoResize(point);
        }
    }
}