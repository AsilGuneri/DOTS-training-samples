using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Profiling;

public partial struct ObstacleSpawnerSystem : ISystem
{
    public DynamicBuffer<ObstacleArcPrimitive> ObstaclePrimtitveBuffer;

    static readonly ProfilerMarker k_ProfileMarker_GenerateObstacles = new ProfilerMarker("Obstacle: GenerateObstacles");
    static readonly ProfilerMarker k_ProfileMarker_CalculateRayCollision = new ProfilerMarker("Obstacle: CalculateRayCollision");

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ObstacleSpawnerExecution>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
        GenerateObstacles(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void GenerateObstacles(ref SystemState state)
    {
        k_ProfileMarker_GenerateObstacles.Begin();

        var ecbSystemSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSystemSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var ObstacleEntity = ecb.CreateEntity();
        
#if UNITY_EDITOR
        ecb.SetName(ObstacleEntity, $"__Buffer_ObstacleArcPrimitive");
#endif
        
        ObstaclePrimtitveBuffer = ecb.AddBuffer<ObstacleArcPrimitive>(ObstacleEntity);

        Vector2 MapCenter = new Vector2(0.5f, 0.5f);

        foreach (var spawner in SystemAPI.Query<RefRO<ObstacleSpawner>>())
        {
            int iObstacleRingCount = spawner.ValueRO.ObstacleRingCount;
            float fObstaclePercentPerRing = spawner.ValueRO.ObstaclePercentPerRing;
            float fObstacleRadius = spawner.ValueRO.ObstacleRadius;

            for (int i = 1; i <= iObstacleRingCount; i++)
            {
                int holeCount = UnityEngine.Random.Range(1, 3);

                // float ringRadius = (i / (iObstacleRingCount + 1f)) * (spawner.MapSize * .5f);
                float ringRadius = (i / (iObstacleRingCount + 1f)) * (0.5f);
                float ringAngleStart = UnityEngine.Random.Range(0.0f, Mathf.PI);
                float ringAngleEndOffset = (2.0f * Mathf.PI / holeCount);

                // float ringArcLength = 2.0f * Mathf.PI * ringRadius / (float)holeCount;            

                for (int r = 0; r < holeCount; r++)
                {
                    ObstacleArcPrimitive PrototypeObstaclePrim = new ObstacleArcPrimitive();
                    PrototypeObstaclePrim.Position = MapCenter;
                    PrototypeObstaclePrim.Radius = ringRadius;
                    // Create angular start/end extents
                    PrototypeObstaclePrim.AngleStart = ringAngleStart;
                    PrototypeObstaclePrim.AngleEnd = ringAngleStart + ringAngleEndOffset * fObstaclePercentPerRing;
                    if (PrototypeObstaclePrim.AngleEnd > 2.0f * Mathf.PI) PrototypeObstaclePrim.AngleEnd -= 2.0f * Mathf.PI;

                    PrototypeObstaclePrim.AngleRange = ((PrototypeObstaclePrim.AngleStart < PrototypeObstaclePrim.AngleEnd) ? (PrototypeObstaclePrim.AngleEnd - PrototypeObstaclePrim.AngleStart) : (PrototypeObstaclePrim.AngleEnd + 2.0f * Mathf.PI - PrototypeObstaclePrim.AngleStart));

                    // Actually spawn the collision objects
                    ObstaclePrimtitveBuffer.Add(PrototypeObstaclePrim);                    

                    ringAngleStart += ringAngleEndOffset;
                }
            }
        }

        k_ProfileMarker_GenerateObstacles.End();
    }

    public static bool CalculateRayCollision(in NativeArray<ObstacleArcPrimitive> ObstaclePrimtitveBuffer, in float2 point, in float2 direction, out float2 CollisionPoint, out float Param)
    {
        k_ProfileMarker_CalculateRayCollision.Begin();

        Param = 1000000000.0f;
        int PrimIndex = -1;
        for( int i = 0; i < ObstaclePrimtitveBuffer.Length; i++ )
        {
            ObstacleArcPrimitive prim = ObstaclePrimtitveBuffer[i];
            float2 VectorFromCenterToPoint = point - prim.Position;
            float a = math.dot(direction, direction);
            float b = 2.0f * math.dot(direction, VectorFromCenterToPoint);
            float c = math.dot(VectorFromCenterToPoint, VectorFromCenterToPoint) - prim.Radius * prim.Radius;
            // Solve the quadratic equation
            float discriminant = b * b - 4.0f * a * c;
            if (0 > discriminant)
            {
                CollisionPoint = point;
                continue;
            }
            discriminant = Mathf.Sqrt(discriminant);
            float t1 = (-b - discriminant) / (2.0f * a);
            float t2 = (-b + discriminant) / (2.0f * a);
            // Calculate the smallest positive T value
            float t = 0.0f;
            if ((0.0f > t1) && (0.0f > t2)) continue;
            if ((0.0f <= t1) && (0.0f <= t2))
            {
                t = Mathf.Min(t1, t2);
            }
            else if (0.0f <= t1)
            {
                t = t1;
            }
            else if (0.0f <= t2)
            {
                t = t2;
            }
            // See if this collision is the closest
            if (t < Param)
            {
                if ((0.0f <= t) && (1.0f >= t))
                {
                    float2 TestCollPoint = point + t * direction;
                    float2 CollisionDirection = TestCollPoint - prim.Position;
                    float Angle = Mathf.Atan2(CollisionDirection.y, CollisionDirection.x);
                    float AngleStart = prim.AngleStart;
                    float AngleEnd = prim.AngleEnd;
                    if (prim.AngleEnd < prim.AngleStart)
                    {
                        AngleEnd += Mathf.PI * 2.0f;
                    }
                    if (prim.AngleStart > Angle)
                    {
                        Angle += Mathf.PI * 2.0f;
                    }
                    if ((Angle >= AngleStart) && (Angle <= AngleEnd))
                    {
                        Param = t;
                        PrimIndex = i;
                    }
                }
            }
        }
        bool ReturnValue = false;
        if (-1 == PrimIndex)
        {
            CollisionPoint = point;
            ReturnValue = false;
        }
        else
        {
            CollisionPoint = point + Param * direction;
            ReturnValue = true;
        }

        k_ProfileMarker_CalculateRayCollision.End();
        return ReturnValue;
    }
}

public struct ObstacleSpawner : IComponentData
{
    public int ObstacleRingCount;
    public Entity Prefab;

    public float ObstaclePercentPerRing;
    public float ObstacleRadius;
}

public struct ObstacleArcPrimitive : IBufferElementData
{
    public float2 Position;
    public float Radius;
    public float AngleStart;
    public float AngleEnd;
    public float AngleRange;

    public float iAnglePerObstacle;
    public int iNumObstacles;
}

