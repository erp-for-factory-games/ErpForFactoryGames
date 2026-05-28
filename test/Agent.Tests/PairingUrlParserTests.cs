using Erp.Presentation.Agent.Common;
using Satisfactory.Presentation.Agent;

namespace Agent.Tests;

public class PairingUrlParserTests
{
    [Fact]
    public void Parses_well_formed_url()
    {
        var result = PairingUrlParser.TryParse(
            "erp-agent://pair?token=eafg_abc123&api=https%3A%2F%2Fsatisfactory.erp-for-factory.games");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("eafg_abc123", result.Payload.Token);
        Assert.Equal("https://satisfactory.erp-for-factory.games", result.Payload.ApiBaseUrl);
    }

    [Fact]
    public void Rejects_unknown_scheme()
    {
        var result = PairingUrlParser.TryParse("https://pair?token=x&api=https://y");
        Assert.False(result.IsSuccess);
        Assert.Contains("scheme", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_unknown_host()
    {
        var result = PairingUrlParser.TryParse("erp-agent://wrong?token=x&api=https://y");
        Assert.False(result.IsSuccess);
        Assert.Contains("host", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_missing_token()
    {
        var result = PairingUrlParser.TryParse("erp-agent://pair?api=https://y");
        Assert.False(result.IsSuccess);
        Assert.Contains("token", result.ErrorMessage);
    }

    [Fact]
    public void Rejects_missing_api()
    {
        var result = PairingUrlParser.TryParse("erp-agent://pair?token=x");
        Assert.False(result.IsSuccess);
        Assert.Contains("api", result.ErrorMessage);
    }

    [Fact]
    public void Rejects_non_http_api()
    {
        var result = PairingUrlParser.TryParse(
            "erp-agent://pair?token=eafg_abc&api=file%3A%2F%2F%2Fetc%2Fpasswd");
        Assert.False(result.IsSuccess);
        Assert.Contains("http", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Strips_path_and_query_from_api()
    {
        // The 'api' parameter should be treated as a base URL — any path or
        // query the web UI accidentally appended must not bleed into the
        // request URLs the agent constructs.
        var result = PairingUrlParser.TryParse(
            "erp-agent://pair?token=eafg_abc&api=https%3A%2F%2Fhost.example%2Fextra%3Fkey%3Dvalue");
        Assert.True(result.IsSuccess);
        Assert.Equal("https://host.example", result.Payload.ApiBaseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void Rejects_garbage(string? input)
    {
        var result = PairingUrlParser.TryParse(input);
        Assert.False(result.IsSuccess);
    }
}
