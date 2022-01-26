using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using static Unity.Mathematics.math;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[UpdateAfter(typeof(SpawnerSystem))]
public partial class UpdateStateSystem : SystemBase
{
    private EntityQuery foodQuery;
    private EntityCommandBufferSystem commandBufferSystem;

    protected override void OnCreate()
    {
        commandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        // Get NativeArray data for food translation and entity data
        foodQuery = GetEntityQuery(typeof(FoodTag));
        int foodCount = foodQuery.CalculateEntityCount();
        NativeArray<Translation> foodTranslationData = new NativeArray<Translation>(foodCount, Allocator.TempJob);
        NativeArray<Entity> foodEntityData = new NativeArray<Entity>(foodCount, Allocator.TempJob);
        Entities
            .WithStoreEntityQueryInField(ref foodQuery)
            .WithAll<FoodTag>()
            .ForEach((Entity entity, int entityInQueryIndex, in Translation translation) =>
            {
                foodTranslationData[entityInQueryIndex] = translation;
                foodEntityData[entityInQueryIndex] = entity;
            }).WithName("GetFoodData")
            .ScheduleParallel();

        // Used to determine if two translations are "equal enough"
        const float distanceDelta = 0.1f;

        // Parallel ECB for recording component add / remove
        var ecb = commandBufferSystem.CreateCommandBuffer();
        var parallelWriter = ecb.AsParallelWriter();
        
        Random random = new Random(1234);
        var goalDepth = 10;
        var arenaExtents = new int2(40, 15);
        var halfArenaHeight = 10;

        // Get "close enough" Food based on distance calculation
        Entities.WithAll<BeeTag>()
            .ForEach((Entity entity, int entityInQueryIndex, ref State state, ref PP_Movement movement, ref CarriedEntity carriedEntity, in Translation translation, in BeeTeam team) =>
            {
                // If Bee is idle -> seeking
                //           carrying -> continue
                //           seeking -> check for attack option then check for carry option
                //           attacking -> check for carry option then check for seeking
                if (state.value == StateValues.Carrying)
                {
                    return;
                }

                if (state.value == StateValues.Seeking)
                {
                    // Also potentially check for attack here
                    for (int i = 0; i < foodCount; i++)
                    {
                        float translationDistance = distance(translation.Value, foodTranslationData[i].Value);
                        if (translationDistance <= distanceDelta && foodEntityData[i] != Entity.Null)
                        {
                            state.value = StateValues.Carrying;
                            carriedEntity.Value = foodEntityData[i];
                            
                            // Calculate end location based on team value;
                            float3 endLocation;
                            if (team.Value == TeamValue.Yellow)
                            {
                                var randomX = random.NextInt(40, 50);
                                var randomY = random.NextInt(halfArenaHeight + halfArenaHeight);
                                var randomZ = random.NextInt(-arenaExtents.y + 1, arenaExtents.y - 1);
                                endLocation = float3(randomX, randomY, randomZ);
                            }
                            else
                            {
                                var randomX = random.NextInt(-50, -40);
                                var randomY = random.NextInt(halfArenaHeight + halfArenaHeight);
                                var randomZ = random.NextInt(-arenaExtents.y + 1, arenaExtents.y - 1);
                                endLocation = float3(randomX, randomY, randomZ);
                            }

                            movement.endLocation = endLocation;
                            movement.startLocation = translation.Value;
                            movement.timeToTravel = distance(endLocation, translation.Value) / 10;
                            movement.t = 0.0f;
                            // Add updated movement information to food entity
                            parallelWriter.AddComponent(entityInQueryIndex, foodEntityData[i], 
                                PP_Movement.Create(movement.startLocation + float3(0f, -1f, 0f),
                                    movement.endLocation + float3(0f, -1f, 0f)));
                                    
                            break;
                        }
                    }

                    if (movement.t >= 0.99f)
                    {
                        // If we have finished moving and are still seeking, go back to idle status
                        state.value = StateValues.Idle;
                    }
                    else
                    {
                        return;
                    }
                }

                if (state.value == StateValues.Idle)
                {
                    // Choose food at random here
                    // TODO: Exclude food that is being carried or dropped?
                    int randomInt = random.NextInt(0, foodTranslationData.Length - 1);
                    Translation randomFoodTranslation = foodTranslationData[randomInt];
                    movement.GoTo(translation.Value, randomFoodTranslation.Value);
                    state.value = StateValues.Seeking;
                }
            }).WithReadOnly(foodTranslationData)
            .WithReadOnly(foodEntityData)
            .WithDisposeOnCompletion(foodTranslationData)
            .WithDisposeOnCompletion(foodEntityData)
            .WithName("ProcessBeeState")
            .ScheduleParallel();
        
        commandBufferSystem.AddJobHandleForProducer(Dependency);
    }
}
