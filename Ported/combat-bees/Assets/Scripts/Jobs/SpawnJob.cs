﻿using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

[BurstCompile]
struct SpawnJob : IJobParallelFor
{
    // A regular EntityCommandBuffer cannot be used in parallel, a ParallelWriter has to be explicitly used.
    public EntityCommandBuffer.ParallelWriter ECB;
    
    public Entity Prefab;

    public AABB Aabb;
    
    public LocalToWorldTransform InitTransform;

    public int InitFaction;
    public void Execute(int index)
    {
        var entity = ECB.Instantiate(index, Prefab);
        var random = Random.CreateFromIndex((uint) index);

        var randomf3 = (random.NextFloat3() - new float3(0.5f, 0.5f, 0.5f)) * 2.0f; // [-1;1]

        float3 position = Aabb.Center + Aabb.Extents * randomf3;

        ECB.SetComponentEnabled<Dead>(index, entity, true);
        ECB.SetComponent(index, entity, new LocalToWorldTransform{Value = UniformScaleTransform.FromPositionRotationScale(position, InitTransform.Value.Rotation, InitTransform.Value.Scale)});
        ECB.AddSharedComponent(index, entity, new Faction{Value = InitFaction});
    }
}