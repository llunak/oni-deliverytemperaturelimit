using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;

namespace DeliveryTemperatureLimit
{
    // Add to all buildings where this makes sense.
    public class Buildings_Patch
    {
        public static void Patch( Harmony harmony )
        {
            Type[] configTypes =
            {
               typeof(StorageLockerSmartConfig),
               typeof(StorageLockerConfig),
               typeof(ObjectDispenserConfig),
               typeof(OrbitalCargoModuleConfig),
               typeof(SolidConduitInboxConfig),
               typeof(BottleEmptierConfig),
               typeof(BottleEmptierGasConfig),
               typeof(CreatureFeederConfig),
               typeof(PlanterBoxConfig),
               typeof(FarmTileConfig),
               typeof(HydroponicFarmConfig),
               typeof(AirFilterConfig),
               typeof(WaterPurifierConfig),
               typeof(RockCrusherConfig),
               typeof(OuthouseConfig),
               typeof(SludgePressConfig),
               typeof(SuitFabricatorConfig),
               typeof(MetalRefineryConfig),
               typeof(GlassForgeConfig),
               typeof(SublimationStationConfig),
               typeof(LonelyMinionHouseConfig),
               typeof(ResearchCenterConfig),
               typeof(WoodGasGeneratorConfig),
               typeof(RustDeoxidizerConfig),
               typeof(AlgaeDistilleryConfig),
               typeof(MineralDeoxidizerConfig),
               typeof(KilnConfig),
#if false
               typeof(WaterCoolerConfig),
               typeof(JuicerConfig),
               typeof(AlgaeHabitatConfig),
               typeof(AdvancedResearchCenterConfig),
               typeof(MechanicalSurfboardConfig),
               typeof(IceMachineConfig),
               typeof(WashBasinConfig),
               typeof(FarmStationConfig),
               typeof(EspressoMachineConfig),
               typeof(SodaFountainConfig),
               typeof(CompostConfig),
               typeof(OxyliteRefineryConfig),
               typeof(HandSanitizerConfig),
               typeof(FertilizerMakerConfig),
               typeof(DiningTableConfig),

               typeof(MicrobeMusherConfig),
               typeof(ClothingFabricatorConfig),
               typeof(ManualHighEnergyParticleSpawnerConfig),
               typeof(OrbitalResearchCenterConfig),
               typeof(CraftingTableConfig),
               typeof(DiamondPressConfig),
               typeof(ApothecaryConfig),
               typeof(EggCrackerConfig),
               typeof(FossilDigSiteConfig),
               typeof(ClothingAlterationStationConfig),
               typeof(AdvancedApothecaryConfig),
               typeof(GenericFabricatorConfig),
               typeof(GourmetCookingStationConfig),
               typeof(CookingStationConfig),
               typeof(SupermaterialRefineryConfig),
               typeof(MissileFabricatorConfig),
               typeof(UraniumCentrifugeConfig),
#endif
            };
            foreach( Type configType in configTypes )
            {
                MethodInfo info = AccessTools.Method( configType, "DoPostConfigureComplete");
                // HACK: Using prefix, postfix or finalizer randomly(?) makes the game crash,
                // probably a Harmony bug (even enabling 'Harmony.DEBUG = true;' avoids
                // the problem ). Use whatever seems to work.
                if( info != null )
                    harmony.Patch( info, postfix: new HarmonyMethod( typeof( Buildings_Patch ).GetMethod( "DoPostConfigureComplete" )));
                else
                    Debug.LogError( "DeliveryTemperatureLimit: Failed to patch DoPostConfigureComplete() for " + configType.Name );
            }

            string[] configTypeStrings =
            {
                // Move This Here
                "MoveThisHere.HaulingPointConfig, MoveThisHere",
                // Storage Pod
                "StoragePod.StoragePodConfig, Storage Pod",
            };
            foreach( string configType in configTypeStrings )
            {
                MethodInfo info = AccessTools.Method( Type.GetType( configType ), "DoPostConfigureComplete");
                if( info != null )
                    harmony.Patch( info, postfix: new HarmonyMethod( typeof( Buildings_Patch ).GetMethod( "DoPostConfigureComplete" )));
            }
        }

        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimit>();
        }
    }
}
