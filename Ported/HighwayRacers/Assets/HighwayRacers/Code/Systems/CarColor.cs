using Aspects;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[WithAll(typeof(CarColor))]
[BurstCompile]
public partial struct CarColorJob : IJobEntity
{

    public EntityCommandBuffer.ParallelWriter EntityParalleWriter;
    [BurstCompile]
    private void Execute([ChunkIndexInQuery] int entityQueryIndex, ref CarColorAspect Car)
    {
        var speedDifferential = Car.Speed;
        float4 color = new float4(0.5f, 0.5f, 0.5f, 1f);
        //color = new float4(Car.DistFromCarInFront * 0.03f, Car.DistFromCarInFront*0.1f, 10.0f - Car.DistFromCarInFront,1);
        if (Car.Speed > Car.CruisingSpeed)
        {
            color = new float4(0, 1, 0, 1);
        }
        else if (Car.Speed < Car.CruisingSpeed)
        {
            color = new float4(1, 0, 0, 1);
        }
        else
        {
            color = new float4(1, 1, 0, 1);
        }
        if (Car.PreviousDifferential != speedDifferential)  // Don't change color that don't need change
        {
            EntityParalleWriter.SetComponent(entityQueryIndex, Car.Self,
            new URPMaterialPropertyBaseColor
            {
                Value = color
            });
            Car.PreviousDifferential = speedDifferential;
        }
    }
}
[BurstCompile]
public partial struct CarColorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
        var writer = ecb.AsParallelWriter();

        var carColorJob = new CarColorJob
        {
            EntityParalleWriter = writer
        };
        var jobHandler = carColorJob.ScheduleParallel(state.Dependency);
        state.Dependency = jobHandler;
        jobHandler.Complete();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

    }
}
