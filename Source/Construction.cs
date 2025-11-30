using HarmonyLib;
using UnityEngine;
using PeterHan.PLib.UI;
using System;
using System.Reflection;

namespace DeliveryTemperatureLimit
{
    [HarmonyPatch(typeof(MaterialSelectionPanel))]
    public class MaterialSelectionPanel_Patch
    {
        private static FieldInfo materialSelectionPanelField
            = AccessTools.Field( typeof( DetailsScreenMaterialPanel ), "materialSelectionPanel" );

        // There are several MaterialSelectionPanel instances (build menu,
        // building rocket modules, the change material tab), share just one
        // singleton for all of them (and the change material tab case will be
        // ignored).
        public static TemperatureLimit limit = null;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(OnPrefabInit))]
        public static void OnPrefabInit(MaterialSelectionPanel __instance)
        {
            if( !Options.Instance.UnderConstructionLimit )
                return;
            // Ignore the change material case. It results in a deconstruct+construct combo,
            // and it'd be necessary to carry-over the temperatures, which the game can't do even
            // for settings of the building. Reconsider when that is implemented.
            DetailsScreenMaterialPanel detailsScreenMaterialPanel
                = DetailsScreen.Instance.GetTabOfType(DetailsScreen.SidescreenTabTypes.Material)
                    .bodyInstance.GetComponentInChildren<DetailsScreenMaterialPanel>();
            if( __instance == (MaterialSelectionPanel) materialSelectionPanelField.GetValue( detailsScreenMaterialPanel ))
                return;
            // Create and set the build singleton instance, it shouldn't matter in which game object it is.
            if( limit == null )
                limit = __instance.gameObject.AddOrGet< TemperatureLimit >();
            CheckResetToConstructionDefaults( limit );
            TemperatureLimitWidget widget = __instance.gameObject.AddOrGet<TemperatureLimitWidget>();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ConfigureScreen))]
        public static void ConfigureScreen(MaterialSelectionPanel __instance)
        {
            if( !Options.Instance.UnderConstructionLimit )
                return;
            TemperatureLimitWidget widget = __instance.GetComponent<TemperatureLimitWidget>();
            if( widget == null )
                return;
            widget.SetTarget( limit );
        }

        public static void CheckResetToConstructionDefaults( TemperatureLimit checkLimit )
        {
            if( checkLimit != limit || limit == null )
                return;
            limit.SetLowLimit( (int) Math.Round( GameUtil.GetTemperatureConvertedToKelvin(
                Options.Instance.MinConstructionTemperature, GameUtil.temperatureUnit )));
            limit.SetHighLimit( (int) Math.Round( GameUtil.GetTemperatureConvertedToKelvin(
                Options.Instance.MaxConstructionTemperature, GameUtil.temperatureUnit )));
        }
    }

    [HarmonyPatch(typeof(BuildingDef))]
    public class BuildingDef_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Instantiate))]
        public static void Instantiate(BuildingDef __instance, GameObject __result)
        {
            if( !Options.Instance.UnderConstructionLimit )
                return;
            if( __result == null ) // MoveThisHere mod patches the function to bail out
                return;
            TemperatureLimit limit = MaterialSelectionPanel_Patch.limit;
            if( limit == null )
                return;
            __result.AddOrGet<TemperatureLimit>().CopySettings( limit );
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(PostProcess))]
        public static void PostProcess(BuildingDef __instance)
        {
            if( !Options.Instance.UnderConstructionLimit )
                return;
            if( __instance.BuildingUnderConstruction == null )
                return;
            // needs to be added for all to make loading from saves work
            __instance.BuildingUnderConstruction.gameObject.AddOrGet<TemperatureLimit>();
        }
    }
}
