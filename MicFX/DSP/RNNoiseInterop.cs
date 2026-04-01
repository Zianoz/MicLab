using System.Runtime.InteropServices;

namespace MicFX.DSP;

internal static class RNNoiseInterop
{
    private const string DllName = "rnnoise.dll";

    public static IntPtr Create()
    {
        try
        {
            IntPtr state = rnnoise_create(IntPtr.Zero);
            if (state == IntPtr.Zero)
                throw new InvalidOperationException("rnnoise_create returned a null state.");

            return state;
        }
        catch (DllNotFoundException ex)
        {
            throw new DllNotFoundException("rnnoise.dll was not found. Place the x64 rnnoise.dll at Native\\rnnoise.dll so MicFX can copy it to the output directory.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new BadImageFormatException("rnnoise.dll could not be loaded. Ensure the native binary is the x64 build.", ex);
        }
    }

    public static void Destroy(IntPtr state)
    {
        if (state != IntPtr.Zero)
            rnnoise_destroy(state);
    }

    public static float ProcessFrame(IntPtr state, float[] output, float[] input)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);

        if (output.Length < 480)
            throw new ArgumentException("RNNoise output frames must be 480 samples.", nameof(output));

        if (input.Length < 480)
            throw new ArgumentException("RNNoise input frames must be 480 samples.", nameof(input));

        return rnnoise_process_frame(state, output, input);
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void rnnoise_destroy(IntPtr state);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern float rnnoise_process_frame(
        IntPtr state,
        [Out] float[] output,
        [In] float[] input);
}
