using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.Jobs;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct ReturnKilledPointsJob : IJobParallelForDefer
{
    [WriteOnly, NativeDisableParallelForRestriction]
    public NativeArray<MongerManager_Jobbified.TarPoint> allPoints;
    [ReadOnly]
    public NativeArray<MongerManager_Jobbified.TarPoint> killedPointsJob;
    public NativeQueue<int>.ParallelWriter invalidPointIndices;
    public void Execute(int index)
    {
        var killedPoint = killedPointsJob[index];
        int managerIndex = (int)killedPoint.managerIndex;
        invalidPointIndices.Enqueue((int)killedPoint.managerIndex);
        allPoints[managerIndex] = MongerManager_Jobbified.TarPoint.invalid;
    }
}