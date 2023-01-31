using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct SaschaMovementQueueInstruction
{
    public float3 Destination;
    public Entity Platform;
}

public struct SaschaMovementQueue : IComponentData
{
    public NativeQueue<SaschaMovementQueueInstruction> QueuedInstructions;
}

