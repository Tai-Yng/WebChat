// Copyright (c) Tai-Yng. MIT license.

using WebChat.Protocol;

namespace WebChat.Tests;

public class ProtocolTests
{
    [Fact]
    public void ChatProtocol_RoundTripsHistory()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatMessage.User, "你好", 1000),
            new(ChatMessage.Assistant, "你好！有什么可以帮你？", 2000),
        };
        var line = ChatProtocol.History(messages);

        var env = ChatProtocol.Parse(line);

        Assert.Equal("history", env.Op);
        Assert.Equal(2, env.Messages.Count);
        Assert.Equal("你好", env.Messages[0].Text);
        Assert.Equal(ChatMessage.Assistant, env.Messages[1].Role);
    }

    [Fact]
    public void ChatProtocol_DeltaCarriesDoneFlag()
    {
        var partial = ChatProtocol.Parse(ChatProtocol.Delta("部分回复", false));
        var final = ChatProtocol.Parse(ChatProtocol.Delta("完整回复", true));

        Assert.False(partial.Done);
        Assert.True(final.Done);
        Assert.Equal("完整回复", final.Text);
    }

    [Fact]
    public void ChatLog_PersistsAndLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "webchat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "chat-history.json");

        var log = new ChatLog(file);
        log.Add(ChatMessage.User, "first");
        log.Add(ChatMessage.Assistant, "second");

        var fresh = new ChatLog(file);
        fresh.Load();

        Assert.Equal(2, fresh.Messages.Count);
        Assert.Equal("second", fresh.Messages[^1].Text);
    }
}
