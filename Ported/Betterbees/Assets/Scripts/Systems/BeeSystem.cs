using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEditor;
using static UnityEngine.GraphicsBuffer;

[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct BeeSystem : ISystem
{
    private EntityQuery _availableFoodSourcesQuery;
    private NativeArray<EntityQuery> _availableBeesQueries;
    private NativeArray<int> _enemyCounts;
    private uint _updateCounter;
    private static float3 _halfFloat = new float3(0.5f, 0.5f, 0.5f);

    private NativeArray<float3> _boundaryNormals;
    private static readonly int _boundaryNormalCount = 3;

    UnsafeList<NativeArray<Entity>> _beeTeams;
    public ComponentLookup<LocalTransform> _localTransformLookup;

    static readonly ProfilerMarker s_PerfMarkerIdle = new ProfilerMarker("Idle");
    static readonly ProfilerMarker s_PerfMarkerGathering = new ProfilerMarker("Gathering");
    static readonly ProfilerMarker s_PerfMarkerAttacking = new ProfilerMarker("Attacking");

    static readonly ProfilerMarker s_PerfMarkerRemoveInvalidBeeTarget = new ProfilerMarker("RemoveInvalidBeeTarget");
    static readonly ProfilerMarker s_PerfMarkerFindBeeTarget = new ProfilerMarker("FindBeeTarget");
    static readonly ProfilerMarker s_PerfMarkerFindBeeTargetQuery = new ProfilerMarker("FindBeeTargetQuery");
    static readonly ProfilerMarker s_PerfMarkerFlyToBeeTarget = new ProfilerMarker("FlyToBeeTarget");
    static readonly ProfilerMarker s_PerfMarkerReturning = new ProfilerMarker("Returning");

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Config>();
        state.RequireForUpdate<BeeSettingsSingletonComponent>();

        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<FoodComponent>()
                .WithNone<Parent, GravityComponent>();

            _availableFoodSourcesQuery = state.GetEntityQuery(builder);
        }
        _availableBeesQueries = new NativeArray<EntityQuery>((int)HiveTag.HiveCount, Allocator.Persistent);
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BeeState, HiveYellow>();
            _availableBeesQueries[(int)HiveTag.HiveYellow] = state.GetEntityQuery(builder);
        }
        { 
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BeeState, HiveBlue>();
            _availableBeesQueries[(int)HiveTag.HiveBlue] = state.GetEntityQuery(builder);
        }
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BeeState, HiveOrange>();
            _availableBeesQueries[(int)HiveTag.HiveOrange] = state.GetEntityQuery(builder);
        }

        _boundaryNormals = new NativeArray<float3>(_boundaryNormalCount, Allocator.Persistent);
        for (int i = 0; i < _boundaryNormalCount; i++)
        {
            float3 normal = float3.zero;
            normal[i] = 1;
            _boundaryNormals[i] = normal;
        }

        _beeTeams = new UnsafeList<NativeArray<Entity>>((int)HiveTag.HiveCount, Allocator.Persistent);
        _enemyCounts = new NativeArray<int>((int)HiveTag.HiveCount, Allocator.Persistent);

        _localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_boundaryNormals.IsCreated)
            _boundaryNormals.Dispose();
        if (_availableBeesQueries.IsCreated)
            _availableBeesQueries.Dispose();
        if (_beeTeams.IsCreated)
            _beeTeams.Dispose();
        if (_enemyCounts.IsCreated)
            _enemyCounts.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<Config>();
        var beeSettings = SystemAPI.GetSingleton<BeeSettingsSingletonComponent>();
        var random = Random.CreateFromIndex(_updateCounter++);

        _beeTeams.Clear();
        for (int i = 0; i < (int)HiveTag.HiveCount; i++)
            _beeTeams.Add(_availableBeesQueries[0].ToEntityArray(Allocator.TempJob));
        for (int i = 0; i < (int)HiveTag.HiveCount; i++)
        {
            _enemyCounts[i] = 0;
            for (int j = 0; j < (int)HiveTag.HiveCount; j++)
            {
                if (i != j)
                    _enemyCounts[i] += _beeTeams[j].Length;
            }
        }

        var availableFood = _availableFoodSourcesQuery.ToEntityArray(Allocator.TempJob);

        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _localTransformLookup.Update(ref state);

        var beeJob = new BeeJob
        {
            _random = random,
            _config = config,
            _beeSettings = beeSettings,
            _availableBeesQueries = _availableBeesQueries,
            _availableFood = availableFood,
            _enemyCounts = _enemyCounts,
            _commandBuffer = ecb.AsParallelWriter(),
            _transformsLookup = _localTransformLookup,
            _beeTeams = _beeTeams,
            _boundaryNormals = _boundaryNormals,
            _deltaTime = SystemAPI.Time.DeltaTime
        };
        var beeJobHandle = beeJob.ScheduleParallel(state.Dependency);

        var rotationAndScaleJob = new RotationAndScaleJob
        {
            _beeSettings = beeSettings
        };
        // depends on bee job since it modifies transforms which have to be constant in bee job
        var rotationAndScaleJobHandle = rotationAndScaleJob.ScheduleParallel(beeJobHandle);

        state.Dependency = JobHandle.CombineDependencies(beeJobHandle, rotationAndScaleJobHandle);
    }   

    [BurstCompile]
    private partial struct BeeJob : IJobEntity
    {
        public Random _random;
        public Config _config;
        public BeeSettingsSingletonComponent _beeSettings;
        [ReadOnly]
        public NativeArray<EntityQuery> _availableBeesQueries;
        [ReadOnly]
        public NativeArray<Entity> _availableFood;
        [ReadOnly]
        public NativeArray<int> _enemyCounts;
        public EntityCommandBuffer.ParallelWriter _commandBuffer;
        [ReadOnly]
        public ComponentLookup<LocalTransform> _transformsLookup;
        [ReadOnly]
        public UnsafeList<NativeArray<Entity>> _beeTeams;
        [ReadOnly]
        public NativeArray<float3> _boundaryNormals;
        public float _deltaTime;

        public void Execute(ref VelocityComponent velocity, ref BeeState beeState, ref TargetComponent target, in ReturnHomeComponent home, ref PostTransformMatrix postTransformMatrix, Entity entity, [ChunkIndexInQuery] int chunkIndex)
        {
            BaseMovement(ref velocity, _deltaTime);

            var transform = _transformsLookup[entity];

            switch (beeState.state)
            {
                case BeeState.State.IDLE:
                    s_PerfMarkerIdle.Begin();
                    Idle(ref beeState);
                    s_PerfMarkerIdle.End();
                    break;
                case BeeState.State.GATHERING:
                    s_PerfMarkerGathering.Begin();
                    float agression = _random.NextFloat();
                    if (_enemyCounts[(int)beeState.hiveTag] > 0 && agression < _beeSettings.aggressionPercentage)
                    {
                        target.Target = Entity.Null;
                        beeState.state = BeeState.State.ATTACKING;
                    }
                    else
                    {
                        Gathering(entity, ref beeState, transform, ref velocity, ref target, chunkIndex);
                    }
                    s_PerfMarkerGathering.End();
                    break;
                case BeeState.State.ATTACKING:
                    s_PerfMarkerAttacking.Begin();
                    Attacking(entity, ref beeState, transform, ref velocity, ref target, chunkIndex);
                    s_PerfMarkerAttacking.End();
                    break;
                case BeeState.State.RETURNING:
                    s_PerfMarkerReturning.Begin();
                    Returning(entity, ref beeState, transform, ref velocity, ref target, home, chunkIndex);
                    s_PerfMarkerReturning.End();
                    break;
            }

            ApplyBoundaries(transform.Position, ref velocity);
        }

        private void BaseMovement(
            ref VelocityComponent velocity,
            float dt)
        {
            velocity.Velocity += (_random.NextFloat3() - _halfFloat) * _beeSettings.flightJitter * dt;
            velocity.Velocity *= (1f - _beeSettings.damping);
        }

        private void Idle(ref BeeState beeState)
        {
            beeState.state = BeeState.State.GATHERING;
        }

        private void Gathering(
            Entity beeEntity,
            ref BeeState beeState,
            in LocalTransform transform,
            ref VelocityComponent velocity,
            ref TargetComponent target,
            int chunkIndex)
        {
            RemoveInvalidTarget(ref target);
            FindFoodTarget(ref target, ref beeState);
            FlyToTarget(beeEntity, ref beeState, transform, ref velocity, ref target, chunkIndex);
        }

        private void RemoveInvalidTarget(ref TargetComponent target)
        {
            bool hasInvalidTarget = target.Target != Entity.Null && !_availableFood.Contains(target.Target);
            if (hasInvalidTarget)
            {
                target.Target = Entity.Null;
            }
        }

        private void FindFoodTarget(ref TargetComponent target, ref BeeState beeState)
        {
            bool alreadyHasTarget = target.Target != Entity.Null;
            if (alreadyHasTarget)
            {
                return;
            }

            if (_availableFood.Length > 0)
            {
                int index = (int)(_random.NextUInt() % _availableFood.Length);
                target.Target = _availableFood[index];
            }
            else
            {
                beeState.state = BeeState.State.IDLE;
            }
        }

        private void FlyToTarget(
            Entity beeEntity,
            ref BeeState beeState,
            in LocalTransform transform,
            ref VelocityComponent velocity,
            ref TargetComponent target,
            int chunkIndex)
        {
            if (target.Target == Entity.Null)
            {
                return;
            }

            var targetTransform = _transformsLookup[target.Target];
            float3 delta = targetTransform.Position - transform.Position;
            float dist2 = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

            if (dist2 > _beeSettings.grabDistance * _beeSettings.grabDistance)
            {
                float dist = math.sqrt(dist2);
                velocity.Velocity += delta * (_beeSettings.chaseForce * _deltaTime / dist);
            }
            else
            {
                _commandBuffer.AddComponent(chunkIndex, target.Target, new Parent { Value = beeEntity });

                targetTransform.Position = new float3();
                targetTransform.Position.y = -0.08f;
                _commandBuffer.SetComponent(chunkIndex, target.Target, targetTransform);

                beeState.state = BeeState.State.RETURNING;
            }
        }

        private void ApplyBoundaries(
            float3 position,
            ref VelocityComponent velocity)
        {
            float3 boundsNormal = float3.zero;
            bool outsideBounds = false;
            for (int i = 0; i < _boundaryNormalCount; i++)
            {
                if (position[i] > _config.bounds[i])
                {
                    boundsNormal += -_boundaryNormals[i];
                    outsideBounds = true;
                }
                else if (position[i] < -_config.bounds[i])
                {
                    boundsNormal += _boundaryNormals[i];
                    outsideBounds = true;
                }
            }
            boundsNormal = math.normalize(boundsNormal);

            if (outsideBounds)
            {
                velocity.Velocity = boundsNormal;
            }
        }

        private void Attacking(
        Entity beeEntity,
        ref BeeState beeState,
        in LocalTransform transform,
        ref VelocityComponent velocity,
        ref TargetComponent target,
        int chunkIndex)
        {
            s_PerfMarkerRemoveInvalidBeeTarget.Begin();
            RemoveInvalidBeeTarget(ref target, beeState);
            s_PerfMarkerRemoveInvalidBeeTarget.End();

            s_PerfMarkerFindBeeTarget.Begin();
            FindBeeTarget(ref target, ref beeState);
            s_PerfMarkerFindBeeTarget.End();

            s_PerfMarkerFlyToBeeTarget.Begin();
            FlyToBeeTarget(beeEntity, ref beeState, transform, ref velocity, ref target, chunkIndex);
            s_PerfMarkerFlyToBeeTarget.End();
        }

        private int EnemyTag(ref Random random, BeeState beeState)
        {
            int enemyHive = (int)(random.NextUInt() % (uint)HiveTag.HiveCount);
            if (enemyHive == (int)beeState.hiveTag)
                enemyHive = (enemyHive + 1) % (int)HiveTag.HiveCount;
            return enemyHive;
        }

        private void RemoveInvalidBeeTarget(ref TargetComponent target, in BeeState beeState)
        {
            bool hasInvalidTarget = false;
            if (target.Target != Entity.Null)
            {
                for (int i = 0; i < (int)HiveTag.HiveCount; i++)
                {
                    if (hasInvalidTarget)
                        break;

                    if (i != (int)beeState.hiveTag)
                        hasInvalidTarget = !_availableBeesQueries[i].Matches(target.Target);
                }
            }
            if (hasInvalidTarget)
            {
                target.Target = Entity.Null;
            }
        }

        private void FindBeeTarget(ref TargetComponent target, ref BeeState beeState)
        {
            bool alreadyHasTarget = target.Target != Entity.Null;
            if (alreadyHasTarget)
            {
                return;
            }

            s_PerfMarkerFindBeeTargetQuery.Begin();
            var enemyBees = _beeTeams[EnemyTag(ref _random, beeState)];
            s_PerfMarkerFindBeeTargetQuery.End();

            if (enemyBees.Length > 0)
            {
                int index = (int)(_random.NextUInt() % enemyBees.Length);
                var targetEntity = enemyBees[index];
                target.Target = targetEntity;
            }
            else
            {
                beeState.state = BeeState.State.IDLE;
            }
        }

        private void FlyToBeeTarget(
            Entity beeEntity,
            ref BeeState beeState,
            in LocalTransform transform,
            ref VelocityComponent velocity,
            ref TargetComponent target,
            int chunkIndex)
        {
            if (target.Target == Entity.Null)
            {
                return;
            }

            var targetTransform = _transformsLookup[target.Target];
            float3 delta = targetTransform.Position - transform.Position;
            float dist2 = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

            if (dist2 > _beeSettings.attackDistance * _beeSettings.attackDistance)
            {
                float dist = math.sqrt(dist2);
                velocity.Velocity += delta * (_beeSettings.chaseForce * _deltaTime / dist);
            }
            else if (dist2 > _beeSettings.hitDistance * _beeSettings.hitDistance)
            {
                float dist = math.sqrt(dist2);
                velocity.Velocity += delta * (_beeSettings.attackForce * _deltaTime / dist);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    var blood = _commandBuffer.Instantiate(chunkIndex, _config.bloodEntity);
                    _commandBuffer.SetComponent(chunkIndex, blood, new LocalTransform
                    {
                        Position = targetTransform.Position,
                        Scale = _random.NextFloat(),
                        Rotation = quaternion.identity
                    });
                    _commandBuffer.SetComponent(chunkIndex, blood, new VelocityComponent { Velocity = _random.NextFloat3() });
                }

                /*
                var targetBeeState = _beeStateLookup[target.Target];
                if (targetBeeState.state == BeeState.State.RETURNING)
                {
                    var foodTarget = _targetComponentLookup[target.Target];
                    _commandBuffer.SetComponent<LocalTransform>(chunkIndex, foodTarget.Target, LocalTransform.FromPosition(targetTransform.Position));
                }
                */
                
                _commandBuffer.DestroyEntity(chunkIndex, target.Target);

                beeState.state = BeeState.State.IDLE;
            }
        }

        private void Returning(
            Entity beeEntity,
            ref BeeState beeState,
            in LocalTransform transform,
            ref VelocityComponent velocity,
            ref TargetComponent target,
            in ReturnHomeComponent home,
            int chunkIndex)
        {
            float3 beePos = transform.Position;
            float3 targetPos = math.clamp(beePos, home.HomeMinBounds, home.HomeMaxBounds);
            float3 delta = targetPos - beePos;
            float dist2 = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

            if (dist2 > _beeSettings.grabDistance * _beeSettings.grabDistance)
            {
                float dist = math.sqrt(dist2);
                velocity.Velocity += delta * (_beeSettings.carryForce * _deltaTime / dist);
            }
            else
            {
                Entity foodEntity = target.Target;

                if (_transformsLookup.HasComponent(foodEntity))
                {
                    _commandBuffer.RemoveComponent<Parent>(chunkIndex, foodEntity);

                    var foodTransform = _transformsLookup[foodEntity];
                    foodTransform.Position = transform.Position;
                    _commandBuffer.SetComponent(chunkIndex, foodEntity, foodTransform);

                    _commandBuffer.SetComponent(chunkIndex, foodEntity, velocity);

                    _commandBuffer.AddComponent<GravityComponent>(chunkIndex, foodEntity);
                }

                beeState.state = BeeState.State.IDLE;
                target.Target = Entity.Null;
            }
        }
    }

    [BurstCompile]
    private partial struct RotationAndScaleJob : IJobEntity
    {
        public BeeSettingsSingletonComponent _beeSettings;

        public void Execute(in VelocityComponent velocity, ref LocalTransform transform, ref PostTransformMatrix postTransformMatrix, in BeeState beestate)
        {
            float3 direction = math.normalize(velocity.Velocity);
            transform.Rotation = quaternion.LookRotation(direction, new float3(0, 1, 0));

            float scale = math.length(velocity.Velocity) * _beeSettings.scaleMultiplier;
            float forwardScale = math.clamp(scale, _beeSettings.minScale.x, _beeSettings.maxScale.x);
            float sideScale = math.clamp(1.0f / scale, _beeSettings.minScale.y, _beeSettings.maxScale.y);
            float3 localScale = new float3(sideScale, sideScale, forwardScale);

            postTransformMatrix.Value = float4x4.Scale(localScale);
        }
    }
}