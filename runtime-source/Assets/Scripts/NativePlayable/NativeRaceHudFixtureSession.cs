using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool PrepareHudFixtureQuickRace(string localVehicle)
        {
            selectedMode=NativeSessionMode.QuickRace;
            if(!CreateSourceDiagnosticOwnerBeforeBegin(localVehicle))return false;
            var owner=NativeLocalSelectionOwners.Instance.Players[0];
            var choice=NativeLocalSelectionOwners.Instance.SavedChoice(owner);
            selectedCharacterIndex=choice.Character;selectedVehicleIndex=choice.Vehicle;selectedSkinIndex=choice.Skin;
            var config=new NativeSessionConfig{mode=NativeSessionMode.QuickRace,modeId="quick-race",laps=5,racerCount=racers.Length,itemType="normal",trackIds=new List<string>{trackId}};
            for(int i=0;i<racers.Length;i++)
            {
                int character=i==0?selectedCharacterIndex:vehicleRoster.CharacterIndexOf(racerNames[i].Split('/')[0].Trim());if(character<0)character=vehicleRoster.DefaultCharacterIndex;
                config.roster.Add(new NativeSessionSeat{seatId=i==0?"human-"+owner.user.id:"seat-"+i,characterId=vehicleRoster.CharacterIdAt(character),vehicleId=i==0?localVehicle:"kart",skinIndex=i==0?choice.Skin:0,isHuman=i==0});
            }
            sessionId=NativeGameSession.Create(config);PrepareModeRoster(NativeGameSession.Current.config);
            finishArrivalQueue.Clear();finishArrivalCount=0;for(int i=0;i<finishPlaces.Length;i++)finishPlaces[i]=0;
            if(!NativeGameSession.Begin(sessionId,trackId))return false;
            ConfigureSourceHumans();
            StartSourceCircuit();
            return true;
        }
    }
}
