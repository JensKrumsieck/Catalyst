﻿using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Catalyst.Allocation;
using Catalyst.Engine.Graphics;
using Catalyst.Pipeline;
using Catalyst.Tools;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Vulkan;

namespace Catalyst.Engine.UI;

public unsafe class ImGuiContext : IDisposable
{
    private readonly Renderer _renderer;
    private readonly DescriptorPool _descriptorPool;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly DescriptorSet _descriptorSet;
    private readonly ShaderEffect _shaderEffect;
    private readonly ShaderPass _shaderPass;
    private readonly Sampler _fontSampler;
    private readonly AllocatedImage _fontImage;
    private readonly ImageView _fontView;

    private Buffer[]? _vertexBuffers = null!;
    private Buffer[]? _indexBuffers = null!;
    
    private readonly IInputContext _input;
    private readonly IKeyboard _keyboard;
    private readonly List<char> _pressedChars = new();
    private Key[]? _allKeys = null!;
    
    public ImGuiContext(Renderer renderer, IInputContext input)
    {
        _renderer = renderer;
        _input = input;
        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        var io = ImGui.GetIO();

        var data = FileTool.ReadBytesFromResource("Assets.Roboto-Regular.ttf");
        var mData = data.AsMemory(0);
        using var hData = mData.Pin();
        io.Fonts.AddFontFromMemoryTTF((nint)hData.Pointer,data.Length,14);
        
        io.Fonts.AddFontDefault();
        
        io.Fonts.GetTexDataAsRGBA32(out nint pixels, out var width, out var height);
        _descriptorPool = new DescriptorPool(_renderer.Device.Device, new[] {new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)});
        var samplerCreateInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MinLod = -1000,
            MaxLod = 1000,
            MaxAnisotropy = 1.0f
        };
        _fontSampler = _renderer.Device.CreateSampler(samplerCreateInfo);
        _descriptorSetLayout = DescriptorSetLayoutBuilder
                               .Start()
                               .WithSampler(0, DescriptorType.CombinedImageSampler, ShaderStageFlags.FragmentBit, _fontSampler)
                               .CreateOn(_renderer.Device.Device);
        _descriptorSet = _descriptorPool.AllocateDescriptorSet(new[] {_descriptorSetLayout});
        var pushRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = sizeof(float) * 4
        };
        _shaderEffect = ShaderEffect.BuildEffect(_renderer.Device.Device, UIShaders.VertexShader, UIShaders.FragmentShader,
                                                 new[] {_descriptorSetLayout}, new[] {pushRange});
        var vertexInfo = new VertexInfo(
                                        new VertexInputBindingDescription[]
                                        {
                                            new(0, (uint) Unsafe.SizeOf<ImDrawVert>(), VertexInputRate.Vertex)
                                        },
                                        new VertexInputAttributeDescription[]
                                        {
                                            new(0, 0, Format.R32G32Sfloat, (uint) Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos))),
                                            new(1, 0, Format.R32G32Sfloat, (uint) Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv))),
                                            new(2, 0, Format.R8G8B8A8Unorm, (uint) Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col)))
                                        });
        var passInfo = ShaderPassInfo.Default();
        passInfo.EnableAlphaBlend();
        passInfo.NoDepthTesting();
        _shaderPass = new ShaderPass(_renderer.Device.Device, _shaderEffect, passInfo, vertexInfo, _renderer.RenderPass);

        var imageExtent = new Extent3D((uint) width, (uint) height, 1);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
            Extent = imageExtent
        };
        _fontImage = _renderer.Device.CreateImage(imageInfo, MemoryPropertyFlags.DeviceLocalBit);
        var imageViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _fontImage.Image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        _fontView = _renderer.Device.CreateImageView(imageViewInfo);
        
        var descriptorImageInfo = new DescriptorImageInfo
        {
            Sampler = _fontSampler,
            ImageView = _fontView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };
        _descriptorPool.UpdateDescriptorSetImage(ref _descriptorSet, descriptorImageInfo,
                                                 DescriptorType.CombinedImageSampler);
        var instanceCount = (uint) (width * height);
        using var stagingBuffer = _renderer.Device.CreateBuffer(sizeof(uint), instanceCount, BufferUsageFlags.TransferSrcBit,
                                               MemoryPropertyFlags.HostVisibleBit);
        stagingBuffer.Map().Validate();
        stagingBuffer.WriteToBuffer(pixels.ToPointer());
        stagingBuffer.Flush().Validate();
        stagingBuffer.Unmap();

        _renderer.Device.TransitionImageLayout(_fontImage.Image, Format.B8G8R8A8Unorm, ImageLayout.Undefined,
                                      ImageLayout.TransferDstOptimal, 1, 1);

        _renderer.Device.CopyBufferToImage(stagingBuffer, _fontImage.Image, ImageLayout.TransferDstOptimal, Format.R8G8B8A8Unorm, imageExtent);
        _renderer.Device.TransitionImageLayout(_fontImage.Image, Format.B8G8R8A8Unorm, ImageLayout.TransferDstOptimal,
                                      ImageLayout.ShaderReadOnlyOptimal, 1, 1);
        io.Fonts.SetTexID((nint)_fontImage.Image.Handle);
        
        SetKeyMappings();
        SetFrameData();
        
        _keyboard = _input.Keyboards[0];
        _keyboard.KeyChar += OnKeyChar;
    }
    private void SetKeyMappings()
    {
        var io = ImGui.GetIO();
        io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.Tab;
        io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.Left;
        io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.Right;
        io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.Up;
        io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.Down;
        io.KeyMap[(int)ImGuiKey.PageUp] = (int)Key.PageUp;
        io.KeyMap[(int)ImGuiKey.PageDown] = (int)Key.PageDown;
        io.KeyMap[(int)ImGuiKey.Home] = (int)Key.Home;
        io.KeyMap[(int)ImGuiKey.End] = (int)Key.End;
        io.KeyMap[(int)ImGuiKey.Delete] = (int)Key.Delete;
        io.KeyMap[(int)ImGuiKey.Backspace] = (int)Key.Backspace;
        io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.Enter;
        io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.Escape;
        io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
        io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
        io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
        io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
        io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
        io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;
    }
    
    private void OnKeyChar(IKeyboard kb, char @char) => _pressedChars.Add(@char);

    private void SetFrameData(float deltaTime = 1f/60f)
    {;
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_renderer.Window.Size.X, _renderer.Window.Size.Y);
        if (_renderer.Window.Size is {X: > 0, Y: > 0})
            io.DisplayFramebufferScale = new Vector2(_renderer.Window.FramebufferSize.X / (float) _renderer.Window.Size.X,
                                                     _renderer.Window.FramebufferSize.Y / (float) _renderer.Window.Size.Y);
        io.DeltaTime = deltaTime;
    }
    
    private void UpdateImGuiInput()
    {
        var io = ImGui.GetIO();

        var mouseState = _input.Mice[0].CaptureState();
        var keyboardState = _input.Keyboards[0];

        io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
        io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
        io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

        var point = new Point((int)mouseState.Position.X, (int)mouseState.Position.Y);
        io.MousePos = new Vector2(point.X, point.Y);

        var wheel = mouseState.GetScrollWheels()[0];
        io.MouseWheel = wheel.Y;
        io.MouseWheelH = wheel.X;

        _allKeys ??= (Key[]?) Enum.GetValues(typeof(Key));
        foreach (var key in _allKeys!)
        {
            if (key == Key.Unknown) continue;
            io.KeysDown[(int)key] = keyboardState.IsKeyPressed(key);
        }

        foreach (var c in _pressedChars) io.AddInputCharacter(c);
        _pressedChars.Clear();

        io.KeyCtrl = keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight);
        io.KeyAlt = keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight);
        io.KeyShift = keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight);
        io.KeySuper = keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight);
    }

    public void Update(float deltaTime)
    {
        UpdateImGuiInput();
    }

    public void Render(CommandBuffer cmd)
    {
        ImGui.Render();
        RenderDrawData(cmd, ImGui.GetDrawData());
    }

    private void RenderDrawData(CommandBuffer cmd, in ImDrawDataPtr drawDataPtr)
    {
        var framebufferWidth = (int) drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X;
        var frameBufferHeight = (int) drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y;
        if (framebufferWidth <= 0 || frameBufferHeight <= 0) return;

        var drawData = *drawDataPtr.NativePtr;
        if (_vertexBuffers is null || _indexBuffers is null)
        {
            _vertexBuffers = new Buffer[_renderer.ImageCount];
            _indexBuffers = new Buffer[_renderer.ImageCount];
        }

        ref var vertexBuffer = ref _vertexBuffers[_renderer.CurrentImageIndex];
        ref var indexBuffer = ref _indexBuffers[_renderer.CurrentImageIndex];

        if (drawData.TotalVtxCount > 0)
        {
            var vertexSize = (ulong) drawData.TotalVtxCount * (ulong) sizeof(ImDrawVert);
            var indexSize = (ulong) drawData.TotalIdxCount * (ulong) sizeof(ushort);
            if (vertexBuffer.Handle == default || vertexBuffer.BufferSize < vertexSize)
                CreateOrResizeBuffer(ref vertexBuffer, (uint) drawData.TotalVtxCount, (uint) sizeof(ImDrawVert),
                                     BufferUsageFlags.VertexBufferBit);
            if (indexBuffer.Handle == default || indexBuffer.BufferSize < indexSize)
                CreateOrResizeBuffer(ref indexBuffer, (uint) drawData.TotalIdxCount, sizeof(ushort),
                                     BufferUsageFlags.IndexBufferBit);

            vertexBuffer.Map().Validate();
            indexBuffer.Map().Validate();
            ulong vtxOffset = 0;
            ulong idxOffset = 0;
            for (var n = 0; n < drawData.CmdListsCount; n++)
            {
                ref var cmdList = ref drawData.CmdLists[n];
                ulong vtxChunkSize = (uint) cmdList->VtxBuffer.Size * (uint) sizeof(ImDrawVert);
                ulong idxChunkSize = (uint) cmdList->IdxBuffer.Size * sizeof(ushort);
                vertexBuffer.WriteToBuffer(cmdList->VtxBuffer.Data.ToPointer(), vtxChunkSize, vtxOffset);
                indexBuffer.WriteToBuffer(cmdList->IdxBuffer.Data.ToPointer(), idxChunkSize, idxOffset);
                vtxOffset += vtxChunkSize;
                idxOffset += idxChunkSize;
            }

            vertexBuffer.Flush().Validate();
            indexBuffer.Flush().Validate();
            vertexBuffer.Unmap();
            indexBuffer.Unmap();
        }

        cmd.BindGraphicsPipeline(_shaderPass);
        cmd.BindGraphicsDescriptorSets(_descriptorSet, _shaderEffect);

        if (drawData.TotalVtxCount > 0)
        {
            cmd.BindVertexBuffer(vertexBuffer);
            cmd.BindIndexBuffer(indexBuffer, IndexType.Uint16);
        }

        //imGui viewport & scissor
        var viewport = new Viewport(0, 0, framebufferWidth, frameBufferHeight, 0, 1);
        cmd.SetViewport(viewport);
        var scale = stackalloc float[2];
        scale[0] = 2.0f / drawData.DisplaySize.X;
        scale[1] = 2.0f / drawData.DisplaySize.Y;
        var translate = stackalloc float[2];
        translate[0] = -1.0f - drawData.DisplayPos.X * scale[0];
        translate[1] = -1.0f - drawData.DisplayPos.Y * scale[1];

        cmd.PushConstants(_shaderEffect, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
        cmd.PushConstants(_shaderEffect, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

        var clipOff = drawData.DisplayPos;
        var clipScale = drawData.FramebufferScale;

        var vertexOffset = 0;
        var indexOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            ref var cmdList = ref drawData.CmdLists[n];
            for (var i = 0; i < cmdList->CmdBuffer.Size; i++)
            {
                ref var pCmd = ref cmdList->CmdBuffer.Ref<ImDrawCmd>(i);
                var clipRect = new Vector4
                {
                    X = (pCmd.ClipRect.X - clipOff.X) * clipScale.X,
                    Y = (pCmd.ClipRect.Y - clipOff.Y) * clipScale.Y,
                    Z = (pCmd.ClipRect.Z - clipOff.X) * clipScale.X,
                    W = (pCmd.ClipRect.W - clipOff.Y) * clipScale.Y,
                };
                if (clipRect.X < framebufferWidth
                    && clipRect.Y < frameBufferHeight
                    && clipRect.Z >= .0f
                    && clipRect.W >= .0f)
                {
                    if (clipRect.X < 0.0f) clipRect.X = 0.0f;
                    if (clipRect.Y < 0.0f) clipRect.Y = 0.0f;


                    var scissor = new Rect2D(
                                             new Offset2D((int) clipRect.X, (int) clipRect.Y),
                                             new Extent2D((uint) (clipRect.Z - clipRect.X),
                                                          (uint) (clipRect.W - clipRect.Y))
                                            );
                    cmd.SetScissor(scissor);
                    cmd.DrawIndexed(pCmd.ElemCount, 1, pCmd.IdxOffset + (uint) indexOffset,
                                    (int) pCmd.VtxOffset + vertexOffset, 0);

                }
            }
            indexOffset += cmdList->IdxBuffer.Size;
            vertexOffset += cmdList->VtxBuffer.Size;
        }
    }

    private void CreateOrResizeBuffer(ref Buffer buffer, uint instanceSize, uint instanceCount, BufferUsageFlags usage)
    {
        if(buffer.Handle != default) buffer.Dispose();
        buffer = _renderer.Device.CreateBuffer(instanceSize, instanceCount, usage, MemoryPropertyFlags.HostVisibleBit);
    }
    
    public void Dispose()
    {
        _keyboard.KeyChar -= OnKeyChar;
        
        _renderer.Device.DestroyImage(_fontImage);
        _renderer.Device.DestroySampler(_fontSampler);
        _renderer.Device.DestroyImageView(_fontView);
        
        _descriptorPool.FreeDescriptorSet(_descriptorSet);
        _descriptorPool.Dispose();
        _shaderPass.Dispose();
        _descriptorSetLayout.Dispose();
        _shaderEffect.Dispose();

        if (_indexBuffers is not null && _vertexBuffers is not null)
        {
            for (var i = 0; i < _vertexBuffers?.Length; i++)
            {
                _vertexBuffers[i].Dispose();
                _indexBuffers![i].Dispose();
            }
            Array.Clear(_vertexBuffers!);
            Array.Clear(_indexBuffers!);
        }
        
        GC.SuppressFinalize(this);
    }
}