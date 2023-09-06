using System.IO;
using System.Reflection;
using System;

namespace ImGuiSilkWebGPU.Sample;

internal class Helpers
{
    public static byte[] ReadResource(string name)
    {
        Assembly assembly = typeof(Helpers).Assembly;
        Stream stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + name.Replace("/", ".")) ?? throw new Exception($"Unable to find resource {name}.");
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        return buffer;
    }
}
