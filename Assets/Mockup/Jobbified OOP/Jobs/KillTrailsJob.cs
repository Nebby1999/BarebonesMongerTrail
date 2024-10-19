using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public struct KillTrailsJob : IJobParallelFor
{
    public NativeArray<MongerManager_Jobbified.TarPoint> points;
    public NativeList<MongerManager_Jobbified.ManagerIndex>.ParallelWriter indicesToKill;

    public void Execute(int index)
    {
        var point = points[index];
        if(point.pointLifetime <= 0)
        {
            indicesToKill.AddNoResize(point.managerIndex);
        }
    }
}