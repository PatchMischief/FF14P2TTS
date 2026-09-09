namespace FF14P2TTS.Application;

public interface ITtsServiceProvider
{
    ITtsService Active { get; }
}
