using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct WriteBoxcastCommandsJob : IJobParallelFor
{
    public PhysicsScene physicsScene;
    public LayerMask physicsCheckMask;
    public NativeArray<MongerManager_Jobbified.TarPoint> tarPoints;
    public NativeArray<BoxcastCommand> output;
    public void Execute(int index)
    {
        var tarPoint = tarPoints[index];

        var xy = tarPoint.pointWidthDepth * tarPoint.remappedLifetime0to1;
        var command = new BoxcastCommand(physicsScene, tarPoint.worldPosition, new float3(xy.x / 2, 0.5f, xy.y / 2), tarPoint.rotation, tarPoint.normalDirection, 1, physicsCheckMask);

        output[index] = command;
    }
}