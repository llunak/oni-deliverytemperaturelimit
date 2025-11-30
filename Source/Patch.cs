using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using PeterHan.PLib.Options;

namespace DeliveryTemperatureLimit
{
    [HarmonyPatch(typeof(FetchManager))]
    public class FetchManager_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(IsFetchablePickup))]
        public static void IsFetchablePickup(ref bool __result, Pickupable pickup, FetchChore chore, Storage destination)
        {
            if( !__result )
                return;
            TemperatureLimit limit = TemperatureLimit.Get( destination.gameObject );
            if( limit == null || limit.IsDisabled() || pickup.PrimaryElement == null )
                return;
            __result = limit.AllowedByTemperature( pickup.PrimaryElement.Temperature );
        }
    }

    // Clearable means objects explicitly marked for sweeping. The code apparently does not
    // use IsFetchablePickup() and somehow only compares fetches, so patch it to check too.
    // Class is internal, needs to be patched manually.
    public class ClearableManager_Patch
    {
        public static void Patch( Harmony harmony )
        {
            MethodInfo info = AccessTools.Method( typeof( KMod.Mod ).Assembly.GetType( "ClearableManager" ), "CollectChores" );
            if( info != null )
                harmony.Patch( info, transpiler: new HarmonyMethod(
                    typeof( ClearableManager_Patch ).GetMethod( nameof( CollectChores ))));
            else
                Debug.LogError( "DeliveryTemperatureLimit: Failed to find"
                    + " ClearableManager.CollectChores() for patching" );
        }

        public static IEnumerable<CodeInstruction> CollectChores(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            int pickupableLoad = -1;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                if( codes[ i ].opcode == OpCodes.Ldloc_S
                    && codes[ i ].operand.ToString().StartsWith( "Pickupable (" ))
                {
                    pickupableLoad = i;
                }
                // The function has code:
                // if (... && kPrefabID.HasTag(fetch.chore.tagsFirst)))
                // Add:
                // if (... && CollectChores_Hook( fetch.chore, pickupable ))
                // Note that the original code is '(c1 && c2) || (c3 && c4))', so the evaluation
                // of the condition is a bit more complex.
                if( pickupableLoad != -1
                    && codes[ i ].opcode == OpCodes.Ldloc_S && codes[ i ].operand.ToString().StartsWith( "KPrefabID (" )
                    && i + 9 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldloc_S
                    && codes[ i + 1 ].operand.ToString().StartsWith( "GlobalChoreProvider+Fetch (" )
                    && codes[ i + 2 ].opcode == OpCodes.Ldfld && codes[ i + 2 ].operand.ToString() == "FetchChore chore"
                    && codes[ i + 3 ].opcode == OpCodes.Ldfld && codes[ i + 3 ].operand.ToString() == "Tag tagsFirst"
                    && codes[ i + 4 ].opcode == OpCodes.Callvirt && codes[ i + 4 ].operand.ToString() == "Boolean HasTag(Tag)"
                    && codes[ i + 5 ].opcode == OpCodes.Br_S
                    && codes[ i + 6 ].opcode == OpCodes.Ldc_I4_0
                    && codes[ i + 7 ].opcode == OpCodes.Br_S
                    && codes[ i + 8 ].opcode == OpCodes.Ldc_I4_1
                    && codes[ i + 9 ].opcode == OpCodes.Brfalse_S )
                {
                    codes.Insert( i + 10, codes[ i + 1 ].Clone());
                    codes.Insert( i + 11, codes[ i + 2 ].Clone()); // load 'fetch.chore'
                    codes.Insert( i + 12, codes[ pickupableLoad ].Clone()); // load 'pickupable'
                    codes.Insert( i + 13, new CodeInstruction( OpCodes.Call,
                        typeof( ClearableManager_Patch ).GetMethod( nameof( CollectChores_Hook ))));
                    codes.Insert( i + 14, codes[ i + 9 ].Clone()); // if false
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch ClearableManager.CollectChores()");
            return codes;
        }

        public static bool CollectChores_Hook( FetchChore chore, Pickupable pickupable )
        {
            TemperatureLimit limit = TemperatureLimit.Get( chore.destination?.gameObject );
            if( limit == null || limit.IsDisabled())
                return true;
            if( pickupable?.PrimaryElement != null )
                return limit.AllowedByTemperature( pickupable.PrimaryElement.Temperature );
            return true;
        }
    }

    // If something to fetch is found, this class tries to find similar objects and add them
    // to the fetch, and it doesn't use IsFetchablePickup(), it only compares the two fetches,
    // so patch the code to check as well.
    [HarmonyPatch(typeof(FetchAreaChore.StatesInstance))]
    public static class FetchAreaChore_StatesInstance_Patch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(Begin))]
        public static IEnumerable<CodeInstruction> Begin(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // if (... && fetchChore3.forbidHash == rootChore.forbidHash)
                // Add:
                // if (... && Begin_Hook( rootChore, fetchChore3 ))
                if( codes[ i ].opcode == OpCodes.Brfalse_S && i + 6 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldloc_S && codes[ i + 1 ].operand.ToString().StartsWith( "FetchChore" )
                    && codes[ i + 2 ].opcode == OpCodes.Ldfld && codes[ i + 2 ].operand.ToString() == "System.Int32 forbidHash"
                    && codes[ i + 3 ].opcode == OpCodes.Ldarg_0
                    && codes[ i + 4 ].opcode == OpCodes.Ldfld && codes[ i + 4 ].operand.ToString() == "FetchChore rootChore"
                    && codes[ i + 5 ].opcode == OpCodes.Ldfld && codes[ i + 5 ].operand.ToString() == "System.Int32 forbidHash"
                    && codes[ i + 6 ].opcode == OpCodes.Bne_Un_S )
                {
                    codes.Insert( i + 7, codes[ i + 3 ].Clone());
                    codes.Insert( i + 8, codes[ i + 4 ].Clone()); // load 'rootChore'
                    codes.Insert( i + 9, codes[ i + 1 ].Clone()); // load 'fetchChore3'
                    codes.Insert( i + 10, new CodeInstruction( OpCodes.Call,
                        typeof( FetchAreaChore_StatesInstance_Patch ).GetMethod( nameof( Begin_Hook ))));
                    codes.Insert( i + 11, codes[ i ].Clone()); // if false
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchAreaChore.StatesInstance.Begin()");
            return codes;
        }

        public static bool Begin_Hook( FetchChore rootChore, FetchChore fetchChore2 )
        {
            // This checks whether the second chore can be handled as a part of the root chore.
            // Therefore add a check if the second chore's range is compatible.
            TemperatureLimit limit = TemperatureLimit.Get( rootChore?.destination?.gameObject );
            TemperatureLimit limit2 = TemperatureLimit.Get( fetchChore2?.destination?.gameObject );
            if( limit == limit2 || limit2 == null || limit2.IsDisabled())
                return true;
            if( limit == null )
                return false; // by now limit2 is a valid range
            return limit2.LowLimit >= limit.LowLimit && limit2.HighLimit <= limit.HighLimit;
        }
    }

    // Delegate called from FetchAreaChore.StatesInstance.Begin().
    public static class FetchAreaChore_StatesInstance_Begin_Delegate_Patch
    {
        public static void Patch( Harmony harmony )
        {
            MethodInfo info = AccessTools.Method( typeof( KMod.Mod ).Assembly.GetType(
                "FetchAreaChore/StatesInstance/<>c__DisplayClass17_0" ), "<Begin>b__0" );
            if( info != null )
                harmony.Patch( info, transpiler: new HarmonyMethod(
                    typeof( FetchAreaChore_StatesInstance_Begin_Delegate_Patch ).GetMethod( nameof( Delegate ))));
            else
                Debug.LogError( "DeliveryTemperatureLimit: Failed to find"
                    + " FetchAreaChore.StatesInstance.Begin() delegate for patching" );
        }

        public static IEnumerable<CodeInstruction> Delegate(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            int rootChoreLoad = -1;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                if( codes[ i ].opcode == OpCodes.Ldarg_0 && i + 2 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i + 1 ].operand.ToString().EndsWith("this")
                    && codes[ i + 2 ].opcode == OpCodes.Ldfld && codes[ i + 2 ].operand.ToString() == "FetchChore rootChore" )
                {
                    rootChoreLoad = i;
                }
                // The function has code:
                // if (!rootContext.consumerState.consumer.CanReach(pickupable))
                // Add:
                // if (... || !Delegate_Hook( rootChore, pickupable ))
                if( rootChoreLoad != -1 && codes[ i ].opcode == OpCodes.Ldloc_S && i + 2 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Callvirt && codes[ i + 1 ].operand.ToString() == "Boolean CanReach(IApproachable)"
                    && codes[ i + 2 ].opcode == OpCodes.Brfalse_S )
                {
                    codes.Insert( i + 3, codes[ rootChoreLoad ].Clone());
                    codes.Insert( i + 4, codes[ rootChoreLoad + 1 ].Clone());
                    codes.Insert( i + 5, codes[ rootChoreLoad + 2 ].Clone()); // load 'rootChore'
                    codes.Insert( i + 6, codes[ i ].Clone()); // load 'pickupable'
                    codes.Insert( i + 7, new CodeInstruction( OpCodes.Call,
                        typeof( FetchAreaChore_StatesInstance_Patch ).GetMethod( nameof( Delegate_Hook ))));
                    codes.Insert( i + 8, codes[ i + 2 ].Clone()); // if false
                    found = true;
                    break;
                }
            }
            if(found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchAreaChore.StatesInstance.Begin delegate()");
            return codes;
        }

        public static bool Delegate_Hook( FetchChore rootChore, Pickupable pickupable2 )
        {
            TemperatureLimit limit = TemperatureLimit.Get( rootChore.destination?.gameObject );
            if( limit == null || limit.IsDisabled() || pickupable2.PrimaryElement == null )
                return true;
            return limit.AllowedByTemperature( pickupable2.PrimaryElement.Temperature );
        }
    }

    [HarmonyPatch(typeof(GlobalChoreProvider))]
    public class GlobalChoreProvider_Patch
    {
        // GlobalChoreProvider is a singleton, but one structure per world is needed.
        private static Dictionary< int, HashSet< Tag >[] > storageFetchableTagsPerTemperatureIndex
            = new Dictionary< int, HashSet< Tag >[] >();
        private static TemperatureLimit.TemperatureIndexData temperatureIndexData;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ClearableHasDestination))]
        public static void ClearableHasDestination(ref bool __result, Pickupable pickupable)
        {
            if( !__result ) // Has no destination already without temperature check.
                return;
            if( pickupable.PrimaryElement != null )
            {
                // GetMyParentWorldId() returns the world itself if there is no parent,
                // so it is the same for a world and its subworlds.
                int worldId = pickupable.GetMyWorldId();
                HashSet< Tag >[] worldTagsPerIndex;
                if( storageFetchableTagsPerTemperatureIndex.TryGetValue( worldId, out worldTagsPerIndex ))
                {
                    int temperatureIndex = TemperatureLimit.getTemperatureIndexData()
                        .TemperatureIndex( pickupable.PrimaryElement.Temperature );
                    if( temperatureIndex < worldTagsPerIndex.Length
                        && worldTagsPerIndex[ temperatureIndex ].Contains( pickupable.KPrefabID.PrefabTag ))
                    {
                        return; // ok, there'a storage that allows that tag with that temperature
                    }
                }
            }
            __result = false; // No storage that'd allow the temperature (or possibly temperature data not up to date).
        }

        // This function updates a hash of allowed tags for ClearableHasDestination.
        // Patch it to build our information that includes temperature limits.
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(UpdateStorageFetchableBits))]
        public static IEnumerable<CodeInstruction> UpdateStorageFetchableBits(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found1 = false;
            bool found2 = false;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // if (!fetchMap.TryGetValue(worldIDsSorted[i], out var value))
                // Change to:
                // if (!fetchMap.TryGetValue(UpdateStorageFetchableBits_Hook1(worldIDsSorted[i]), out var value))
                if( codes[ i ].opcode == OpCodes.Callvirt && codes[ i ].operand.ToString() == "Int32 get_Item(Int32)"
                    && i + 3 < codes.Count
                    && codes[ i + 2 ].opcode == OpCodes.Callvirt
                    && codes[ i + 2 ].operand.ToString() == "Boolean TryGetValue(Int32, System.Collections.Generic.List`1[FetchChore] ByRef)" )
                {
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Dup ));
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Call,
                        typeof( GlobalChoreProvider_Patch ).GetMethod( nameof( UpdateStorageFetchableBits_Hook1 ))));
                    found1 = true;
                }

                // The function has code:
                // storageFetchableTags.UnionWith(fetchChore.tags);
                // Append:
                // UpdateStorageFetchableBits_Hook2(fetchChore);
                if( codes[ i ].opcode == OpCodes.Ldarg_0
                    && i + 4 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld
                    && codes[ i + 1 ].operand.ToString().EndsWith( "storageFetchableTags" )
                    && codes[ i + 2 ].opcode == OpCodes.Ldloc_S
                    && codes[ i + 2 ].operand.ToString().StartsWith( "FetchChore (" )
                    && codes[ i + 3 ].opcode == OpCodes.Ldfld
                    && codes[ i + 3 ].operand.ToString().EndsWith( "tags" )
                    && codes[ i + 4 ].opcode == OpCodes.Callvirt
                    && codes[ i + 4 ].operand.ToString().StartsWith( "Void UnionWith(" ))
                {
                    codes.Insert( i + 5, codes[ i + 2 ].Clone()); // load 'fetchChore'
                    codes.Insert( i + 6, new CodeInstruction( OpCodes.Call,
                        typeof( GlobalChoreProvider_Patch ).GetMethod( nameof( UpdateStorageFetchableBits_Hook2 ))));
                    found2 = true;
                    break;
                }
            }
            if(!found1 || !found2)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch GlobalChoreProvider.UpdateStorageFetchableBits()");
            return codes;
        }

        public static void UpdateStorageFetchableBits_Hook1( int worldId )
        {
            temperatureIndexData = TemperatureLimit.getTemperatureIndexData();
            HashSet< Tag >[] worldTagsPerIndex;
            if( !storageFetchableTagsPerTemperatureIndex.TryGetValue( worldId, out worldTagsPerIndex )
                || worldTagsPerIndex.Length != temperatureIndexData.TemperatureIndexCount())
            {
                worldTagsPerIndex = new HashSet< Tag >[ temperatureIndexData.TemperatureIndexCount() ];
                for( int i = 0; i < temperatureIndexData.TemperatureIndexCount(); ++i )
                    worldTagsPerIndex[ i ] = new HashSet< Tag >();
                storageFetchableTagsPerTemperatureIndex[ worldId ] = worldTagsPerIndex;
            }
            else
            {
                for( int i = 0; i < temperatureIndexData.TemperatureIndexCount(); ++i )
                    worldTagsPerIndex[ i ].Clear();
            }
        }

        public static void UpdateStorageFetchableBits_Hook2(FetchChore chore)
        {
            TemperatureLimit limit = TemperatureLimit.Get( chore.destination.gameObject );
            int lowIndex = 0;
            int highIndex = temperatureIndexData.TemperatureIndexCount();
            // The game code groups this by fetchMap indexes, which are parentWorldId. Since parentWorldId points to the world itself
            // if there is no parent, this is grouping it by world and its subworlds.
            int parentWorldId = chore.gameObject.GetMyParentWorldId();
            HashSet< Tag >[] worldTagsPerIndex = storageFetchableTagsPerTemperatureIndex[ parentWorldId ];
            if( limit != null && !limit.IsDisabled())
                ( lowIndex, highIndex ) = temperatureIndexData.TemperatureIndexes( limit );
            for( int i = lowIndex; i < highIndex; ++i )
                worldTagsPerIndex[ i ].UnionWith( chore.tags );
        }
    }

    // FetchManager keeps a list of available Pickupable's, and (for presumably performance reasons)
    // it sorts them by tag+priority+cost, and then keeps only the cheapest one for each tag+priority.
    // This needs to be changed to keep one for each tag+priority+temperatureindex, otherwise
    // the game wouldn't find a further pickupable with suitable temperature if there would be
    // a closer one with an unsuitable temperature.
    [HarmonyPatch(typeof(FetchManager.FetchablesByPrefabId))]
    public class FetchManager_FetchablesByPrefabId_Patch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(UpdatePickups))]
        public static IEnumerable<CodeInstruction> UpdatePickups(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            int pickupLoad = -1;
            int pickup2Load = -1;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                if( i > 0 && codes[ i ].opcode == OpCodes.Ldfld && codes[ i ].operand.ToString() == "System.Int32 tagBitsHash" )
                {
                    if( pickupLoad < 0 )
                        pickupLoad = i - 1;
                    else
                        pickup2Load = i - 1;
                }
                // The function has code:
                // if (pickup.masterPriority == pickup2.masterPriority && tagBitsHash == num)
                // Change to:
                // if (.. && tagBitsHash == num
                //     && UpdatePickups_Hook( pickup, pickup2 ) == 1 )
                if( codes[ i ].opcode == OpCodes.Ldfld && codes[ i ].operand.ToString() == "System.Int32 masterPriority"
                    && i + 4 < codes.Count && pickupLoad != -1 && pickup2Load != -1
                    && codes[ i + 1 ].opcode == OpCodes.Bne_Un_S
                    && codes[ i + 2 ].opcode == OpCodes.Ldloc_S
                    && CodeInstructionExtensions.IsLdloc( codes[ i + 3 ] )
                    && codes[ i + 4 ].opcode == OpCodes.Bne_Un_S )
                {
                    codes.Insert( i + 5, codes[ pickupLoad ].Clone()); // load 'pickup'
                    codes.Insert( i + 6, codes[ pickup2Load ].Clone()); // load 'pickup2'
                    codes.Insert( i + 7, new CodeInstruction( OpCodes.Call,
                        typeof( FetchManager_FetchablesByPrefabId_Patch ).GetMethod( nameof( UpdatePickups_Hook ))));
                    codes.Insert( i + 8, new CodeInstruction( OpCodes.Ldc_I4_1 )); // load '1' (so that the == test can be reused)
                    codes.Insert( i + 9, codes[ i + 4 ].Clone()); // if not equal
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchManager.FetchablesByPrefabId.UpdatePickups()");
            return codes;
        }

        public static int UpdatePickups_Hook( FetchManager.Pickup pickup, FetchManager.Pickup pickup2 )
        {
            TemperatureLimit.TemperatureIndexData data = TemperatureLimit.getTemperatureIndexData();
            if( pickup.pickupable.PrimaryElement == null || pickup2.pickupable.PrimaryElement == null )
                return 0;
            return data.TemperatureIndex( pickup.pickupable.PrimaryElement.Temperature )
                 == data.TemperatureIndex( pickup2.pickupable.PrimaryElement.Temperature ) ? 1 : 0;
        }
    }

    public class FetchManager_PickupComparerIncludingPriority_Patch
    {
        // The class is private, so patch manually.
        public static void Patch( Harmony harmony )
        {
            MethodInfo info = AccessTools.Method(
                typeof( FetchManager ).GetNestedType( "PickupComparerIncludingPriority", BindingFlags.NonPublic ),
                "Compare");
            if( info != null )
                harmony.Patch( info, transpiler: new HarmonyMethod(
                    typeof( FetchManager_PickupComparerIncludingPriority_Patch ).GetMethod( nameof( Compare ))));
            else
                Debug.LogError( "DeliveryTemperatureLimit: Failed to find"
                    + " FetchManager.PickupComparerIncludingPriority.Compare() for patching" );
        }

        public static IEnumerable<CodeInstruction> Compare(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // num = a.masterPriority.CompareTo(b.masterPriority);
                // if (num != 0)
                //    return num;
                // Append:
                // num = Compare_Hook( a, b );
                // if (num != 0)
                //     return num;
                if( codes[ i ].opcode == OpCodes.Ldarga_S && codes[ i ].operand.ToString() == "1"
                    && i + 9 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldflda && codes[ i + 1 ].operand.ToString() == "System.Int32 masterPriority"
                    && codes[ i + 2 ].opcode == OpCodes.Ldarg_0
                    && codes[ i + 3 ].opcode == OpCodes.Ldfld && codes[ i + 3 ].operand.ToString() == "System.Int32 masterPriority"
                    && codes[ i + 4 ].opcode == OpCodes.Call && codes[ i + 4 ].operand.ToString() == "Int32 CompareTo(Int32)"
                    && CodeInstructionExtensions.IsStloc( codes[ i + 5 ] )
                    && CodeInstructionExtensions.IsLdloc( codes[ i + 6 ] )
                    && codes[ i + 7 ].opcode == OpCodes.Brfalse_S
                    && CodeInstructionExtensions.IsLdloc( codes[ i + 8 ] )
                    && codes[ i + 9 ].opcode == OpCodes.Ret )
                {
                    codes.Insert( i + 10, new CodeInstruction( OpCodes.Ldarg_0 )); // load 'a'
                    codes.Insert( i + 11, new CodeInstruction( OpCodes.Ldarg_1 )); // load 'b'
                    codes.Insert( i + 12, new CodeInstruction( OpCodes.Call,
                        typeof( FetchManager_PickupComparerIncludingPriority_Patch ).GetMethod( nameof( Compare_Hook ))));
                    codes.Insert( i + 13, codes[ i + 5 ].Clone()); // stloc
                    codes.Insert( i + 14, codes[ i + 6 ].Clone()); // ldloc
                    codes.Insert( i + 15, codes[ i + 7 ].Clone()); // brfalse
                    codes.Insert( i + 16, codes[ i + 8 ].Clone()); // ldloc
                    codes.Insert( i + 17, codes[ i + 9 ].Clone()); // ret
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchManager.PickupComparerIncludingPriority.Compare()");
            return codes;
        }

        public static int Compare_Hook( FetchManager.Pickup a, FetchManager.Pickup b )
        {
            TemperatureLimit.TemperatureIndexData data = TemperatureLimit.getTemperatureIndexData();
            if( a.pickupable.PrimaryElement == null || b.pickupable.PrimaryElement == null )
                return a.pickupable.PrimaryElement == b.pickupable.PrimaryElement
                    ? 0 : a.pickupable.PrimaryElement == null ? -1 : 1;
            return data.TemperatureIndex( a.pickupable.PrimaryElement.Temperature )
                .CompareTo( data.TemperatureIndex( b.pickupable.PrimaryElement.Temperature ));
        }
    }
}
