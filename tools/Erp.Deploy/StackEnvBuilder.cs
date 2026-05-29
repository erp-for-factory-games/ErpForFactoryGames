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
    public static byte[] Build(string connectorToken, string imageTag, string jwtSigningKey = "")
    {
        var sb = new StringBuilder();
        sb.Append("TUNNEL_TOKEN=").Append(connectorToken).Append('\n');
        sb.Append("ERP_IMAGE_TAG=").Append(imageTag).Append('\n');
        // Shared agent-JWT signing key (ADR-0027 / 5c3). Compose maps this to
        // Auth__JwtSigningKey on every API container so the Auth API and the
        // game APIs share one HMAC key. Omitted when blank so the APIs keep
        // their dev fallback (which logs a warning) rather than an empty key.
        if (!string.IsNullOrEmpty(jwtSigningKey))
        {
            sb.Append("Auth__JwtSigningKey=").Append(jwtSigningKey).Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
