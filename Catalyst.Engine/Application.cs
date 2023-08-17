﻿using Catalyst.Engine.Graphics;
using Catalyst.Engine.UI;
using Silk.NET.Windowing;

namespace Catalyst.Engine;

public class Application : IDisposable
{
    private readonly List<ILayer> _layerStack = new();
    private readonly IWindow _window;

    private readonly GraphicsDevice _device;
    private readonly Renderer _renderer;
    private readonly ImGuiContext _guiContext;

    public Application()
    {
        _window = Window.Create(WindowOptions.DefaultVulkan);
        _window.Initialize();
        _device = new GraphicsDevice(_window);
        _renderer = new Renderer(_device, _window);
        _guiContext = new ImGuiContext(_device);
    }

    public void AttachLayer(ILayer layer)
    {
        _layerStack.Add(layer);
        layer.OnAttach();
    }
    
    public void Run()
    {
        _window.Render += DrawFrame;
        _window.Run();
        _device.WaitIdle();
        _window.Close();
    }

    private void DrawFrame(double deltaTime)
    {
        var cmd = _renderer.BeginFrame();
        _renderer.BeginRenderPass(cmd);
        foreach (var layer in _layerStack)
        {
            layer.OnUpdate(deltaTime);
            layer.OnDrawGui(deltaTime);
        }
        _renderer.EndRenderPass(cmd);
        _renderer.EndFrame();
    }
    public void Dispose()
    {
        _guiContext.Dispose();
        _renderer.Dispose();
        _device.Dispose();
        _window.Dispose();
        GC.SuppressFinalize(this);
    }
}