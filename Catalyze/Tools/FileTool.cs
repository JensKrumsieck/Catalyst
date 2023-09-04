﻿using System.Reflection;

namespace Catalyze.Tools;

public class FileTool
{
    public static byte[] ReadBytesFromResource(string filename)
    {
        var asm = Assembly.GetCallingAssembly();
        var resName = asm.GetName().Name + "." + filename; 
        if(!asm.GetManifestResourceNames().Contains(resName))
            throw new ApplicationException($"Could not find resource for {filename}");
        using var stream = asm.GetManifestResourceStream(resName);
        using var ms = new MemoryStream();
        if (stream is null) return Array.Empty<byte>();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}