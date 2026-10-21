using System.Security.Cryptography;
using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing;

public sealed class EditorOutputPublisher(OutputWriter writer, ExportTargetResolver resolver, IEditorPayloadValidator validator)
{
    public async Task<ConversionResult> PublishAsync(OperationRequest request, StagedExport staged, string privateDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ExportTarget target = resolver.Resolve(request);
        string root = Path.GetFullPath(privateDirectory);
        string payload = Path.Combine(root,"payload");
        ValidateMembers(request,staged,target,payload);
        // The required family validator independently decodes/inspects actual bytes, never renderer assertions.
        var hashes = new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in staged.Files)
        {
            await using var input = File.OpenRead(Path.Combine(payload,member.RelativePath));
            hashes.Add(member.RelativePath,await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
        }
        await validator.ValidateAsync(request,staged,root,ct).ConfigureAwait(false);
        ValidateMembers(request,staged,target,payload);
        string desired = Path.Combine(target.Directory,target.Stem + target.Extension);
        string output;
        if (target.Shape == ExportShape.File)
            output = await writer.WriteFileAsync(desired,(temporary,token) => CopyVerified(staged.Files[0],temporary,token),ct).ConfigureAwait(false);
        else
            output = await writer.WriteDirectoryAsync(desired,async (temporary,token) =>
            {
                foreach (var member in staged.Files) await CopyVerified(member,Path.Combine(temporary,member.RelativePath),token).ConfigureAwait(false);
            },ct).ConfigureAwait(false);
        return new([output],staged.Compression,staged.Warnings.IsDefault ? [] : staged.Warnings);

        async Task CopyVerified(StagedOutput member,string destination,CancellationToken token)
        {
            string inputPath = Path.Combine(payload,member.RelativePath);
            SafeEditorFiles.RejectLinks(inputPath);
            await using var input = new FileStream(inputPath,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,FileOptions.Asynchronous);
            await using var sink = new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128*1024]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer,token).ConfigureAwait(false)) > 0)
            {
                total = checked(total + read);
                if (total > member.Bytes) throw new IOException("Payload grew after validation.");
                hash.AppendData(buffer,0,read);
                await sink.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);
            }
            if (total != member.Bytes || !hash.GetHashAndReset().AsSpan().SequenceEqual(hashes[member.RelativePath])) throw new IOException("Payload changed after validation.");
        }
    }

    private static void ValidateMembers(OperationRequest request,StagedExport staged,ExportTarget target,string payload)
    {
        if (staged.Shape != target.Shape || staged.Files.IsDefaultOrEmpty || (target.Shape == ExportShape.File && staged.Files.Length != 1)) throw new InvalidDataException("Output shape or member count disagrees with the recipe.");
        SafeEditorFiles.RejectLinks(payload);
        if (!Directory.Exists(payload)) throw new InvalidDataException("Payload directory is missing.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var member in staged.Files)
        {
            if (!EditorPathPolicy.IsLeaf(member.RelativePath) || !names.Add(member.RelativePath) || !Path.GetExtension(member.RelativePath).Equals("." + request.Editor!.Export.FormatId,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid, duplicate or incompatible payload filename.");
            string path = Path.Combine(payload,member.RelativePath);
            SafeEditorFiles.RejectLinks(path);
            if (!File.Exists(path) || Directory.Exists(path) || member.Bytes < 0 || new FileInfo(path).Length != member.Bytes) throw new InvalidDataException("Payload member is missing or measured size disagrees.");
            total = checked(total + member.Bytes);
        }
        foreach (string path in Directory.EnumerateFileSystemEntries(payload))
            if (Directory.Exists(path) || !names.Contains(Path.GetFileName(path))) throw new InvalidDataException("Unlisted payload entry.");
        var rootIds = EditorRecipeReferences.Roots(request.Editor!.Recipe).ToHashSet();
        long originalBytes = checked(request.Editor.Sources.Where(s => rootIds.Contains(s.Id)).Sum(s => s.Length));
        long? expectedTarget = request.Editor.Export.Compression switch
        {
            ByteGoal bytes => bytes.MaximumBytes,
            ReductionGoal reduction => (long)decimal.Floor(originalBytes * (1 - reduction.Percent / 100)),
            _ => null
        };
        var outcome = staged.Compression;
        if (outcome is null || outcome.ActualBytes != total || outcome.OriginalBytes != originalBytes || outcome.TargetBytes != expectedTarget || outcome.Attempts <= 0 || !outcome.MetTarget || (expectedTarget is { } limit && total > limit))
            throw new InvalidDataException("Compression outcome disagrees with measured bytes or requested target.");
    }
}
