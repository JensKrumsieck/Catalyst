﻿using System.Runtime.InteropServices;
using Catalyze.Tools;
using Silk.NET.Vulkan;

namespace Catalyze.Allocation;

public sealed unsafe class PassthroughAllocator : IAllocator
{
    private struct AllocatorState
    {
        public ulong* MemoryTypeAllocSizes;
        public uint TotalAllocations;
        public GraphicsDevice Context;
    }

    private AllocatorState _state;
    public GraphicsDevice Device => _state.Context;
    
    public void Bind(GraphicsDevice device)
    {
        _state.Context = device;
        vk.GetPhysicalDeviceMemoryProperties(device.VkPhysicalDevice, out var memoryProperties);
        _state.MemoryTypeAllocSizes = (ulong*) Marshal.AllocHGlobal((nint) (sizeof(uint) * memoryProperties.MemoryTypeCount));
    }

    private void Deactivate() => Marshal.FreeHGlobal((nint) _state.MemoryTypeAllocSizes);

    public void Allocate(AllocationCreateInfo createInfo, out Allocation alloc)
    {
        _state.TotalAllocations++;
        _state.MemoryTypeAllocSizes[createInfo.MemoryTypeIndex] += createInfo.Size;
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = createInfo.Size,
            MemoryTypeIndex = createInfo.MemoryTypeIndex
        };
        vk.AllocateMemory(_state.Context.VkDevice, allocInfo, null, out var allocatedMemory).Validate();
        alloc = new Allocation(this, allocatedMemory, createInfo.MemoryTypeIndex, 0, createInfo.Size, 0);
    }

    public void Free(Allocation alloc)
    {
        _state.TotalAllocations--;
        _state.MemoryTypeAllocSizes[alloc.Type] -= alloc.Size;
        vk.FreeMemory(_state.Context.VkDevice, alloc.AllocatedMemory, null);
    }

    public ulong AllocatedSize(uint memoryType) => _state.MemoryTypeAllocSizes[memoryType];

    public uint NumberOfAllocations() => _state.TotalAllocations;

    public void Dispose() => Deactivate();
}