using Erp.Presentation.Web.Auth;
using Microsoft.Extensions.Options;

namespace Erp.Presentation.Web.Auth.Tests;

/// <summary>
/// ADR-0028 §7 — the hardcoded sign-in backend. Security-relevant credential
/// check, so it gets direct coverage (right creds in, wrong creds out, no
/// blank-password bypass).
/// </summary>
public sealed class HardcodedCredentialAuthBackendTests
{
    private static HardcodedCredentialAuthBackend Backend(string user, string pass, string display = "Local User") =>
        new(Options.Create(new AuthLandingOptions
        {
            Hardcoded = new AuthLandingOptions.HardcodedCredentials
            {
                Username = user,
                Password = pass,
                DisplayName = display,
            },
        }));

    [Fact]
    public async Task Correct_credentials_succeed_with_subject_and_display_name()
    {
        var result = await Backend("admin", "s3cret", "Chris").ValidateAsync("admin", "s3cret");

        Assert.True(result.Succeeded);
        Assert.Equal("local:admin", result.Subject);
        Assert.Equal("Chris", result.DisplayName);
    }

    [Theory]
    [InlineData("admin", "wrong")]      // bad password
    [InlineData("nope", "s3cret")]      // bad username
    [InlineData("admin", "")]           // empty supplied password
    [InlineData("", "")]                // nothing supplied
    public async Task Wrong_credentials_fail(string user, string pass)
    {
        var result = await Backend("admin", "s3cret").ValidateAsync(user, pass);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Blank_configured_password_never_authenticates()
    {
        // A misconfiguration (no password set) must not become an open door,
        // even if the caller also sends a blank password.
        var result = await Backend("admin", "").ValidateAsync("admin", "");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Username_match_is_case_sensitive()
    {
        var result = await Backend("admin", "s3cret").ValidateAsync("Admin", "s3cret");
        Assert.False(result.Succeeded);
    }
}
