using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

public struct TrailPointVisualJob : IJobParallelForTransform
{
    public float totalLifetime;
    public float deltaTime;
    public NativeArray<float> pointLifetimes;
    public void Execute(int index, TransformAccess transform)
    {
        if (!transform.isValid)
            return;

        var lifetime = pointLifetimes[index];
        lifetime -= deltaTime;
        transform.localScale = math.lerp(float3.zero, new float3(1f), Util.Remap(lifetime, 0, totalLifetime, 0, 1));
        pointLifetimes[index] = lifetime;
    }
}