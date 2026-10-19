using Citrus.Core;

namespace Citrus.Conversion;

/// <summary>Offline composition; discovery is always performed on a worker, including native capability probes.</summary>
public sealed record EngineRegistry(IReadOnlyList<IConversionEngine> Engines, IReadOnlyList<string> Warnings, InputInspector Inspector)
{
    public static Task<EngineRegistry> Create(OutputWriter writer, string toolsDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(writer);
            var process = new MediaProcess(Path.Combine(toolsDirectory, "ffmpeg.exe"), Path.Combine(toolsDirectory, "ffprobe.exe"));
            var engines = new List<IConversionEngine> { new ImageEngine(writer), new PdfEngine(writer), new SubtitleEngine(writer), new ArchiveEngine(outputWriter: writer) };
            var warnings = new List<string>();
            try
            {
                var media = await MediaEngine.CreateAsync(process, cancellationToken, writer).ConfigureAwait(false);
                if (media.Operations.Count == 0) throw new InvalidOperationException("No media operations were discovered.");
                engines.Add(media);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or System.Text.Json.JsonException)
            {
                warnings.Add("Audio and video tools are unavailable. Reinstall Citrus to restore the bundled media tools. Other formats remain available.");
            }
            return new EngineRegistry(engines.AsReadOnly(), warnings.AsReadOnly(), new InputInspector(process));
        }, cancellationToken);
}
