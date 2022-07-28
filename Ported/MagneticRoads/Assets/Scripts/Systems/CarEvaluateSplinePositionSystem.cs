using Aspects;
using Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Util;

namespace Systems
{
    [WithAll(typeof(Car))]
    [BurstCompile]
    public partial struct CarEvaluateSplinePositionJob : IJobEntity
    {
        [ReadOnly]
        public ComponentDataFromEntity<RoadSegment> RoadSegmentFromEntity;
        public float DT;
        public ComponentDataFromEntity<WaitingAtIntersection> CarAtIntersectionFromEntity;

        void Execute(ref CarAspect carAspect)
        {
            RoadSegmentFromEntity.TryGetComponent(carAspect.RoadSegment, out RoadSegment rs);
            var carT = math.clamp(carAspect.T + ((carAspect.Speed * DT / rs.Length)), 0, 1);
            
            // Cars in lane 2 and 4 go backwards alone the spline
            var directionalT = carAspect.LaneNumber % 2 == 1 ? carT : 1 - carT;
            
            // Center of the spline
            var splinePos = Spline.EvaluatePosition(rs.Start, rs.End, directionalT);

            // TODO: spin rot if backwards
            var rot = Spline.EvaluateRotation(rs.Start, rs.End, directionalT);
            var offset = math.mul(rot, Spline.GetLocalCarOffset(carAspect.LaneNumber));

            carAspect.T = carT;
            carAspect.Position = splinePos + offset;
            carAspect.Rotation = rot;

            if (carT >= 1)
            {
                CarAtIntersectionFromEntity.SetComponentEnabled(carAspect.Entity, true);
            }
        }
    }

    [UpdateAfter(typeof(CarSpeedSystem))]
    [BurstCompile]
    partial struct CarEvaluateSplinePositionSystem : ISystem
    {
        ComponentDataFromEntity<RoadSegment> m_RoadSegmentFromEntity;
        ComponentDataFromEntity<WaitingAtIntersection> m_CarAtIntersectionFromEntity;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_RoadSegmentFromEntity = state.GetComponentDataFromEntity<RoadSegment>(true);
            m_CarAtIntersectionFromEntity = state.GetComponentDataFromEntity<WaitingAtIntersection>(false);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_RoadSegmentFromEntity.Update(ref state);
            m_CarAtIntersectionFromEntity.Update(ref state);

            var dt = state.Time.DeltaTime;

            var evaluatePositionOnSpline = new CarEvaluateSplinePositionJob
            {
                DT = dt,
                RoadSegmentFromEntity = m_RoadSegmentFromEntity,
                CarAtIntersectionFromEntity = m_CarAtIntersectionFromEntity
                
            };
            evaluatePositionOnSpline.ScheduleParallel();
        }
    }
}
