using Unity.Entities;
using Unity.Burst;
using Unity.Transforms;
using System.Diagnostics;
using Unity.Rendering;
using Unity.Mathematics;
using System;
using Random = Unity.Mathematics.Random;
using UnityEngine;
using static UnityEditor.PlayerSettings;
using Unity.Collections;
using UnityEditor.PackageManager;

[BurstCompile]
public partial struct WorldGenerationSystem : ISystem
{
    Random random;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        random = Random.CreateFromIndex(1234);
        state.RequireForUpdate<WorldGrid>();
        state.RequireForUpdate<Config>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<Config>();
        var worldGrid = SystemAPI.GetSingleton<WorldGrid>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        worldGrid.typeGrid = new NativeArray<byte>(worldGrid.gridSize.x * worldGrid.gridSize.y, Allocator.Temp);
        worldGrid.entityGrid = new NativeArray<Entity>(worldGrid.gridSize.x * worldGrid.gridSize.y, Allocator.Temp);

        int width = worldGrid.gridSize.x;
        int height = worldGrid.gridSize.y;
        float rockNoiseThreshold = 0.2f;
        float safeZone = config.safeZoneRadius * config.safeZoneRadius;

        float maxNoiseVal = -math.INFINITY;
        float minNoiseVal = math.INFINITY;

        for(int x = 0;x< width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float3 gridWorldPos = worldGrid.GridToWorld(x, y);
                if (math.lengthsq(gridWorldPos) < safeZone) continue;

                var pos = new float2(x, y);
                float noiseVal = noise.cnoise(pos / 10f);

                if (noiseVal < minNoiseVal) minNoiseVal = noiseVal;
                if (noiseVal > maxNoiseVal) maxNoiseVal = noiseVal;

                float remappedNoise = math.remap(rockNoiseThreshold, 1.0f, 20, 100, noiseVal);

                if (noiseVal > rockNoiseThreshold)
                {
                    worldGrid.SetTypeAt(x, y,Rock.type);
                    //Create it
                    Entity rock = state.EntityManager.Instantiate(config.RockPrefab);
                    RockAspect rAspect = state.EntityManager.GetAspect<RockAspect>(rock);
                    rAspect.Health = (int)remappedNoise;
                    rAspect.Transform.LocalPosition = gridWorldPos;
                    worldGrid.SetEntityAt(x,y,rock);
                }
            }
        }

        


        state.Enabled = false;

    }

}

