using System;
using System.Numerics;
using DefaultEcs;
using DefaultEcs.System;
using My3DEngine.Rendering;
using My3DEngine.Physics;

namespace My3DEngine.Ecs;

// Components
public readonly record struct NameComponent(string Value);
public struct TransformComponent
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static TransformComponent Identity => new() { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
}

public readonly record struct MeshComponent(); // Minimal slice: renderer owns a single cube mesh.
public readonly record struct MaterialComponent(); // Minimal slice: shader is fixed for now.

public struct CameraComponent
{
    public Vector3 Position;
    public Vector3 Target;
    public float VerticalFovRadians;
    public float Near;
    public float Far;
}

public readonly record struct RigidBodyComponent(PhysicsBody Body);

/// <summary>Updates transforms (physics -> ECS) after stepping the simulation.</summary>
public sealed class TransformSystem : ISystem<float>
{
    private readonly World _world;

    public TransformSystem(World world) => _world = world;

    public bool IsEnabled { get; set; } = true;

    public void Update(float dt)
    {
        // Pull physics pose into TransformComponent.
        foreach (var entity in _world.GetEntities().With<RigidBodyComponent>().With<TransformComponent>().AsEnumerable())
        {
            ref readonly var rb = ref entity.Get<RigidBodyComponent>();
            ref var tr = ref entity.Get<TransformComponent>();

            var pose = rb.Body.GetPose();
            tr.Position = pose.Position;
            // Design choice for minimal demo: keep visual rotation controllable by input,
            // so physics sync only updates position here.
        }
    }

    public void Dispose() { }
}

/// <summary>Minimal render system: renders the first camera + first mesh entity.</summary>
public sealed class RenderSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Renderer _renderer;

    public RenderSystem(World world, Renderer renderer)
    {
        _world = world;
        _renderer = renderer;
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(float dt)
    {
        // Find camera.
        CameraComponent? cam = null;
        foreach (var e in _world.GetEntities().With<CameraComponent>().AsEnumerable())
        {
            cam = e.Get<CameraComponent>();
            break;
        }
        if (cam is null) { return; }

        // Find something to draw.
        TransformComponent? model = null;
        foreach (var e in _world.GetEntities().With<TransformComponent>().With<MeshComponent>().AsEnumerable())
        {
            model = e.Get<TransformComponent>();
            break;
        }
        if (model is null) { return; }

        var c = cam.Value;
        var camera = new Camera(
            position: c.Position,
            target: c.Target,
            verticalFovRadians: c.VerticalFovRadians <= 0 ? (float)(Math.PI / 3) : c.VerticalFovRadians,
            near: c.Near <= 0 ? 0.1f : c.Near,
            far: c.Far <= 0 ? 200f : c.Far);

        var m = model.Value;
        var tr = new Transform(m.Position, m.Rotation, m.Scale == default ? Vector3.One : m.Scale);

        _renderer.Render(camera, tr);
    }

    public void Dispose() { }
}
