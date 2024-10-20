using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct UpdateFromManagerJob : IJobFor
{
    [ReadOnly]
    public NativeList<MongerManager_Jobbified.TarPoint> allTarPoints;
    public NativeArray<MongerManager_Jobbified.TarPoint> myTarPoints;
    public void Execute(int index)
    {
        var myPoint = myTarPoints[index];
        myTarPoints[index] = allTarPoints[(int)myPoint.managerIndex];
    }
}