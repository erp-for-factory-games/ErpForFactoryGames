using System.Text;

namespace Erp.Deploy;

// Renders the docker-compose env-file (stack.env) as raw UTF-8 bytes.
//
// Why a dedicated builder: the previous PowerShell deploy mangled bodies
// that contained `$`, quotes, or newlines because the remote shell re-parsed
// the string when ssh piped it in. The new SFTP path writes bytes byte-for-
// byte over the wire, so the only place mangling could still happen is here.
// Isolating the byte production makes it trivially testable in isolation.
public static class StackEnvBuilder
{
    public static byte[] Build(string connectorToken, string imageTag)
    {
        var sb = new StringBuilder();
        sb.Append("TUNNEL_TOKEN=").Append(connectorToken).Append('\n');
        sb.Append("ERP_IMAGE_TAG=").Append(imageTag).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
