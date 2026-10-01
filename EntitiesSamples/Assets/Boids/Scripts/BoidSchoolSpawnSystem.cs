using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Boids
{
    // Flock size control. The spawn system owns the singleton; the UI writes Delta and reads Count.
    public struct BoidPopulation : IComponentData
    {
        public int Count;
        public int Delta;   // fish to add (+) or remove (-) on the next update
        public bool Started;
    }

    [BurstCompile]
    public partial struct BoidSchoolSpawnSystem : ISystem
    {
        public const int StartCount = 10000;

        EntityQuery m_NewBoidQuery;
        EntityQuery m_BoidQuery;
        EntityQuery m_SchoolQuery;
        ComponentLookup<LocalToWorld> m_LocalToWorldLookup;

        public void OnCreate(ref SystemState state)
        {
            m_NewBoidQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<Boid, LocalTransform>().Build(ref state);
            m_BoidQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<Boid>().Build(ref state);
            m_SchoolQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<BoidSchool, LocalToWorld>().Build(ref state);
            m_LocalToWorldLookup = state.GetComponentLookup<LocalToWorld>();
            state.EntityManager.CreateSingleton<BoidPopulation>();
            state.RequireForUpdate(m_SchoolQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // read by value: the structural changes below invalidate any ref into the singleton
            var population = SystemAPI.GetSingleton<BoidPopulation>();
            int spawn = 0;
            if (!population.Started)
            {
                spawn = StartCount;
                population.Started = true;
            }
            else if (population.Delta > 0)
            {
                spawn = population.Delta;
            }
            else if (population.Delta < 0)
            {
                var boids = m_BoidQuery.ToEntityArray(Allocator.Temp);
                int remove = math.min(-population.Delta, boids.Length);
                state.EntityManager.DestroyEntity(boids.GetSubArray(0, remove));
            }
            population.Delta = 0;

            if (spawn > 0)
                Spawn(ref state, spawn);

            population.Count = m_BoidQuery.CalculateEntityCount();
            SystemAPI.SetSingleton(population);
        }

        // splits `total` fish across the schools, placed on a sphere around each school
        void Spawn(ref SystemState state, int total)
        {
            var schools = m_SchoolQuery.ToComponentDataArray<BoidSchool>(Allocator.Temp);
            var schoolTransforms = m_SchoolQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
            int perSchool = total / schools.Length;
            if (perSchool == 0)
                return;

            var world = state.WorldUnmanaged;
            for (int s = 0; s < schools.Length; s++)
            {
                var boidEntities =
                    CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(perSchool, ref world.UpdateAllocator);
                state.EntityManager.Instantiate(schools[s].Prefab, boidEntities);

                m_LocalToWorldLookup.Update(ref state);
                var setBoidLocalToWorldJob = new SetBoidLocalToWorld
                {
                    LocalToWorldFromEntity = m_LocalToWorldLookup,
                    Entities = boidEntities,
                    Center = schoolTransforms[s].Position,
                    Radius = schools[s].InitialRadius
                };
                state.Dependency = setBoidLocalToWorldJob.Schedule(perSchool, 64, state.Dependency);
                state.Dependency.Complete();
            }

            // TODO: all Prefabs are currently forced to TransformUsageFlags.Dynamic by default, which means boids get a LocalTransform
            // they don't need. As a workaround, remove the component at spawn-time.
            state.EntityManager.RemoveComponent<LocalTransform>(m_NewBoidQuery);
        }
    }

    [BurstCompile]
    struct SetBoidLocalToWorld : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction] [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;

        public NativeArray<Entity> Entities;
        public float3 Center;
        public float Radius;

        public void Execute(int i)
        {
            var entity = Entities[i];
            var random = new Random(((uint)(entity.Index + i + 1) * 0x9F6ABC1));
            var dir = math.normalizesafe(random.NextFloat3() - new float3(0.5f, 0.5f, 0.5f));
            var pos = Center + (dir * Radius);
            var localToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(pos, quaternion.LookRotationSafe(dir, math.up()), new float3(1.0f, 1.0f, 1.0f))
            };
            LocalToWorldFromEntity[entity] = localToWorld;
        }
    }
}
