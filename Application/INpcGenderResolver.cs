using System.Threading.Tasks;

namespace FF14P2TTS.Application;

public interface INpcGenderResolver
{
    NpcGender GetGender(string speakerName);
    Task<NpcGender> GetGenderAsync(string speakerName);
    void RequestGenderLookup(string speakerName);
    void Forget(string speakerName);
}
