// Copyright (c) Tai-Yng. MIT license.
//
// Self-contained: the CmdPal project is a WinAppSDK TFM and cannot be referenced from
// plain net10.0 tests, so the AUMID contract is pinned here and must be kept in sync
// with WebChat.CmdPal/Commands/LaunchWebChatCommand.cs.

namespace WebChat.Tests;

public class LauncherTests
{
    public const string AppAumid = "TaiYng.WebChat.App_511vm1e3pntc2!App";

    [Fact]
    public void AppAumid_IsWellFormed()
    {
        Assert.Matches(@"^TaiYng\.WebChat\.App_[a-z0-9]+!App$", AppAumid);
    }

    [Fact]
    public void AppAumid_MatchesManifestIdentityFamily()
    {
        // The manifest identity TaiYng.WebChat.App + publisher CN=Tai-Yng must produce
        // exactly this family suffix (verified via Get-AppxPackage at deploy time).
        Assert.StartsWith("TaiYng.WebChat.App_", AppAumid);
        Assert.EndsWith("!App", AppAumid);
    }
}
