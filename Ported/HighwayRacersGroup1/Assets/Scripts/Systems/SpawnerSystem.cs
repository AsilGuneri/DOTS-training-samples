using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;


partial struct SpawnerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Config>();
    }
    
    public void OnDestroy(ref SystemState state)
    {
    }
    
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<Config>();
        
        var random = Random.CreateFromIndex(1234);
        var hue = random.NextFloat();
        
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var segmentPositions = HighwaySpawner.SegmentPositions;
        var segmentDirections = HighwaySpawner.SegmentRightDirections;

        for(var i = 0; i < segmentPositions.Length; ++i)
        {
            var entity = ecb.Instantiate(config.SegmentPrefab);
            var segmentTransform = LocalTransform.FromPosition(segmentPositions[i]);
            ecb.SetComponent(entity, segmentTransform);
            ecb.SetComponent(entity, new Segment { Right = segmentDirections[i]});
        }

        var cars = CollectionHelper.CreateNativeArray<Entity>(config.CarCount, Allocator.Temp);
        ecb.Instantiate(config.CarPrefab, cars);
        
        for (var i = 0; i < cars.Length; ++i)
        {
            var segmentID = UnityEngine.Random.Range(0, segmentPositions.Length - 1);

            var carSpeed = (UnityEngine.Random.value + 0.2f) * 50;
            var laneID = UnityEngine.Random.Range(0, 4);

            var car = new CarData
            {
                SegmentID = segmentID,
                Lane = laneID,
                TargetLane = laneID,
                DefaultSpeed = carSpeed,
                Speed = carSpeed,
                inFrontCarIndex = -1
            };
            ecb.SetComponent(cars[i], car);

            float carLane = 4f + (2.45f * car.Lane);
            var position = segmentPositions[segmentID] + (segmentDirections[segmentID] * -carLane);
            ecb.SetComponent(cars[i], LocalTransform.FromPosition(position));
            
            hue = (hue + 0.618034005f) % 1;
            var color = UnityEngine.Color.HSVToRGB(hue, 1.0f, 1.0f);
            ecb.SetComponent(cars[i], new URPMaterialPropertyBaseColor { Value = (UnityEngine.Vector4)color });
        }

        state.Enabled = false;
    }
}