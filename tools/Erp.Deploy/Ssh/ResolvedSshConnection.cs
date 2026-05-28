namespace Erp.Deploy.Ssh;

public sealed record ResolvedSshConnection(
    string Host,
    int Port,
    string User,
    IReadOnlyList<string> IdentityFiles)
{
    public string Display
    {
        get
        {
            var keys = IdentityFiles.Count switch
            {
                0 => "(no keys)",
                1 => IdentityFiles[0],
                _ => $"{IdentityFiles[0]} (+{IdentityFiles.Count - 1} more)",
            };
            return $"{User}@{Host}:{Port} (key={keys})";
        }
    }
}
