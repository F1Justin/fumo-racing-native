using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool CreateSourceDiagnosticOwnerBeforeBegin(string vehicle = null)
        {
            int requested=vehicle==null?selectedVehicleIndex:vehicleRoster.VehicleIndexOf(vehicle);
            bool previousVerification=localSelectionVerification;
            localSelectionVerification=true;
            try
            {
                if(!BeginLocalSelection() || localOwners.Players.Length!=1)return false;
                var owner=localOwners.Players[0];int id=checked((int)owner.user.id);
                if(!owner.user.valid || !localSelection.SubmitStarted(id))return false;
                if(requested<0)return false;
                for(int i=0;localSelection.ReadBox(0).Vehicle!=requested && i<vehicleRoster.VehicleCount;i++)
                    if(!localSelection.MoveStarted(id,1))return false;
                if(localSelection.ReadBox(0).Vehicle!=requested || !localSelection.SubmitStarted(id)
                    || !localSelection.SubmitStarted(id) || !localSelection.AllReady)return false;
                localSelection.Ended();localSelectionActive=false;localOwners.End();
                localOwners.MarkDiagnosticOwner(true);sourceInjectedDiagnosticOwner=true;
                NativeInputBindings.SetActualLocalOwners(localOwners.Players);
                var choice=localOwners.SavedChoice(owner);
                Debug.Log("NATIVE_SOURCE_OWNER_FIXTURE scope=NativeInjected production selection SubmitStarted Character/Vehicle/Skin; actual PlayerInput/InputUser; physicalHumanAccepted=false");
                return choice!=null && choice.Vehicle==requested;
            }
            finally { localSelectionVerification=previousVerification; }
        }
    }
}
