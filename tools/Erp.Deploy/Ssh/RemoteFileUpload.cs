namespace Erp.Deploy.Ssh;

// Describes a single file to push to the remote stack directory.
// Mode is a POSIX octal (e.g. 0o644). Body is the in-memory bytes; for files
// read off disk, the caller fills Body from File.ReadAllBytes.
public sealed record RemoteFileUpload(string RemotePath, byte[] Body, short Mode)
{
    public int Size => Body.Length;
}
