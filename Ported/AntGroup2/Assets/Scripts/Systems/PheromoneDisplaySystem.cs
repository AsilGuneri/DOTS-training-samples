using Unity.Burst;
using UnityEngine;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial class PheromoneDisplaySystem : SystemBase
{
    public const int PheromoneTextureSizeX = 128;
    public const int PheromoneTextureSizeY = 128;
        
    public GameObject displayPlane;
    private Material displayMaterial;
    private Texture2D pheromoneTexture;

    [BurstCompile]
    protected override void OnCreate()
    {
        base.OnCreate();
        
        displayPlane = GameObject.Find("DisplayPlane");
        
        pheromoneTexture = new Texture2D(PheromoneTextureSizeX, PheromoneTextureSizeY, TextureFormat.R8, false);   // TEMP: Hardcoded size
        
        if (displayPlane.TryGetComponent<Renderer>(out var displayRenderer))
            displayMaterial = displayRenderer.material;
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<Config>();

        // Resize the display plane to cover the play area
        float planeScale = config.PlaySize / 10.0f;
        displayPlane.transform.localScale = new Vector3(planeScale,planeScale,planeScale);

        #if false
        // Assume there's only one PheromoneMap
        Entities.ForEach((Entity ent, in DynamicBuffer<PheromoneMap> map) =>
        {
            // TODO...
            
            // Clear
            for (int i = 0; i < PheromoneTextureSizeX; i++)
            {
                for (int j = 0; j < PheromoneTextureSizeY; j++)
                {
                    pheromoneTexture.SetPixel(i, j, Color.black);
                }    
            }
            
            // Random fill contents
            /*for (int i = 0; i < 64; i++)
            {
                float x = Random.value;
                float y = Random.value;
            
                pheromoneTexture.SetPixel((int)(x*(texSize.x-1)), (int)(y*(texSize.x-1)), Color.red);
            }*/

            
            for (int i = 0; i < map.Length; i++)
            {
                float amount = map[i].amount;

                int x = i % PheromoneTextureSizeX;
                int y = i / PheromoneTextureSizeX;
                
                // TODO: figure out it needs a flip.
                x = PheromoneTextureSizeY - 1 - x;
                y = PheromoneTextureSizeY - 1 - y;
                
                pheromoneTexture.SetPixel(x, y, new Color(math.saturate(amount), 0, 0, 1));
            }

            pheromoneTexture.Apply();
            
            displayMaterial.mainTexture = pheromoneTexture;
        }).WithAll<PheromoneMap>().WithoutBurst().Run();
        #else
        // Upload pheromone map buffer to the texture
        {
            var map = SystemAPI.GetSingletonBuffer<PheromoneMap>();
            var pixels = pheromoneTexture.GetPixelData<byte>(0);
            int len = map.Length;
            for (int i = 0; i < len; i++)
            {
                float a = map[i].amount;
                byte b = (byte)(a * 255.0f);

                // X & Y flip // TODO: investigate why it's needed.
                int dstIndex = len - i - 1;
                pixels[dstIndex] = b;
            }
            pheromoneTexture.Apply();
            displayMaterial.mainTexture = pheromoneTexture;
        }
        #endif
    }

}
