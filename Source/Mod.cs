using HarmonyLib;
using System.Collections.Generic;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace DeliveryTemperatureLimit
{
    public class Mod : KMod.UserMod2
    {
        public override void OnLoad( Harmony harmony )
        {
            base.OnLoad( harmony );
            PUtil.InitLibrary( false );
            new POptions().RegisterOptions( this, typeof( Options ));
            ClearableManager_Patch.Patch( harmony );
            FetchManager_PickupComparerIncludingPriority_Patch.Patch( harmony );
            FetchAreaChore_StatesInstance_Begin_Delegate_Patch.Patch( harmony );
        }
        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<KMod.Mod> mods)
        {
            base.OnAllModsLoaded( harmony, mods );
            Buildings_Patch.Patch( harmony );
            ChoreComparator_Patch.Patch( harmony );
            FetchManagerFastUpdate_PickupTagDict_Patch.Patch( harmony );
            StatusItemsUpdaterPatch.Patch( harmony );
        }
    }
}
