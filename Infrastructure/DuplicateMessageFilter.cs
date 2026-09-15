using System;

namespace FF14P2TTS.Infrastructure;

/// <summary>Suppresses identical messages repeated within a short window.</summary>
public sealed class DuplicateMessageFilter
{
    private const double WindowSeconds = 2;
    private string _lastMessage = string.Empty;
    private DateTime _lastMessageTime = DateTime.MinValue;

    public bool ShouldSkip(string text)
    {
        if (text == _lastMessage && (DateTime.UtcNow - _lastMessageTime).TotalSeconds < WindowSeconds)
            return true;

        _lastMessage = text;
        _lastMessageTime = DateTime.UtcNow;
        return false;
    }
}
