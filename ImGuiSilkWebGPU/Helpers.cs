namespace ImGuiSilkWebGPU;

internal class Helpers
{
    public static int Align(int size, int align) => (((size) + ((align) - 1)) & ~((align) - 1));
}
