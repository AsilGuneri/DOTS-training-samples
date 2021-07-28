using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public class SpawnerSystem : SystemBase
{
    private static readonly float4 MIN_HEIGHT_COLOR = new float4(0, 1, 0, 1);
    private static readonly float4 MAX_HEIGHT_COLOR = new float4(99 / 255f, 47 / 255f, 0 / 255f, 1);
    
    private EntityQuery _PlayerEntityQuery;
    private EntityQuery _BoxEntityQuery;
    private EntityQuery _BoxMapEntityQuery;

    private Entity _BoxMapEntity;

    protected override void OnCreate()
    {
        _PlayerEntityQuery = EntityManager.CreateEntityQuery(typeof(Player));
        _BoxEntityQuery = EntityManager.CreateEntityQuery(typeof(NonUniformScale)); // TODO: Reinforce this maybe
        _BoxMapEntityQuery = EntityManager.CreateEntityQuery(typeof(HeightBufferElement), typeof(OccupiedBufferElement));

        _BoxMapEntity = EntityManager.CreateEntity(typeof(HeightBufferElement), typeof(OccupiedBufferElement));
    }

    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // The PRNG (pseudorandom number generator) from Unity.Mathematics is a struct
        // and can be used in jobs. For simplicity and debuggability in development,
        // we'll initialize it with a constant. (In release, we'd want a seed that
        // randomly varies, such as the time from the user's system clock.)
        var random = new Random(1234);

        var heightMapBuffer = EntityManager.GetBuffer<HeightBufferElement>(_BoxMapEntity);
        var occupiedMapBuffer = EntityManager.GetBuffer<OccupiedBufferElement>(_BoxMapEntity);

        var refs = this.GetSingleton<GameObjectRefs>();
        Entities
            .WithAll<Spawner>()
            .WithoutBurst()
            .ForEach((Entity entity) =>
            {
                
                var config = refs.Config.Data;
                
                ecb.DestroyEntity(entity);

                // build terrain
                heightMapBuffer.Clear();
                occupiedMapBuffer.Clear();
                ecb.DestroyEntitiesForEntityQuery(_BoxEntityQuery);
                for (int i = 0; i < config.TerrainWidth; ++i)
                {
                    for (int j = 0; j < config.TerrainLength; ++j)
                    {
                        float height = random.NextFloat(config.MinTerrainHeight, config.MaxTerrainHeight);
                        heightMapBuffer.Add((HeightBufferElement) height);
                        occupiedMapBuffer.Add((OccupiedBufferElement)false);

                        var box = ecb.Instantiate(refs.BoxPrefab);
                        ecb.SetComponent(box, new NonUniformScale
                        {
                            Value = new float3(1, height, 1)
                        });

                        ecb.SetComponent(box, new URPMaterialPropertyBaseColor
                        {
                            Value = GetColorForHeight(height, config.MinTerrainHeight, config.MaxTerrainHeight)
                        });

                        ecb.SetComponent(box, new Translation
                        {
                            Value = new float3(j, height / 2, i) // reposition halfway to height to level all at 0 plane
                        });
                    }
                }

                // new fresh player at proper x/y and height and empty parabola t value
                ecb.DestroyEntitiesForEntityQuery(_PlayerEntityQuery);
                var player = ecb.Instantiate(refs.PlayerPrefab);
                int boxX = config.TerrainLength / 2; // TODO: spawn player at which box ??? look at original game logic I guess; assuming center for now
                int boxY = config.TerrainWidth / 2;
                float boxHeight = heightMapBuffer[boxY * config.TerrainLength + boxX] + Player.Y_OFFSET; // use box height map to figure out starting y
                ecb.SetComponent(player, new Translation
                {
                    Value = new float3(boxX, boxHeight + Player.Y_OFFSET, boxY) // reposition halfway to height to level all at 0 plane
                });
                ecb.AddComponent(player, new ParabolaTValue
                {
                    Value = -1 // less than zero will trigger recalculate the next bounce parabola
                });

                // make tanks!
                for (int i = 0; i < config.TankCount; i++)
                {
                    var turretPosX = random.NextInt(1, config.TerrainLength);
                    var turretPosY = random.NextInt(1, config.TerrainWidth);
                    var turretHeight = heightMapBuffer[turretPosY * config.TerrainLength + turretPosX];
                    var turretBase = ecb.Instantiate(refs.TurretBase);

                    ecb.SetComponent(turretBase, new Translation
                    {
                        Value = new float3(turretPosX, turretHeight, turretPosY) // TODO: figure out turret base offset needs
                    });
                }
                
                
            }).Run();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    /// <summary>
    /// Helper function to calculate terrain color to use for a given height
    /// TODO: how/where do we put to share between spawner and ball collision system when it recalculates color based on new height
    /// 
    /// </summary>
    /// <param name="height"></param>
    /// <param name="minTerrainHeight"></param>
    /// <param name="maxTerrainHeight"></param>
    /// <returns></returns>
    private static float4 GetColorForHeight(float height, float minTerrainHeight, float maxTerrainHeight)
    {
        float4 color;

        // change color based on height
        if (math.abs(maxTerrainHeight - minTerrainHeight) < math.EPSILON) // special case, if max is close to min just color as min height
        {
            color = MIN_HEIGHT_COLOR;
        }
        else
        {
            color = math.lerp(MIN_HEIGHT_COLOR, MAX_HEIGHT_COLOR, (height - minTerrainHeight) / (maxTerrainHeight - minTerrainHeight));
        }

        return color;
    }
}
