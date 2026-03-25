using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace My3DEngine.Rendering;

/// <summary>
/// Minimal Veldrid-backed renderer targeting OpenGL first.
/// Encapsulates window, graphics device, swapchain and per-frame command recording.
/// </summary>
public sealed class Renderer : IDisposable
{
    private readonly Sdl2Window _window;
    private readonly GraphicsDevice _gd;
    private readonly CommandList _cl;
    private bool _resizePending;

    private readonly DeviceBuffer _vb;
    private readonly DeviceBuffer _ib;
    private readonly Shader[] _shaders;
    private readonly Pipeline _pipeline;

    // MVP uniform (std140 block). We keep it as 3 matrices to avoid packing surprises.
    private readonly DeviceBuffer _cameraBuffer;
    private readonly ResourceLayout _cameraLayout;
    private readonly ResourceSet _cameraSet;

    private Vector4 _clearColor = new(0.05f, 0.05f, 0.08f, 1f);

    public Renderer(string title, int width, int height, bool debug = false, bool vsync = true)
    {
        var windowCI = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = width,
            WindowHeight = height,
            WindowTitle = title,
        };

        var options = new GraphicsDeviceOptions
        {
            Debug = debug,
            SyncToVerticalBlank = vsync,
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            // Keep swapchain simple for now (no depth in the "first light" slice).
            HasMainSwapchain = true,
            SwapchainDepthFormat = null,
        };

        VeldridStartup.CreateWindowAndGraphicsDevice(
            windowCI,
            options,
            GraphicsBackend.OpenGL,
            out _window,
            out _gd);

        _window.Resized += () => _resizePending = true;

        _cl = _gd.ResourceFactory.CreateCommandList();

        // --- Shaders / pipeline (very small, production-ish: explicit resource layout) ---
        const string vertexCode = """
            #version 450

            layout(location = 0) in vec3 Position;
            layout(location = 1) in vec4 Color;

            // Vulkan-style GLSL so Veldrid.SPIRV can compile to SPIR-V.
            // Set/binding indices map to ResourceLayouts/ResourceLayoutElements.
            layout(set = 0, binding = 0, std140) uniform Camera
            {
                mat4 View;
                mat4 Projection;
                mat4 Model;
            };

            layout(location = 0) out vec4 fsin_Color;

            void main()
            {
                gl_Position = Projection * View * Model * vec4(Position, 1.0);
                fsin_Color = Color;
            }
            """;

        const string fragmentCode = """
            #version 450

            layout(location = 0) in vec4 fsin_Color;
            layout(location = 0) out vec4 fsout_Color;

            void main()
            {
                fsout_Color = fsin_Color;
            }
            """;

        var factory = _gd.ResourceFactory;

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4));

        var vsDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        var fsDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");
        _shaders = factory.CreateFromSpirv(vsDesc, fsDesc, new CrossCompileOptions(fixClipSpaceZ: true, invertVertexOutputY: false));

        _cameraLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Camera", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _cameraBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<CameraBlock>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _cameraSet = factory.CreateResourceSet(new ResourceSetDescription(_cameraLayout, _cameraBuffer));

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.CounterClockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _cameraLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
            Outputs = _gd.SwapchainFramebuffer.OutputDescription,
        };

        _pipeline = factory.CreateGraphicsPipeline(ref pipelineDesc);

        // --- Mesh data: a simple colored cube (12 triangles, indexed) ---
        var cube = MeshFactory.CreateColoredCube();
        _vb = factory.CreateBuffer(new BufferDescription((uint)(cube.Vertices.Length * Marshal.SizeOf<VertexPositionColor>()), BufferUsage.VertexBuffer));
        _ib = factory.CreateBuffer(new BufferDescription((uint)(cube.Indices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
        _gd.UpdateBuffer(_vb, 0, cube.Vertices);
        _gd.UpdateBuffer(_ib, 0, cube.Indices);
    }

    public bool WindowExists => _window.Exists;
    public (int Width, int Height) WindowSize => (_window.Width, _window.Height);
    public Vector2 MouseDelta { get; private set; }
    public bool IsLeftMouseDown { get; private set; }

    public void PumpEvents()
    {
        var snapshot = _window.PumpEvents();
        MouseDelta = _window.MouseDelta;
        IsLeftMouseDown = snapshot.IsMouseDown(MouseButton.Left);
        if (_resizePending)
        {
            _gd.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
            _resizePending = false;
        }
    }

    public void Render(in Camera camera, in Transform modelTransform)
    {
        var (w, h) = WindowSize;
        if (w <= 0 || h <= 0) { return; }

        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix((float)w / h);
        var model = modelTransform.WorldMatrix;

        // System.Numerics matrices are row-major. Veldrid uniform blocks map naturally when treated as 4x4 floats;
        // the shader expects standard mat4 multiplication order.
        var block = new CameraBlock(view, proj, model);
        _gd.UpdateBuffer(_cameraBuffer, 0, ref block);

        _cl.Begin();
        _cl.SetFramebuffer(_gd.SwapchainFramebuffer);
        _cl.ClearColorTarget(0, new RgbaFloat(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W));
        _cl.SetPipeline(_pipeline);
        _cl.SetGraphicsResourceSet(0, _cameraSet);
        _cl.SetVertexBuffer(0, _vb);
        _cl.SetIndexBuffer(_ib, IndexFormat.UInt16);
        _cl.DrawIndexed(
            indexCount: (uint)(MeshFactory.ColoredCubeIndexCount),
            instanceCount: 1,
            indexStart: 0,
            vertexOffset: 0,
            instanceStart: 0);
        _cl.End();

        _gd.SubmitCommands(_cl);
        _gd.SwapBuffers();
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        foreach (var s in _shaders) { s.Dispose(); }
        _cameraSet.Dispose();
        _cameraLayout.Dispose();
        _cameraBuffer.Dispose();
        _vb.Dispose();
        _ib.Dispose();
        _cl.Dispose();
        _gd.Dispose();
        _window.Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CameraBlock
    {
        public readonly Matrix4x4 View;
        public readonly Matrix4x4 Projection;
        public readonly Matrix4x4 Model;

        public CameraBlock(Matrix4x4 view, Matrix4x4 projection, Matrix4x4 model)
        {
            View = view;
            Projection = projection;
            Model = model;
        }
    }
}

/// <summary>Small camera helper for MVP.</summary>
public readonly struct Camera
{
    public readonly Vector3 Position;
    public readonly Vector3 Target;
    public readonly float VerticalFovRadians;
    public readonly float Near;
    public readonly float Far;

    public Camera(Vector3 position, Vector3 target, float verticalFovRadians = (float)(Math.PI / 3), float near = 0.1f, float far = 200f)
    {
        Position = position;
        Target = target;
        VerticalFovRadians = verticalFovRadians;
        Near = near;
        Far = far;
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
    public Matrix4x4 GetProjectionMatrix(float aspectRatio) => Matrix4x4.CreatePerspectiveFieldOfView(VerticalFovRadians, aspectRatio, Near, Far);
}

/// <summary>World transform backed by System.Numerics.</summary>
public readonly struct Transform
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly Vector3 Scale;

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public static Transform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 WorldMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);
}

/// <summary>Basic vertex for the minimal pipeline.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct VertexPositionColor
{
    public readonly Vector3 Position;
    public readonly Vector4 Color;

    public VertexPositionColor(Vector3 position, Vector4 color)
    {
        Position = position;
        Color = color;
    }
}

public static class MeshFactory
{
    public const int ColoredCubeIndexCount = 36;

    public static (VertexPositionColor[] Vertices, ushort[] Indices) CreateColoredCube()
    {
        // 8 vertices, per-vertex color; indices define 12 triangles.
        var v = new[]
        {
            new VertexPositionColor(new Vector3(-1, -1, -1), new Vector4(1, 0, 0, 1)),
            new VertexPositionColor(new Vector3( 1, -1, -1), new Vector4(0, 1, 0, 1)),
            new VertexPositionColor(new Vector3( 1,  1, -1), new Vector4(0, 0, 1, 1)),
            new VertexPositionColor(new Vector3(-1,  1, -1), new Vector4(1, 1, 0, 1)),
            new VertexPositionColor(new Vector3(-1, -1,  1), new Vector4(1, 0, 1, 1)),
            new VertexPositionColor(new Vector3( 1, -1,  1), new Vector4(0, 1, 1, 1)),
            new VertexPositionColor(new Vector3( 1,  1,  1), new Vector4(1, 1, 1, 1)),
            new VertexPositionColor(new Vector3(-1,  1,  1), new Vector4(0.2f, 0.2f, 0.2f, 1)),
        };

        var i = new ushort[]
        {
            // -Z
            0, 2, 1, 0, 3, 2,
            // +Z
            4, 5, 6, 4, 6, 7,
            // -X
            0, 7, 3, 0, 4, 7,
            // +X
            1, 2, 6, 1, 6, 5,
            // -Y
            0, 1, 5, 0, 5, 4,
            // +Y
            3, 7, 6, 3, 6, 2,
        };

        return (v, i);
    }
}
