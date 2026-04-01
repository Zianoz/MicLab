Place the x64 RNNoise native library in this folder as:

`Native\rnnoise.dll`

MicFX is configured to copy that file to the build output directory with `CopyToOutputDirectory=PreserveNewest`.

Requirements:

- The DLL must export `rnnoise_create`, `rnnoise_destroy`, and `rnnoise_process_frame`
- The DLL must be the x64 build
- The MicFX audio pipeline runs at 48 kHz mono for RNNoise compatibility

After adding the file, rebuild the project:

```powershell
dotnet build
```
