using System;
using System.Numerics;
using DefaultEcs;
using DefaultEcs.System;
using My3DEngine.Ecs;
using My3DEngine.Physics;
using My3DEngine.Rendering;
using My3DEngine.Runtime;

// Minimal demo:
// - creates OpenGL window (Veldrid)
// - creates ECS world + systems
// - creates BEPU physics world with ground + a falling cube
// - renders a colored cube each frame

using var renderer = new Renderer("My3DEngine Demo (OpenGL/Veldrid)", 1280, 720, debug: false, vsync: true);
using var physics = new PhysicsWorld(gravity: new Vector3(0, -10, 0));

var world = new World();

// Camera entity.
var cam = world.CreateEntity();
cam.Set(new NameComponent("MainCamera"));
cam.Set(new CameraComponent
{
    Position = new Vector3(6, 4, 6),
    Target = Vector3.Zero,
    VerticalFovRadians = (float)(Math.PI / 3),
    Near = 0.1f,
    Far = 200f
});

// Physics: ground + a dynamic body.
physics.AddStaticBox(position: new Vector3(0, -1, 0), size: new Vector3(50, 1, 50));
var body = physics.AddDynamicBox(position: new Vector3(0, 3, 0), size: new Vector3(1, 1, 1), mass: 1f);

// Renderable entity (mesh is implicit for now; renderer draws a cube).
var cube = world.CreateEntity();
cube.Set(new NameComponent("Cube"));
cube.Set(TransformComponent.Identity);
cube.Set(new MeshComponent());
cube.Set(new MaterialComponent());
cube.Set(new RigidBodyComponent(body));

using ISystem<float> transformSystem = new TransformSystem(world);
using ISystem<float> renderSystem = new RenderSystem(world, renderer);

float yaw = 0f;
float pitch = 0f;
const float mouseSensitivity = 0.01f; // radians per pixel-ish

GameLoop.Run(
    shouldContinue: () => renderer.WindowExists,
    pumpEvents: renderer.PumpEvents,
    update: dt =>
    {
        physics.StepSimulation(dt);
        transformSystem.Update(dt);

        if (renderer.IsLeftMouseDown)
        {
            var d = renderer.MouseDelta;
            yaw += d.X * mouseSensitivity;
            pitch += d.Y * mouseSensitivity;

            // Clamp pitch to avoid flipping.
            pitch = Math.Clamp(pitch, -(float)Math.PI * 0.49f, (float)Math.PI * 0.49f);

            ref var tr = ref cube.Get<TransformComponent>();
            tr.Rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0f);
        }
    },
    render: () =>
    {
        // Render system pulls camera+mesh entity and draws.
        renderSystem.Update(0);
    },
    targetFps: 60);
