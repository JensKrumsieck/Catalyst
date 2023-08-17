﻿using Silk.NET.Vulkan;

namespace Catalyst.Allocation;

public readonly struct AllocatedImage
{
    public readonly Image Image;
    public readonly Allocation Allocation;
    public AllocatedImage(Image image, Allocation allocation)
    {
        Image = image;
        Allocation = allocation;
    }
}