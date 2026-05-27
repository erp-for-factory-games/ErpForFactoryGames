namespace Erp.Deploy.Cloudflare;

public sealed class CloudflareApiException : Exception
{
    public string Method { get; }
    public string Path { get; }
    public int? HttpStatus { get; }
    public IReadOnlyList<(int Code, string Message)> ApiErrors { get; }

    public CloudflareApiException(
        string method,
        string path,
        int? httpStatus,
        IReadOnlyList<(int, string)> apiErrors,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Method = method;
        Path = path;
        HttpStatus = httpStatus;
        ApiErrors = apiErrors;
    }
}
