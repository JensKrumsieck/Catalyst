﻿using Silk.NET.Vulkan;

namespace Catalyst.Allocation;

public static class AllocatorExtensions
{
    public static void AllocateImage(this IAllocator allocator, Image image, AllocationCreateInfo allocInfo, out Allocation allocation)
    {
        vk.GetImageMemoryRequirements(allocator.Device, image, out var memReq);
        allocInfo.Size = memReq.Size;
        allocInfo.MemoryTypeIndex = allocator.FindMemoryType(memReq.MemoryTypeBits, allocInfo.Usage);
        allocator.Allocate(allocInfo, out allocation);
        vk.BindImageMemory(allocator.Device, image, allocation.AllocatedMemory, 0);
    }
}