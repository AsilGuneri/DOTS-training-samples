using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[BurstCompile]
public partial struct PhysicalSystem : ISystem
{
    private EntityQuery _physicalQuery;
    private ComponentLookup<Physical> _physicals;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _physicalQuery = SystemAPI.QueryBuilder().WithAll<Physical>().Build();
        _physicals = state.GetComponentLookup<Physical>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dt = SystemAPI.Time.DeltaTime;
        _physicals.Update(ref state);


        var physicalJob = new PhysicalJob()
        {
            Dt = dt
        };

        physicalJob.ScheduleParallel();
    }

    [BurstCompile]
    partial struct PhysicalJob : IJobEntity
    {
        public float Dt;
        public static readonly float3 right = new float3(1, 0, 0);
        public static readonly float3 up = new float3(0, 1, 0);
        public static readonly float3 forward = new float3(0, 0, 1);

        void Execute(ref Physical physical, ref LocalToWorldTransform localToWorld, ref PostTransformMatrix postTransformMatrix)
        {
            // Apply gravity if necessary
            if (physical.IsFalling)
            {
                physical.Velocity.y += Field.gravity * Dt;
            }

            // Move according to velocity
            var delta = physical.Velocity * Dt * physical.SpeedModifier;
            physical.Position += delta;

            // Handle the position having gone beyond the field limits
            var absPosition = math.abs(physical.Position);
            bool exceededX = absPosition.x > Field.BoundsMax.x;
            bool exceededY = absPosition.y > Field.BoundsMax.y;
            bool exceededZ = absPosition.z > Field.BoundsMax.z;
            if ( exceededX || exceededY || exceededZ )
            {
                var exceededPosition = physical.Position;
                physical.Position = math.clamp(physical.Position, Field.BoundsMin, Field.BoundsMax);
                var excess = exceededPosition - physical.Position;

                switch (physical.Collision)
                {
                    case Physical.FieldCollisionType.Bounce:
                        // Reflect velocity and excess
                        if (exceededX)
                        {
                            excess = math.reflect(excess, right);
                            physical.Velocity = math.reflect(physical.Velocity, right);
                        }
                        if (exceededY)
                        {
                            excess = math.reflect(excess, up);
                            physical.Velocity = math.reflect(physical.Velocity, up);
                        }
                        if (exceededZ)
                        {
                            excess = math.reflect(excess, forward);
                            physical.Velocity = math.reflect(physical.Velocity, forward);
                        }
                        physical.Position += excess;
                        break;

                    case Physical.FieldCollisionType.Splat:
                        physical.IsFalling = false;
                        physical.Velocity = float3.zero;
                        break;

                    case Physical.FieldCollisionType.Slump:
                        if (physical.Position.y == Field.BoundsMin.y)
                        {
                            physical.IsFalling = false;
                        }
                        physical.Velocity = float3.zero;
                        break;
                }
            }

            // Apply position to entity transform
            var scale = localToWorld.Value.Scale;
            var uniformScaleTransform = new UniformScaleTransform
            {
                Position = physical.Position,
                Rotation = quaternion.LookRotationSafe(physical.Velocity, up),
                Scale = scale
            };

            localToWorld.Value = uniformScaleTransform;
            
            // Calculate and apply stretch
            float stretch = math.max(1f, math.length(physical.Velocity) * physical.Stretch);
            float minorStretch = 1f; // + ((stretch - 1f) / 5f);
            
            postTransformMatrix.Value = float4x4.Scale(new float3(minorStretch, minorStretch, stretch));
        }
    }

}