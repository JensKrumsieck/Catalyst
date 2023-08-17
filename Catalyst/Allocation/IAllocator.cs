﻿using Silk.NET.Vulkan;

namespace Catalyst.Allocation;

public unsafe interface IAllocator : IDisposable
{
    public Device Device { get; }

    void Allocate(AllocationCreateInfo createInfo, out Allocation alloc);
    void Free(Allocation alloc);
    Result MapMemory(DeviceMemory deviceMemory, ulong offset, ulong size, ref void* pMappedData);
    void UnmapMemory(DeviceMemory deviceMemory);
    
    ulong AllocatedSize(uint memoryType);
    uint NumberOfAllocations();

    public uint FindMemoryType(uint filter, MemoryPropertyFlags flags)
    {
        vk.GetPhysicalDeviceMemoryProperties(Device.PhysicalDevice, out var properties);
        for (var i = 0; i < properties.MemoryTypeCount; i++)
        {
            if ((filter & (uint)(1 << i)) != 0u && (properties.MemoryTypes[i].PropertyFlags & flags) == flags)
                return (uint)i;
        }
        throw new Exception("unable to find suitable memory type");
    }
}