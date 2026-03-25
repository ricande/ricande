using System;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace My3DEngine.Physics;

/// <summary>
/// Thin wrapper around a BEPUphysics v2 Simulation.
/// Provides minimal API for a static ground + dynamic bodies and stepping.
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _pool = new();
    private readonly ThreadDispatcher _dispatcher;
    private readonly Simulation _simulation;

    public PhysicsWorld(Vector3 gravity)
    {
        _dispatcher = new ThreadDispatcher(Environment.ProcessorCount);
        _simulation = Simulation.Create(
            _pool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(gravity),
            new SolveDescription(velocityIterationCount: 8, substepCount: 1));
    }

    public PhysicsBody AddStaticBox(Vector3 position, Vector3 size)
    {
        var shape = new Box(size.X, size.Y, size.Z);
        var shapeHandle = _simulation.Shapes.Add(shape);
        var staticHandle = _simulation.Statics.Add(new StaticDescription(position, shapeHandle));
        return PhysicsBody.FromStatic(_simulation, staticHandle);
    }

    public PhysicsBody AddDynamicBox(Vector3 position, Vector3 size, float mass = 1f, float speculativeMargin = 0.01f)
    {
        var box = new Box(size.X, size.Y, size.Z);
        var inertia = box.ComputeInertia(mass);
        var shapeHandle = _simulation.Shapes.Add(box);
        var bodyHandle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(position, inertia, shapeHandle, speculativeMargin));
        return PhysicsBody.FromDynamic(_simulation, bodyHandle);
    }

    public PhysicsBody AddDynamicSphere(Vector3 position, float radius = 1f, float mass = 1f, float speculativeMargin = 0.01f)
    {
        var sphere = new Sphere(radius);
        var inertia = sphere.ComputeInertia(mass);
        var shapeHandle = _simulation.Shapes.Add(sphere);
        var bodyHandle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(position, inertia, shapeHandle, speculativeMargin));
        return PhysicsBody.FromDynamic(_simulation, bodyHandle);
    }

    public void StepSimulation(float dt)
    {
        if (dt <= 0) { return; }
        _simulation.Timestep(dt, _dispatcher);
    }

    public void Dispose()
    {
        _simulation.Dispose();
        _dispatcher.Dispose();
        _pool.Clear();
    }

    // BEPU requires callback structs; minimal, stable defaults.
    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
            => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        public bool ConfigureContactManifold<TManifold>(
            int workerIndex,
            CollidablePair pair,
            ref TManifold manifold,
            out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = 1f;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30, 1);
            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;

        public void Dispose() { }
    }

    private struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        public Vector3 Gravity;

        public PoseIntegratorCallbacks(Vector3 gravity) : this() => Gravity = gravity;

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        private Vector3Wide _gravityWideDt;

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
        }

        public void IntegrateVelocity(
            Vector<int> bodyIndices,
            Vector3Wide position,
            QuaternionWide orientation,
            BodyInertiaWide localInertia,
            Vector<int> integrationMask,
            int workerIndex,
            Vector<float> dt,
            ref BodyVelocityWide velocity)
        {
            velocity.Linear += _gravityWideDt;
        }
    }
}

/// <summary>Handle/adapter over either a dynamic body or a static.</summary>
public readonly struct PhysicsBody
{
    private readonly Simulation _simulation;
    private readonly BodyHandle _dynamicHandle;
    private readonly StaticHandle _staticHandle;
    private readonly bool _isDynamic;

    private PhysicsBody(Simulation simulation, BodyHandle dynamicHandle)
    {
        _simulation = simulation;
        _dynamicHandle = dynamicHandle;
        _staticHandle = default;
        _isDynamic = true;
    }

    private PhysicsBody(Simulation simulation, StaticHandle staticHandle)
    {
        _simulation = simulation;
        _staticHandle = staticHandle;
        _dynamicHandle = default;
        _isDynamic = false;
    }

    public static PhysicsBody FromDynamic(Simulation simulation, BodyHandle handle) => new(simulation, handle);
    public static PhysicsBody FromStatic(Simulation simulation, StaticHandle handle) => new(simulation, handle);

    public (Vector3 Position, Quaternion Orientation) GetPose()
    {
        if (_isDynamic)
        {
            var br = _simulation.Bodies[_dynamicHandle];
            return (br.Pose.Position, br.Pose.Orientation);
        }

        var sr = _simulation.Statics[_staticHandle];
        return (sr.Pose.Position, sr.Pose.Orientation);
    }
}
