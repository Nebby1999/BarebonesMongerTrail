
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct TrailPointLifetimeJob : IJobParallelFor
{
    public float deltaTime;
    public NativeArray<MongerManager_Jobbified.TarPoint> tarPoints;

    public void Execute(int index)
    {
        var point = tarPoints[index];
        if (point.Equals(MongerManager_Jobbified.TarPoint.invalid))
            return;

        point.pointLifetime -= deltaTime;
        tarPoints[index] = point;
    }
}