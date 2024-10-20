using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct WriteRaycastCommandsJob : IJobParallelForTransform
{
    public float raycastLength;
    public PhysicsScene physicsScene;
    public LayerMask raycastMask;
    public NativeArray<RaycastCommand> output;

    public void Execute(int index, TransformAccess access)
    {
        output[index] = new RaycastCommand(physicsScene, access.position, math.down(), raycastLength, raycastMask, 1);
    }
}