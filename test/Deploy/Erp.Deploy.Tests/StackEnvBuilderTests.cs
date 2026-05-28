using System.Text;
using Erp.Deploy;

namespace Erp.Deploy.Tests;

// Pins the bug that motivated moving deploy off PowerShell: hostile bytes in
// the connector token used to get re-parsed by a remote shell (heredoc / scp).
// The new SFTP path writes bytes verbatim, so the only place we could still
// mangle is StackEnvBuilder. Any regression there fails this test.
public class StackEnvBuilderTests
{
    [Fact]
    public void Plain_token_round_trips_with_expected_layout()
    {
        var bytes = StackEnvBuilder.Build("abc123", "v1.2.3");

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal("TUNNEL_TOKEN=abc123\nERP_IMAGE_TAG=v1.2.3\n", text);
    }

    [Theory]
    [InlineData("$dollar")]                         // would interpolate in a shell
    [InlineData("\"double-quoted\"")]               // breaks `cat > foo` heredocs
    [InlineData("'single-quoted'")]                 // breaks ssh "cmd" outer-quoting
    [InlineData("back`tick`")]                      // command-substitution trap
    [InlineData("path\\with\\backslashes")]
    [InlineData("a;b|c&d>e<f(g)h{i}j[k]")]          // grab-bag of shell metachars
    [InlineData("contains\nliteral\nnewlines")]     // would split into bogus env lines after re-parse
    [InlineData("trailing\\")]
    [InlineData("$(whoami)")]                       // would execute under shell expansion
    [InlineData("ünïcödé-token-Ω")]                 // multibyte UTF-8
    public void Hostile_token_lands_verbatim_in_bytes(string hostile)
    {
        var bytes = StackEnvBuilder.Build(hostile, "v1.0.0");
        var text = Encoding.UTF8.GetString(bytes);

        // The whole point: the token sits between `TUNNEL_TOKEN=` and `\n`
        // exactly as supplied. No quoting added, no metachars escaped, no
        // newlines collapsed. The compose env-file consumer is responsible
        // for whatever interpretation it wants — our job is byte-fidelity.
        Assert.Equal($"TUNNEL_TOKEN={hostile}\nERP_IMAGE_TAG=v1.0.0\n", text);
    }

    [Fact]
    public void Hostile_image_tag_also_round_trips()
    {
        // ImageTag is operator-controlled but treat it the same way — any
        // future caller passing a tag with metachars should still get bytes
        // through unmolested.
        var bytes = StackEnvBuilder.Build("token", "tag-with-$weird\"chars");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal("TUNNEL_TOKEN=token\nERP_IMAGE_TAG=tag-with-$weird\"chars\n", text);
    }

    [Fact]
    public void Output_is_utf8_no_bom()
    {
        var bytes = StackEnvBuilder.Build("token", "tag");

        // BOM would corrupt the first env-var key (`﻿TUNNEL_TOKEN`).
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'T', bytes[0]);
    }
}
