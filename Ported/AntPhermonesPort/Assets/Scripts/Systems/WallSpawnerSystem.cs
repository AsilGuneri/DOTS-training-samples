using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;

public partial class WallSpawnerSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var map = EntityManager.GetBuffer<CellMap>(GetSingletonEntity<CellMap>());

        var random = new Unity.Mathematics.Random(1234);
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        Entities
            .ForEach((Entity entity, in WallSpawner wallSpawner) =>
            {
                ecb.DestroyEntity(entity);

                CellMapHelper.InitCellMap(map);

                for (int i = 0; i < Config.RingCount; ++i)
                {
                    // choose if 2 openings
                    int segmentCount = random.NextInt(1, 2);
                    float startAngle = random.NextFloat(0f, 360f);
                    float angleSize = 270f / (float)segmentCount;

                    for (int s = 0; s < segmentCount; ++s)
                    {
                        SpawnWallSegment(ecb, wallSpawner, (i + 1) * 10f, startAngle, startAngle+angleSize);
                    }
                }
            }).Run();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    static void SpawnWallSegment(EntityCommandBuffer ecb, WallSpawner wallSpawner, float distance, float startAngle, float endAngle, float stepAngle = 1f)
    {
        for (float angle = startAngle; angle <= endAngle; angle += stepAngle)
        {
            float tmpAngle = angle;
            if (tmpAngle >= 360f)
                tmpAngle -= 360f;
            tmpAngle *= Mathf.Deg2Rad;
            float x = Mathf.Cos(tmpAngle) * distance;
            float y = Mathf.Sin(tmpAngle) * distance;

            var instance = ecb.Instantiate(wallSpawner.WallComponent);
            ecb.SetComponent(instance, new Translation
            {
                Value = new float3(x, 0, y)
            });
        }
    }
}