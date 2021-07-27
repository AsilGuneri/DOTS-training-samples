﻿using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace src.Components
{
    /// <summary>
    ///     All config data for the simulation.
    /// </summary>
    [GenerateAuthoringComponent]
    public struct FireSimConfig : IComponentData
    {
        public Entity FullBucketPasserWorkerPrefab;
        public Entity EmptyBucketPasserWorkerPrefab;
        public Entity BucketFetcherPrefab;
        public Entity BucketThrowerWorkerPrefab;
        public Entity OmniWorkerPrefab;
        
        public Entity BucketPrefab;
 
    }

    public struct FireSimConfigValues : IComponentData
    {
        // Fire config
        [Tooltip("How many random fires do you want to battle?")]
        public int StartingFireCount;

        [Tooltip("How high the flames reach at max temperature")]
        public float MaxFlameHeight;

        [Tooltip("Size of an individual flame. Full grid will be (rows * cellSize)")]
        public float CellSize;

        [Tooltip("How many cells WIDE the simulation will be")]
        public int Rows;

        [Tooltip("How many cells DEEP the simulation will be")]
        public int Columns;

        private float simulation_WIDTH, simulation_DEPTH;

        [Tooltip("When temperature reaches *flashpoint* the cell is on fire")]
        public float Flashpoint;

        [Tooltip("How far does heat travel? Note: Higher heat radius significantly increases CPU usafge")]
        public int HeatRadius;

        [Tooltip("How fast will adjascent cells heat up?")]
        public float HeatTransferRate;

        [Range(0.0001f, 2f)] [Tooltip("How often the fire cells update. 1 = once per second. Lower = faster")]
        public float FireSimUpdateRate;

        [Range(0.0001f, 100f)] public float WorkerSpeed;
        [Range(0.0001f, 100f)] public float WorkerSpeedWhenHoldingBucket;

        [Range(0.0001f, 5f)] public float DistanceToPickupBucket;

        [Range(0.0001f, 50f)] public float WaterFillUpDuration;


        float GetTemperatureForCell(in DynamicBuffer<Temperature> temperatureBuffer, int row, int column)
        {
            if (row < 0 || column < 0 || row >= Rows || column >= Columns)
                return 0;

            int cellIndex = column + row * Columns;

            return temperatureBuffer[cellIndex].Intensity;
        }

        void SetTemperatureForCell(ref DynamicBuffer<Temperature> temperatureBuffer, int row, int column, float temperature)
        {
            if (row >= 0 && column >= 0 && row < Rows && column < Columns)
            {
                int cellIndex = column + row * Columns;

                temperatureBuffer[cellIndex] = new Temperature {Intensity = temperature};
            }
        }
    }
}