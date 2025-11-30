using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

using AmountByTagIndexDict = System.Collections.Generic.Dictionary< ( Tag, int ), float >;

// Update also status items such as 'Building lacks resources'.
// Support both game code and FastTrack.
// FastTrack replaces code both for updating the status itself and for collecting world inventory,
// both of which can be enabled independently, so the code should handle all combinations.
namespace DeliveryTemperatureLimit
{

    public static class StatusItemsUpdaterPatch
    {
        public static void Patch( Harmony harmony )
        {
            if( !Options.Instance.CheckTemperatureForStatusItems )
                return;

            MethodInfo infoRender200ms = AccessTools.Method( typeof( FetchListStatusItemUpdater ), "Render200ms" );
            if( infoRender200ms != null )
                harmony.Patch( infoRender200ms,
                    transpiler: new HarmonyMethod( typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( Render200ms ))));
            else
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to find FetchListStatusItemUpdater.Render200ms().");

            MethodInfo infoUpdateStatus = AccessTools.Method(
                Type.GetType( "PeterHan.FastTrack.GamePatches.FetchListStatusItemUpdater_Render200ms_Patch, FastTrack" ),
                "UpdateStatus" );
            if( infoUpdateStatus != null )
                harmony.Patch( infoUpdateStatus, transpiler: new HarmonyMethod(
                    typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( UpdateStatus ))));

            MethodInfo infoWorldInventoryUpdate = AccessTools.Method( typeof( WorldInventory ), "Update" );
            if( infoWorldInventoryUpdate != null )
                harmony.Patch( infoWorldInventoryUpdate,
                    transpiler: new HarmonyMethod( typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( WorldInventoryUpdate ))),
                    postfix: new HarmonyMethod( typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( WorldInventoryUpdate_Postfix ))));
            else
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to find WorldInventory.Update().");

            MethodInfo infoStartUpdateAll = AccessTools.Method(
                Type.GetType( "PeterHan.FastTrack.UIPatches.BackgroundInventoryUpdater, FastTrack" ),
                "StartUpdateAll" );
            MethodInfo infoRunUpdate = AccessTools.Method(
                Type.GetType( "PeterHan.FastTrack.UIPatches.BackgroundWorldInventory, FastTrack" ),
                "RunUpdate" );
            MethodInfo infoSumTotal = AccessTools.Method(
                Type.GetType( "PeterHan.FastTrack.UIPatches.BackgroundWorldInventory, FastTrack" ),
                "SumTotal" );
            if( infoStartUpdateAll != null && infoRunUpdate != null && infoSumTotal != null )
            {
                harmony.Patch( infoStartUpdateAll, prefix: new HarmonyMethod(
                    typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( StartUpdateAll_Prefix ))));
                harmony.Patch( infoRunUpdate,
                    transpiler: new HarmonyMethod( typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( RunUpdate ))),
                    postfix: new HarmonyMethod( typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( RunUpdate_Postfix ))));
                harmony.Patch( infoSumTotal, transpiler: new HarmonyMethod(
                    typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( SumTotal ))));
            }
        }

        // Keeping track of amounts for each world+tag+temperature combo.
        private static Dictionary< int, AmountByTagIndexDict > worldAmounts = new Dictionary< int, AmountByTagIndexDict >();

        // Game's FetchListStatusItemUpdater.Render200ms().
        public static IEnumerable<CodeInstruction> Render200ms(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            LocalBuilder worldContainer = null;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // int id = worldContainer.id;
                // Here 'worldContainer' is actually only a temporary variable on the IL stack, so prepend storing it:
                // WorldContainer worldContainer = worldContainer;
                if( codes[ i ].opcode == OpCodes.Call && codes[ i ].operand.ToString() == "WorldContainer get_Current()"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i + 1 ].operand.ToString() == "System.Int32 id" )
                {
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Dup ));
                    worldContainer = generator.DeclareLocal( typeof( WorldContainer ));
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Stloc_S, worldContainer.LocalIndex )); // store 'worldContainer'
                }
                // The function has code:
                // float minimumAmount = item8.GetMinimumAmount(key);
                // Append:
                // Render200ms_Hook( num3, ref num6, item8, id, key, value2, minimumAmount );
                if( codes[ i ].opcode == OpCodes.Callvirt && codes[ i ].operand.ToString() == "Single GetMinimumAmount(Tag)"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Stloc_S && codes[ i + 1 ].operand.ToString() == "System.Single (38)" )
                {
                    // Load all the arguments, this of course depends on the exact code (but this patch already in practice
                    // depends on the exact code anyway). The following code uses only 'num6' out of the values that we
                    // change, so that's the only one with a reference.
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Dup )); // load 'num3', the code leaves it on the stack
                    codes.Insert( i + 3, new CodeInstruction( OpCodes.Ldloca_S, 37 )); // ref 'num6'
                    codes.Insert( i + 4, new CodeInstruction( OpCodes.Ldloc_S, 28 )); // 'item8'
                    codes.Insert( i + 5, new CodeInstruction( OpCodes.Ldloc_S, worldContainer.LocalIndex )); // load 'worldContainer'
                    codes.Insert( i + 6, new CodeInstruction( OpCodes.Ldloc_S, 33 )); // 'key'
                    codes.Insert( i + 7, new CodeInstruction( OpCodes.Ldloc_S, 34 )); // 'value2'
                    codes.Insert( i + 8, new CodeInstruction( OpCodes.Ldloc_S, 38 )); // 'minimumAmount'
                    codes.Insert( i + 9, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( Render200ms_Hook ))));
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchListStatusItemUpdater.Render200ms()");
            return codes;
        }

        public static void Render200ms_Hook( float num3, ref float num6, FetchList2 item8,
            WorldContainer worldContainer, Tag key, float value2, float minimumAmount )
        {
            UpdateAvailable( item8, worldContainer, key, ref num6, num3, value2, minimumAmount );
        }

        // FastTrack's code for updating status.
        public static IEnumerable<CodeInstruction> UpdateStatus(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // float minimumAmount = errand.GetMinimumAmount(tag);
                // Append:
                // UpdateStatus_Hook( errand, inventory, tag, inStorage, ref fetchable, minimumAmount, remaining );
                if( codes[ i ].opcode == OpCodes.Callvirt && codes[ i ].operand.ToString() == "Single GetMinimumAmount(Tag)"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Stloc_S && codes[ i + 1 ].operand.ToString() == "System.Single (13)" )
                {
                    // Load all the arguments, this of course depends on the exact code (but this patch already in practice
                    // depends on the exact code anyway).
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Ldarg_0 )); // 'errand'
                    codes.Insert( i + 3, new CodeInstruction( OpCodes.Ldarg_S, 4 )); // 'inventory'
                    codes.Insert( i + 4, new CodeInstruction( OpCodes.Ldloc_S, 7 )); // 'tag'
                    codes.Insert( i + 5, new CodeInstruction( OpCodes.Ldloca_S, 12 )); // 'fetchable'
                    codes.Insert( i + 6, new CodeInstruction( OpCodes.Ldloc_S, 9 )); // 'inStorage'
                    codes.Insert( i + 7, new CodeInstruction( OpCodes.Ldloc_S, 8 )); // 'remaining'
                    codes.Insert( i + 8, new CodeInstruction( OpCodes.Ldloc_S, 13 )); // 'minimumAmount'
                    codes.Insert( i + 9, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( UpdateStatus_Hook ))));
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchListStatusItemUpdater_Render200ms_Patch.UpdateStatus()");
            return codes;
        }

        public static void UpdateStatus_Hook( FetchList2 errand, WorldInventory inventory, Tag tag,
            ref float fetchable, float inStorage, float remaining, float minimumAmount )
        {
            UpdateAvailable( errand, inventory.WorldContainer, tag, ref fetchable, inStorage, remaining, minimumAmount );
        }

        // Shared code for updating status. Possibly compute again available amount based on temperature
        // of items.
        public static void UpdateAvailable( FetchList2 errand, WorldContainer world, Tag tag,
            ref float fetchable, float inStorage, float remaining, float minimumAmount )
        {
            if( inStorage + fetchable < minimumAmount )
                return; // No need to bother if there are no materials even without considering temperature.
            TemperatureLimit limit = TemperatureLimit.Get( errand.Destination?.gameObject );
            if( limit == null || limit.IsDisabled())
                return;
            TemperatureLimit.TemperatureIndexData data = TemperatureLimit.getTemperatureIndexData();
            ( int lowIndex, int highIndex ) = data.TemperatureIndexes( limit );
            float total = 0;
            // Sum up amounts for all indexes included in the range.
            // Make sure to include amounts from sub-worlds and/or the parent world.
            // TODO: Would it be worth it to cache this?
            int parentWorldId = world.ParentWorldId;
            for( int index = lowIndex; index < highIndex; ++index )
            {
                foreach( WorldContainer world2 in ClusterManager.Instance.WorldContainers )
                {
                    if( world2.ParentWorldId == parentWorldId ) // (Parent points to self if does not exist.)
                    {
                        // This is a race condition, as the indexes may change before the world amounts
                        // info is updated, so cope with that. The proper value will eventually be calculated.
                        try
                        {
                            total += worldAmounts[ world2.id ][ ( tag, index ) ];
                        } catch( KeyNotFoundException )
                        {
                        }
                    }
                }
            }
            // Treat total and available the same. The latter is the sooner, with reserved amounts removed,
            // but the MaterialNeeds class also does not include temperature, and tracking that would be a lot of work
            // for minimal gain. At worst this should result in insufficient resources getting reported with a delay,
            // after that need is served and the total amount also decreases.
            // (And yes, it seems broken to sum available and total, but that's what the game code does.)
            float available = total;
            // And fix the available amount.
            fetchable = available + Mathf.Min(remaining, total);
        }

        // Patch WorldInventory.Update() to track amounts also per temperature index, not just tag.
        public static IEnumerable<CodeInstruction> WorldInventoryUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found1 = false;
            bool found2 = false;
            bool found3 = false;
            int keyIndex = -1;
            int num2Index = -1;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // Get location of 'num2'.
                // int num2 = worldId;
                if( num2Index == -1
                    && codes[ i ].opcode == OpCodes.Call && codes[ i ].operand.ToString() == "Int32 get_worldId()"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].IsStloc())
                {
                    num2Index = codes[ i + 1 ].LocalIndex();
                }
                // Get location of 'key'.
                // Tag key = current.Key;
                if( keyIndex == -1
                    && codes[ i ].opcode == OpCodes.Call && codes[ i ].operand.ToString() == "Tag get_Key()"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].IsStloc())
                {
                    keyIndex = codes[ i + 1 ].LocalIndex();
                }
                // The function has code:
                // num3 = 0;
                // Append:
                // WorldInventoryUpdate_Hook1( num2, key );
                if( keyIndex != -1 && num2Index != -1
                    && codes[ i ].opcode == OpCodes.Ldc_R4 && codes[ i ].operand.ToString() == "0"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Stloc_S && codes[ i + 1 ].operand.ToString() == "System.Single (5)" )
                {
                    codes.Insert( i + 2, CodeInstruction2.LoadLocal( num2Index )); // load 'num2'
                    codes.Insert( i + 3, CodeInstruction2.LoadLocal( keyIndex )); // load 'key'
                    codes.Insert( i + 4, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( WorldInventoryUpdate_Hook1 ))));
                    found1 = true;
                }
                // The function has code:
                // num3 += item.TotalAmount;
                // Append:
                // WorldInventoryUpdate_Hook2( item );
                if( codes[ i ].opcode == OpCodes.Ldloc_S && codes[ i ].operand.ToString().StartsWith( "Pickupable (" )
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Callvirt && codes[ i + 1 ].operand.ToString() == "Single get_TotalAmount()" )
                {
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Dup )); // create a copy of 'item'
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( WorldInventoryUpdate_Hook2 ))));
                    found2 = true;
                }
                // The function has code:
                // accessibleAmounts[key] = num3;
                // Append:
                // WorldInventoryUpdate_Hook3( key, num2 );
                if( codes[ i ].opcode == OpCodes.Ldfld && codes[ i ].operand.ToString().EndsWith( " accessibleAmounts" ))
                {
                    codes.Insert( i + 1, CodeInstruction2.LoadLocal( keyIndex )); // load 'key'
                    codes.Insert( i + 2, CodeInstruction2.LoadLocal( num2Index )); // load 'num2'
                    codes.Insert( i + 3, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( WorldInventoryUpdate_Hook3 ))));
                    found3 = true;
                }
            }
            if(!found1 || !found2 || !found3)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch WorldInventory.Update()");
            return codes;
        }

        // WorldInventory.Update() runs in a single thread, so simply use statics for data.
        private static float[] updateSums;
        private static TemperatureLimit.TemperatureIndexData updateIndexData;

        public static void WorldInventoryUpdate_Hook1( int worldId, Tag key )
        {
            updateIndexData = TemperatureLimit.getTemperatureIndexData();
            if( updateSums == null || updateSums.Length != updateIndexData.TemperatureIndexCount())
                updateSums = new float[ updateIndexData.TemperatureIndexCount() ];
            Array.Clear( updateSums, 0, updateSums.Length ); // set all elements to 0
        }

        public static void WorldInventoryUpdate_Hook2( Pickupable item )
        {
            if( item.PrimaryElement == null )
                return;
            updateSums[ updateIndexData.TemperatureIndex( item.PrimaryElement.Temperature ) ] += item.TotalAmount;
        }

        public static void WorldInventoryUpdate_Hook3( Tag key, int worldId )
        {
            if( !worldAmounts.TryGetValue( worldId, out AmountByTagIndexDict amounts ))
            {
                amounts = new AmountByTagIndexDict();
                worldAmounts[ worldId ] = amounts;
            }
            for( int i = 0; i < updateSums.Length; ++i )
                amounts[ ( key, i ) ] = updateSums[ i ];
        }

        public static void WorldInventoryUpdate_Postfix()
        {
            updateIndexData = null;
        }

        // FastTrack's world inventory code. StartUpdateAll() starts running RunUpdate() in threads, and RunUpdate()
        // calls into SumTotal() for each world+tag combo.
        // This needs to be thread-safe. For this reason, the setup is that StartUpdateAll() ensures worldAmounts()
        // has the proper dictionary entry for all worlds, so that the shared dictionary (itself) is not modified in a thread.
        // RunUpdate() will update the relevant dictionary entry item for its world.
        public static void StartUpdateAll_Prefix()
        {
            var inst = ClusterManager.Instance;
            /*var jm = AsyncJobManager.Instance;*/
            if (!SpeedControlScreen.Instance.IsPaused/* && FastTrackMod.GameRunning && inst != null && jm != null*/)
            {
                var worlds = inst.WorldContainers;
                foreach (var container in worlds)
                {
                    int worldId = container.id;
                    // We use a dictionary to store per-world data, so make sure the dictionary has an item
                    // for each world, so that the dictionary itself is not modified in threads, only its items will be.
                    // TODO: Maybe do this when world is added/removed?
                    if( worldId >= 0 && worldId != ClusterManager.INVALID_WORLD_IDX )
                    {
                        if( !worldAmounts.ContainsKey( worldId ))
                            worldAmounts[ worldId ] = new AmountByTagIndexDict();
                    }
                }
            }
        }

        public static IEnumerable<CodeInstruction> RunUpdate(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found1 = false;
            int found2Count = 0;
            LocalBuilder localAmounts = null;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // ui = updateIndex
                // Append:
                // AmountByTagIndexDict amounts = RunUpdate_Hook1( worldId );
                if( codes[ i ].opcode == OpCodes.Ldarg_0
                    && i + 2 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i + 1 ].operand.ToString() == "System.Int32 updateIndex"
                    && codes[ i + 2 ].opcode == OpCodes.Stloc_S )
                {
                    codes.Insert( i + 3, new CodeInstruction( OpCodes.Ldloc_0 )); // load 'worldId'
                    codes.Insert( i + 4, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( RunUpdate_Hook1 ))));
                    localAmounts = generator.DeclareLocal( typeof( AmountByTagIndexDict ));
                    codes.Insert( i + 5, new CodeInstruction( OpCodes.Stloc_S, localAmounts.LocalIndex )); // store 'amounts'
                    found1 = true;
                }
                // The function has code (two times):
                // accessibleAmounts[pair.Key] = SumTotal(pair.Value, worldId);
                // And append:
                // RunUpdate_Hook2( pair.Key, amounts );
                if( found1 && codes[ i ].opcode == OpCodes.Call
                    && codes[ i ].operand.ToString() == "Tag get_Key()"
                    && codes[ i + 3 ].opcode == OpCodes.Ldloc_0
                    && i + 4 < codes.Count
                    && codes[ i + 4 ].opcode == OpCodes.Call
                    && codes[ i + 4 ].operand.ToString() == "Single SumTotal(System.Collections.Generic.IEnumerable`1[Pickupable], Int32)" )
                {
                    // Right after calling SumTotal() and before the Dictionary operator [] is called,
                    // the stack contains 'accessibleAmounts', 'pair.Key' and the return value of SumTotal().
                    // Duplicate 'pair.Key', add 'amounts', call the hook function, and it'll return the passed
                    // SumTotal() result so that the stack is in the same state again.
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Dup )); // duplicate 'pair.Key'
                    codes.Insert( i + 1 + 5, new CodeInstruction( OpCodes.Ldloc_S, localAmounts.LocalIndex )); // load 'amounts'
                    codes.Insert( i + 1 + 6, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( RunUpdate_Hook2 ))));
                    ++found2Count;
                }
            }
            if( !found1 || found2Count != 2 )
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch BackgroundWorldInventory.RunUpdate()");
            return codes;
        }

        // We'd need to add another parameter to SumTotals(), which AFAIK cannot be done. So instead
        // use a static variable to pass totals per temperature index, and since this runs in threads,
        // it has to be thread-local.
        public class SumTotalData
        {
            public float[] sumTotals;
            public TemperatureLimit.TemperatureIndexData indexData;
        };

        [ThreadStatic]
        private static SumTotalData sumTotalData;

        public static AmountByTagIndexDict RunUpdate_Hook1( int worldId )
        {
            // Prepare the floats array where to store sums per temperature index.
            SumTotalData data = sumTotalData;
            if( data == null )
                data = sumTotalData = new SumTotalData();
            data.indexData = TemperatureLimit.getTemperatureIndexData();
            if( data.sumTotals == null || data.sumTotals.Length != data.indexData.TemperatureIndexCount())
                data.sumTotals = new float[ data.indexData.TemperatureIndexCount() ];
            Array.Clear( data.sumTotals, 0, data.sumTotals.Length ); // set all elements to 0
            // Return the AmountByTagIndexDict for this world to use in this thread.
            return worldAmounts[ worldId ];
        }

        public static float RunUpdate_Hook2( Tag key, float sumTotalResult, AmountByTagIndexDict amounts )
        {
            // The sum totals needed to be calculated in separate array (so that worldAmounts dictionary
            // does not contain work-in-progress sums that could be meanwhile read by the main thread),
            // and now only write the totals.
            SumTotalData data = sumTotalData;
            for( int i = 0; i < data.sumTotals.Length; ++i )
                amounts[ ( key, i ) ] = data.sumTotals[ i ];
            return sumTotalResult;
        }

        public static void RunUpdate_Postfix()
        {
            updateIndexData = null;
        }

        public static IEnumerable<CodeInstruction> SumTotal(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            // I don't know the compiler will be smart enough to optimize the TLS access, so cache the variable.
            // At the very beginning of the function, add:
            // SumTotalData data = SumTotal_Hook1();
            codes.Insert( 0, new CodeInstruction( OpCodes.Call,
                typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( SumTotal_Hook1 ))));
            LocalBuilder localSumTotalData = generator.DeclareLocal( typeof( SumTotalData ));
            codes.Insert( 1, new CodeInstruction( OpCodes.Stloc_S, localSumTotalData.LocalIndex )); // store 'data'
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code (two times):
                // total += pickupable.TotalAmount;
                // Append:
                // SumTotal_Hook2( pickupable, data );
                if( codes[ i ].opcode == OpCodes.Ldloc_S && codes[ i ].operand.ToString() == "Pickupable (4)"
                    && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Callvirt && codes[ i + 1 ].operand.ToString() == "Single get_TotalAmount()" )
                {
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Dup )); // create a copy of 'pickupable'
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Ldloc_S, localSumTotalData.LocalIndex )); // load 'data'
                    codes.Insert( i + 3, new CodeInstruction( OpCodes.Call,
                        typeof( StatusItemsUpdaterPatch ).GetMethod( nameof( SumTotal_Hook2 ))));
                    found = true;
                    break;
                }
            }
            if( !found )
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch BackgroundWorldInventory.SumTotal()");
            return codes;
        }

        public static SumTotalData SumTotal_Hook1()
        {
            return sumTotalData;
        }

        public static void SumTotal_Hook2( Pickupable pickupable, SumTotalData data )
        {
            if( pickupable.PrimaryElement == null )
                return;
            data.sumTotals[ data.indexData.TemperatureIndex( pickupable.PrimaryElement.Temperature ) ] += pickupable.TotalAmount;
        }
    }
}
