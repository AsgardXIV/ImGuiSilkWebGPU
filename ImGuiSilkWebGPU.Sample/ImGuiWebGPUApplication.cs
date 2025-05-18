using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.Numerics;
using Color = Silk.NET.WebGPU.Color;

namespace ImGuiSilkWebGPU.Sample;

internal unsafe class ImGuiWebGPUApplication
{
    private IWindow _window;

    private WebGPU _webGpu;

    private Instance* _instance;
    private Surface* _surface;
    private Adapter* _adapter;
    private Device* _device;
    private TextureFormat _swapchainFormat;

    private Queue* _queue;
    private Texture* _catTexture;
    private TextureView* _catTextureView;

    private ImGuiController _imGuiController;

    public void Run()
    {
        GlfwWindowing.RegisterPlatform();
        GlfwInput.RegisterPlatform();

        InitWindow();
        MainLoop();
        Cleanup();
    }

    private void InitWindow()
    {
        _window = Window.Create(WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            ShouldSwapAutomatically = false,
            IsContextControlDisabled = true,
            Position = new Vector2D<int>(100, 100),
            Size = new Vector2D<int>(1024, 768)
        });

        _window.Render += Render;
        _window.FramebufferResize += FrameBufferResize;
        _window.Load += InitApplication;
    }

    private void InitApplication()
    {
        InitWebGPU();
        InitImGui();
        CreateCustomTextureView();
    }

    private void InitImGui()
    {
        _imGuiController = new ImGuiController(_webGpu, _device, _window, _window.CreateInput(), 2, _swapchainFormat, null);
    }

    private void InitWebGPU()
    {
        _webGpu = WebGPU.GetApi();

        var instanceDescriptor = new InstanceDescriptor();
        _instance = _webGpu.CreateInstance(ref instanceDescriptor);
        _surface = _window.CreateWebGPUSurface(_webGpu, _instance);

        var requestAdapterOptions = new RequestAdapterOptions
        {
            CompatibleSurface = _surface,
            PowerPreference = PowerPreference.HighPerformance,
            ForceFallbackAdapter = false
        };

        _webGpu.InstanceRequestAdapter(_instance, ref requestAdapterOptions, new PfnRequestAdapterCallback((status, adapter, message, userData) =>
        {
            if (status != RequestAdapterStatus.Success)
            {
                throw new Exception($"Unable to create adapter: {SilkMarshal.PtrToString((nint)message)}");
            }

            _adapter = adapter;
        }), null);

        var deviceDescriptor = default(DeviceDescriptor);
        _webGpu.AdapterRequestDevice(_adapter, ref deviceDescriptor, new PfnRequestDeviceCallback((status, device, message, userData) =>
        {
            if (status != RequestDeviceStatus.Success)
                throw new Exception($"Unable to create device: {SilkMarshal.PtrToString((nint)message)}");

            _device = device;
        }), null);

        _webGpu.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(UncapturedError), null);

        _queue = _webGpu.DeviceGetQueue(_device);

        CreateOrUpdateSwapchain();
    }

    private void CreateOrUpdateSwapchain()
    {
        if (_window.WindowState == WindowState.Minimized || _window.Size.X <= 0 || _window.Size.Y <= 0)
            return;

        _swapchainFormat = _webGpu.SurfaceGetPreferredFormat(_surface, _adapter);

        var surfaceConfig = new SurfaceConfiguration()
        {
            Usage = TextureUsage.RenderAttachment,
            Format = _swapchainFormat,
            PresentMode = PresentMode.Fifo,
            Device = _device,
            Width = (uint)_window.FramebufferSize.X,
            Height = (uint)_window.FramebufferSize.Y,
        };

        _webGpu.SurfaceConfigure(_surface, ref surfaceConfig);
    }

    private void CreateCustomTextureView()
    {
        byte[] rawImage = Helpers.ReadResource("Assets/CatUmbrella.png");
        using Image<Rgba32> image = Image.Load<Rgba32>(rawImage);

        TextureFormat viewFormat = TextureFormat.Rgba8Unorm;

        TextureDescriptor descriptor = new()
        {
            Size = new Extent3D((uint)image.Width, (uint)image.Height, 1),
            Format = viewFormat,
            Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            ViewFormats = &viewFormat,
            ViewFormatCount = 1
        };

        _catTexture = _webGpu.DeviceCreateTexture(_device, ref descriptor);

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = viewFormat,
            Dimension = TextureViewDimension.Dimension2D,
            Aspect = TextureAspect.All,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            BaseArrayLayer = 0,
            BaseMipLevel = 0
        };

        _catTextureView = _webGpu.TextureCreateView(_catTexture, ref viewDescriptor);

        Rgba32[] pixelArray = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixelArray);

        ImageCopyTexture imageCopyTexture = new()
        {
            Texture = _catTexture,
            Aspect = TextureAspect.All,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0)
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = (uint)(image.Width * sizeof(Rgba32)),
            RowsPerImage = (uint)image.Height
        };

        Extent3D extent = new()
        {
            Width = (uint)image.Width,
            Height = (uint) image.Height,
            DepthOrArrayLayers = 1
        };

        fixed (void* dataPtr = pixelArray)
        {
            _webGpu.QueueWriteTexture(_queue, &imageCopyTexture, dataPtr, (nuint)(sizeof(Rgba32) * image.Width * image.Height), ref layout, ref extent);
        }

        _imGuiController.BindImGuiTextureView(_catTextureView);
    }

    private void MainLoop()
    {
        _window.Run();
    }

    private void Render(double delta)
    {
        SurfaceTexture surfaceTexture;
        _webGpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        
        switch (surfaceTexture.Status)
        {
            case SurfaceGetCurrentTextureStatus.Success:
                break;
            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                _webGpu.TextureRelease(surfaceTexture.Texture);
                CreateOrUpdateSwapchain();
                return;
            default:
                throw new Exception($"Error... {surfaceTexture.Status}");
        }
        
        var renderView = _webGpu.TextureCreateView(surfaceTexture.Texture, null);

        var commandEncoderDescriptor = new CommandEncoderDescriptor();
        CommandEncoder* encoder = _webGpu.DeviceCreateCommandEncoder(_device, ref commandEncoderDescriptor);

        RenderPassColorAttachment colorAttachment = new()
        {
            View = renderView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 0)
        };

        var renderPassDescriptor = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        };
        RenderPassEncoder* renderPass = _webGpu.CommandEncoderBeginRenderPass(encoder, ref renderPassDescriptor);

        _imGuiController.Update((float)delta);

        DrawImGui();

        _imGuiController.Render(renderPass);

        _webGpu.RenderPassEncoderEnd(renderPass);

        _webGpu.RenderPassEncoderRelease(renderPass);

        _webGpu.TextureViewRelease(renderView);

        var commandBufferDescriptor = new CommandBufferDescriptor();
        CommandBuffer* commandBuffer = _webGpu.CommandEncoderFinish(encoder, ref commandBufferDescriptor);
        _webGpu.QueueSubmit(_queue, 1, &commandBuffer);

        _webGpu.CommandEncoderRelease(encoder);
        _webGpu.CommandBufferReference(commandBuffer);

        _webGpu.SurfacePresent(_surface);
        _window.SwapBuffers();
    }

    private void DrawImGui()
    {
        ImGui.ShowDemoWindow();

        ImGui.Begin("Image Test");
        ImGui.Image((nint)_catTextureView, new Vector2(512, 288));
        ImGui.End();
    }

    private void FrameBufferResize(Vector2D<int> obj)
    {
        CreateOrUpdateSwapchain();
    }

    private void Cleanup()
    {
        _imGuiController.Dispose();

        _window.Load -= InitApplication;
        _window.Render -= Render;
        _window.FramebufferResize -= FrameBufferResize;

        _webGpu.AdapterRelease(_adapter);
        _webGpu.SurfaceRelease(_surface);
        _webGpu.InstanceRelease(_instance);
        _webGpu.Dispose();
    }

    private void UncapturedError(ErrorType arg0, byte* arg1, void* arg2)
    {
        var msg = $"GPU Error: {arg0}: {SilkMarshal.PtrToString((nint)arg1)}";
        Debug.WriteLine(msg);
        Console.WriteLine(msg);
    }
}
