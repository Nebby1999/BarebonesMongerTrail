using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Video;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct GetActiveTarPointsJob : IJobParallelFor
{
    public NativeArray<MongerManager_Jobbified.TarPoint> allTarPoints;
    public NativeList<MongerManager_Jobbified.ManagerIndex>.ParallelWriter activePoints;
    public void Execute(int index)
    {
        var point = allTarPoints[index];
        if (point.isValid)
        {
            activePoints.AddNoResize(point.managerIndex);
        }
    }
}