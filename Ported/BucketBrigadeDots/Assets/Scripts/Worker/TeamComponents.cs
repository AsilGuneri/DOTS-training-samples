using Unity.Entities;
using Unity.Mathematics;

public enum TeamStates
{
    Idle,
    Repositioning,
    Extinguishing
}

public struct TeamSpawner : IComponentData
{
    public Entity WorkerPrefab;
    public int NumberOfTeams;
    public int WorkersPerTeam;
}

public struct Team : IComponentData
{
    public float2 FirePosition;
    public float2 WaterPosition;
}

public struct TeamState : IComponentData
{
    public TeamStates Value;
}

public struct TeamMember : IBufferElementData
{
    public Entity Value;
}       