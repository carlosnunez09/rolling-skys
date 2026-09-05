using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RollingSkys.Performance
{
    /// <summary>
    /// Burst-compiled SIMD job for accelerating nearest-vertex queries on planet meshes.
    /// Eliminates managed iteration overhead and vectorizes distance calculations.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, OptimizeFor = OptimizeFor.Performance)]
    public struct FindNearestVertexBurstJob : IJob
    {
        [ReadOnly] public NativeArray<float3> Vertices;
        public float3 LocalPoint;
        public NativeArray<int> ResultIndex;

        public void Execute()
        {
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            int count = Vertices.Length;

            for (int i = 0; i < count; i++)
            {
                float d = math.distancesq(Vertices[i], LocalPoint);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }

            ResultIndex[0] = bestIdx;
        }
    }

    /// <summary>
    /// Burst-compiled parallel job for batch spatial queries (e.g. all 4 wheels + ground probe simultaneously).
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, OptimizeFor = OptimizeFor.Performance)]
    public struct BatchFindNearestVerticesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Vertices;
        [ReadOnly] public NativeArray<float3> LocalPoints;
        [WriteOnly] public NativeArray<int> ResultIndices;

        public void Execute(int index)
        {
            float3 target = LocalPoints[index];
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            int count = Vertices.Length;

            for (int i = 0; i < count; i++)
            {
                float d = math.distancesq(Vertices[i], target);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }

            ResultIndices[index] = bestIdx;
        }
    }
}
