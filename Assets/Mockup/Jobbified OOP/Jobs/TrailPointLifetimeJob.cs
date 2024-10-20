
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct TrailPointLifetimeJob : IJobParallelFor
{
    public float deltaTime;
    public NativeArray<MongerManager_Jobbified.TarPoint> tarPoints;
    [ReadOnly]
    public NativeList<MongerManager_Jobbified.ManagerIndex> activeTarPoints;

    public void Execute(int index)
    {
        var managerIndex = (int)activeTarPoints[index];
        var point = tarPoints[managerIndex];

        point.pointLifetime -= deltaTime;
        point.remappedLifetime0to1 = math.remap(0f, point.totalLifetime, 0, 1, point.pointLifetime);
        tarPoints[managerIndex] = point;
    }
}