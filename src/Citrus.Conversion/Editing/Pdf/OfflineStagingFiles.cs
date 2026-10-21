namespace Citrus.Conversion.Editing.Pdf;

/// <summary>Checks the worker boundary after its process tree has retired, before full-trust parsers open any output.</summary>
internal static class OfflineStagingFiles
{
    internal static string[] ValidatePayload(string directory,string format)
    {
        string root=Path.GetFullPath(directory);SafeEditorFiles.RejectLinks(root);
        if(!Directory.Exists(root))throw new InvalidDataException("The offline worker output directory is missing.");
        var files=new List<string>();long total=0;
        foreach(string entry in Directory.EnumerateFileSystemEntries(root))
        {
            SafeEditorFiles.RejectLinks(entry);
            string full=Path.GetFullPath(entry);
            if(!string.Equals(Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(full)||!Path.GetExtension(full).Equals("."+format,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The offline worker returned an unlisted directory or incompatible output.");
            long bytes=new FileInfo(full).Length;total=checked(total+bytes);
            if(bytes==0||total>2L*1024*1024*1024||files.Count>=100_000)throw new InvalidDataException("The offline conversion exceeds its output limits or returned an empty file.");
            files.Add(full);
        }
        if(files.Count==0)throw new InvalidDataException("The offline worker did not create an output.");
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    internal static void DeletePrivateTree(string directory)
    {
        string root=Path.GetFullPath(directory);SafeEditorFiles.RejectLinks(Path.GetDirectoryName(root)!);
        Delete(root);
        static void Delete(string path)
        {
            if(!Path.Exists(path))return;
            var attributes=File.GetAttributes(path);
            // Never traverse a link supplied by an untrusted worker, even during cleanup.
            if((attributes&FileAttributes.ReparsePoint)!=0)
            {if((attributes&FileAttributes.Directory)!=0)Directory.Delete(path);else File.Delete(path);return;}
            if((attributes&FileAttributes.Directory)!=0)
            {
                foreach(string entry in Directory.EnumerateFileSystemEntries(path))Delete(entry);
                File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);Directory.Delete(path);
            }
            else{File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);File.Delete(path);}
        }
    }
}
