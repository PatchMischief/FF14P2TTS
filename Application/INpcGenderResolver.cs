namespace FF14P2TTS.Application;

public interface INpcGenderResolver
{
    NpcGender GetGender(string speakerName);
}
