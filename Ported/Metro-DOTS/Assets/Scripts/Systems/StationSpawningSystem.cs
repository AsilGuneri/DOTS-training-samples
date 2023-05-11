using Metro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial struct StationSpawningSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StationConfig>();
    }

    public void OnDestroy(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        var stationConfig = SystemAPI.GetSingleton<StationConfig>();
        var stations = CollectionHelper.CreateNativeArray<Entity>(stationConfig.NumStations, Allocator.Temp);

        var em = state.EntityManager;
        em.Instantiate(stationConfig.StationEntity, stations);

        // TODO Make random posiioning work properly
        var random = Random.CreateFromIndex(12314);

        float accumulatedValue = 0;
        foreach (var transform in
            SystemAPI.Query<RefRW<LocalTransform>>()
            .WithAll<StationIDComponent>())
        {
            /*
            var val = random.NextFloat();
            float3 offset = new float3(val * stationConfig.Spacing, 0, 0);

            transform.ValueRW.Position = new float3(i * stationConfig.Spacing, 0, 0) + offset;
            i++;
            */

            var val = random.NextFloat();
            accumulatedValue += (math.max(val * stationConfig.Spacing, 10)) + stationConfig.StationWidth;// + width of the platform
            var offset = new float3(accumulatedValue, 0, 0);
            transform.ValueRW.Position = offset;
        }

        var trackArchetype = em.CreateArchetype(typeof(TrackIDComponent), typeof(Track));
        var trackEntityA = em.CreateEntity(trackArchetype);
        var trackEntityB = em.CreateEntity(trackArchetype);
#if UNITY_EDITOR
        em.SetName(trackEntityA, "TrackEntityA");
        em.SetName(trackEntityB, "TrackEntityB");
#endif
        var TrackPointBufferA = em.AddBuffer<TrackPoint>(trackEntityA);
        var TrackPointBufferB = em.AddBuffer<TrackPoint>(trackEntityB);
        TrackPointBufferA = em.GetBuffer<TrackPoint>(trackEntityA);

        int i = 0;
        var halfStationLength = 20;
        float3 trackPointOffsetFromCenter = new float3(halfStationLength, 0, 0);
        foreach (var (transform, stationEntity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
            .WithAll<StationIDComponent>()
            .WithEntityAccess())
        {
            bool isEnd = i == 0 || i == stationConfig.NumStations - 1;
            TrackPointBufferA.Add(new TrackPoint { Position = stationConfig.TrackACenter + transform.ValueRO.Position - trackPointOffsetFromCenter, Station = Entity.Null });
            TrackPointBufferA.Add(new TrackPoint { Position = stationConfig.TrackACenter + transform.ValueRO.Position, IsEnd = isEnd, IsStation = true, Station = stationEntity});
            TrackPointBufferA.Add(new TrackPoint { Position = stationConfig.TrackACenter + transform.ValueRO.Position + trackPointOffsetFromCenter, Station = Entity.Null  });
            
            TrackPointBufferB.Add(new TrackPoint { Position = stationConfig.TrackBCenter + transform.ValueRO.Position - trackPointOffsetFromCenter, Station = Entity.Null  });
            TrackPointBufferB.Add(new TrackPoint { Position = stationConfig.TrackBCenter + transform.ValueRO.Position, IsEnd = isEnd, IsStation = true, Station = stationEntity });
            TrackPointBufferB.Add(new TrackPoint { Position = stationConfig.TrackBCenter + transform.ValueRO.Position + trackPointOffsetFromCenter, Station = Entity.Null  });
            i++;
        }

        float sleeperSpacing = 1;

        foreach (var trackBuffer in new[] { TrackPointBufferA, TrackPointBufferB })
        {
            for (int j = 0; j < trackBuffer.Length - 1; j++)
            {
                float distance = math.distance(trackBuffer[j].Position, trackBuffer[j + 1].Position);
                var numberOfSleepers = (int)(distance / sleeperSpacing);

                var sleepers = CollectionHelper.CreateNativeArray<Entity>(numberOfSleepers, Allocator.Temp);
                em.Instantiate(stationConfig.TrackEntity, sleepers);

                for (int k = 0; k < sleepers.Length; k++)
                {
                    float3 pos = math.lerp(trackBuffer[j].Position, trackBuffer[j + 1].Position, (float)k / sleepers.Length);
                    LocalTransform lc = new LocalTransform
                    {
                        Position = pos,
                        Scale = 1f,
                        Rotation = quaternion.RotateY(math.PI / 2)
                    };
                    em.SetComponentData<LocalTransform>(sleepers[k], lc);
                }
            }   
        }

        // TODO Add random spawn points between 3-5 for same number of carriadges
        // var random = Random.CreateFromIndex(12314);
        // var val = random.NextFloat();
        int numQueuePoints = stationConfig.NumStations * (stationConfig.NumQueingPoints /** 2*/);
        var queuePoints = CollectionHelper.CreateNativeArray<Entity>(numQueuePoints, Allocator.Temp);

        EntityArchetype queueArchetype = em.CreateArchetype(typeof(QueueComponent), typeof(LocalTransform));
#if UNITY_EDITOR
        // todo: make naming work
        for (var k = 0; k < queuePoints.Length; k++)
        {
            em.SetName(queuePoints[k], "QueueEntity");   
        }
#endif
        em.CreateEntity(queueArchetype, queuePoints);

        float carriageLength = 5;

        i = 0;
        foreach (var transform in
            SystemAPI.Query<RefRO<LocalTransform>>()
            .WithAll<StationIDComponent>())
        {
            for (int k = 0; k < stationConfig.NumQueingPoints; k++)
            {
                LocalTransform lc = new LocalTransform();
                float totalQueuePointsSpan = (carriageLength * (stationConfig.NumQueingPoints - 1)) / 2;
                lc.Position = stationConfig.TrackACenter + stationConfig.SpawnPointOffsetFromCenterPoint + transform.ValueRO.Position - new float3(totalQueuePointsSpan, 0, 0) + new float3(k * carriageLength, 0, 0);
                lc.Scale = 1;
                var queuePointIndex = (i * stationConfig.NumQueingPoints) + k;
                em.SetComponentData<LocalTransform>(queuePoints[queuePointIndex], lc);
            }

            i++;
            //for (int k = 0; k < stationConfig.NumCarriadges; k++)
            //{
            //    // make the x negative
            //}
        }

        // var tracks = CollectionHelper.CreateNativeArray<Entity>(TrackPointBuffer.Length, Allocator.Temp);
        // em.Instantiate(stationConfig.TrackEntity, tracks);

        // i = 0;
        // foreach (var transform in
        //     SystemAPI.Query<RefRW<LocalTransform>>()
        //     .WithAll<SleeperTag>())
        // {
        //     transform.ValueRW.Position = TrackPointBuffer[i].Position;
        //     transform.ValueRW.Rotation = quaternion.RotateY(math.PI / 2);
        //     i++;
        // }


        /*
        var query = SystemAPI.QueryBuilder().WithAll<StationConfig>().Build();
        var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        var localTransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>();
        foreach (var chunk in chunks)
        {
            var localTransformVals = chunk.GetNativeArray(localTransformHandle);
            for (int j = 0; j < chunk.Count; j++)
            {
                var v = localTransformVals[j];
            }
        }
        */
        
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var query = SystemAPI.QueryBuilder().WithAll<URPMaterialPropertyBaseColor>().Build();
        var queryMask = query.GetEntityQueryMask();
        
        // This system will only run once, so the random seed can be hard-coded.
        // Using an arbitrary constant seed makes the behavior deterministic.
        var hue = random.NextFloat();

        // Set the color of the station
        // Helper to create any amount of colors as distinct from each other as possible.
        // The logic behind this approach is detailed at the following address:
        // https://martin.ankerl.com/2009/12/09/how-to-create-random-colors-programmatically/
        URPMaterialPropertyBaseColor RandomColor()
        {
            // Note: if you are not familiar with this concept, this is a "local function".
            // You can search for that term on the internet for more information.

            // 0.618034005f == 2 / (math.sqrt(5) + 1) == inverse of the golden ratio
            hue = (hue + 0.618034005f) % 1;
            var color = Color.HSVToRGB(hue, 1.0f, 1.0f);
            return new URPMaterialPropertyBaseColor { Value = (Vector4)color };
        }

        var stationColor = RandomColor();

        foreach (var station in stations)
        {
            ecb.SetComponentForLinkedEntityGroup(station, queryMask, stationColor);
        }

        state.Enabled = false;
    }
}
