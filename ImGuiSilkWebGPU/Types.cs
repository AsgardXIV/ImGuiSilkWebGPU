using Silk.NET.Core.Native;
using System.Numerics;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace ImGuiSilkWebGPU;

internal struct Uniforms
{
    public Matrix4x4 MVP;
    public float Gamma;
}

internal unsafe struct FrameRenderBuffer
{
    public ulong VertexBufferSize;
    public ulong IndexBufferSize;
    public Buffer* VertexBufferGpu;
    public Buffer* IndexBufferGpu;
    public GlobalMemory VertexBufferMemory;
    public GlobalMemory IndexBufferMemory;
};

internal struct WindowRenderBuffers
{
    public uint Index;
    public uint Count;
    public FrameRenderBuffer[] FrameRenderBuffers;
};