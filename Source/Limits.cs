using HarmonyLib;
using KSerialization;
using UnityEngine;
using PeterHan.PLib.Core;
using PeterHan.PLib.UI;
using TMPro;
using System;
using STRINGS;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace DeliveryTemperatureLimit
{
    public class TemperatureLimits : KMonoBehaviour
    {
        [Serialize]
        private float lowLimit = 0; // 0 Kelvin

        [Serialize]
        private float highLimit = 0; // if 0, then not active

        public float MinValue => 0f;

        public float MaxValue => 5000f; // diamond melts at ~4200K

        private static readonly EventSystem.IntraObjectHandler<TemperatureLimits> OnCopySettingsDelegate
            = new EventSystem.IntraObjectHandler<TemperatureLimits>(delegate(TemperatureLimits component, object data)
        {
            component.OnCopySettings(data);
        });

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        private void OnCopySettings(object data)
        {
            TemperatureLimits component = ((GameObject)data).GetComponent<TemperatureLimits>();
            if (component != null)
            {
                lowLimit = component.lowLimit;
                highLimit = component.highLimit;
            }
        }

        public bool IsDisabled() => ( highLimit == 0 );
        public float LowLimit => lowLimit;
        public float HighLimit => highLimit;

        public void SetLowLimit(float value)
        {
            lowLimit = Mathf.Max( value, MinValue );
        }

        public void SetHighLimit(float value)
        {
            highLimit = Mathf.Min( value, MaxValue );
        }

        public void Disable()
        {
            highLimit = 0;
        }

        public bool AllowedByTemperature( float temperature )
        {
            if( highLimit == 0 ) // limit disabled
                return true;
            return lowLimit <= temperature && temperature <= highLimit;
        }
    }

    [HarmonyPatch(typeof(DetailsScreen))]
    [HarmonyPatch("OnPrefabInit")]
    public static class DetailsScreen_OnPrefabInit_Patch
    {
        public static void Postfix()
        {
            PUIUtils.AddSideScreenContent<TemperatureLimitsSideScreen>();
        }
    }

    public class TemperatureLimitsSideScreen : SideScreenContent
    {
        private GameObject lowInput;

        private GameObject highInput;

        private TemperatureLimits target;

        protected override void OnPrefabInit()
        {
            var margin = new RectOffset(6, 6, 6, 6);
            var baseLayout = gameObject.GetComponent<BoxLayoutGroup>();
            if (baseLayout != null)
                baseLayout.Params = new BoxLayoutParams()
                {
                    Alignment = TextAnchor.MiddleLeft,
                    Margin = margin,
                };
            PPanel panel = new PPanel("MainPanel")
            {
                Direction = PanelDirection.Horizontal,
                Margin = margin,
                Spacing = 8,
                FlexSize = Vector2.right
            };
            PTextField lowInputField = new PTextField( "lowLimit" )
            {
                    Type = PTextField.FieldType.Float,
                    OnTextChanged = OnTextChangedLow,
            };
            lowInputField.SetMinWidthInCharacters(6);
            lowInputField.AddOnRealize((obj) => lowInput = obj);
            PTextField highInputField = new PTextField( "highLimit" )
            {
                Type = PTextField.FieldType.Float,
                OnTextChanged = OnTextChangedHigh,
            };
            highInputField.SetMinWidthInCharacters(6);
            highInputField.AddOnRealize((obj) => highInput = obj);
            PLabel label = new PLabel( "label" )
            {
                TextStyle = PUITuning.Fonts.TextDarkStyle,
                Text = STRINGS.TEMPERATURELIMITS.LABEL
            };
            PLabel separator = new PLabel( "separator" )
            {
                TextStyle = PUITuning.Fonts.TextDarkStyle,
                Text = STRINGS.TEMPERATURELIMITS.RANGE_SEPARATOR
            };
            panel.AddChild( label );
            panel.AddChild( lowInputField );
            panel.AddChild( separator );
            panel.AddChild( highInputField );
            panel.AddTo( gameObject );
            ContentContainer = gameObject;
            base.OnPrefabInit();
            UpdateInputs();
        }

        public override bool IsValidForTarget(GameObject target)
        {
            return target.GetComponent<TemperatureLimits>() != null;
        }

        public override void SetTarget(GameObject new_target)
        {
            if (new_target == null)
            {
                Debug.LogError("Invalid gameObject received");
                return;
            }
            target = new_target.GetComponent<TemperatureLimits>();
            if (target == null)
            {
                Debug.LogError("The gameObject received does not contain a TemperatureLimits component");
                return;
            }
            UpdateInputs();
        }

        private void UpdateInputs()
        {
            if( target == null || lowInput == null )
                return;
            if( target.IsDisabled())
                EmptyInputs();
            else
            {
                SetLowValue( target.LowLimit );
                SetHighValue( target.HighLimit );
            }
            UpdateToolTip();
        }

        private void OnTextChangedLow(GameObject source, string text)
        {
            if( target.IsDisabled())
                SetHighValue( target.MaxValue ); // fill in a value in the other one
            float value = OnTextChanged( text, (float v) => SetLowValue( v ), target.MinValue );
            if( value != -1 && value > target.HighLimit )
                SetHighValue( value );
            UpdateToolTip();
        }

        private void OnTextChangedHigh(GameObject source, string text)
        {
            if( target.IsDisabled())
                SetLowValue( target.MinValue ); // fill in a value in the other one
            float value = OnTextChanged( text, (float v) => SetHighValue( v ), target.MaxValue );
            if( value != -1 && value < target.LowLimit )
                SetLowValue( value );
            UpdateToolTip();
        }

        private float OnTextChanged( string text, Action< float > setValueFunc, float fallback )
        {
            text = text.Trim();
            if( string.IsNullOrEmpty( text ))
            {
                target.Disable();
                EmptyInputs();
                return -1;
            }
            // TryParse() can't handle extra text at the end (temperature unit),
            // so strip it if it's there
            if( text.EndsWith( GameUtil.GetTemperatureUnitSuffix()))
                text = text.Remove( text.Length - GameUtil.GetTemperatureUnitSuffix().Length );
            float result;
            if(float.TryParse(text, out result))
                result = GameUtil.GetTemperatureConvertedToKelvin(result);
            else
                result = fallback;
            setValueFunc( result );
            return result;
        }

        private void SetLowValue( float value )
        {
            SetValue( value, lowInput,
                (float v) => target.SetLowLimit( v ),
                () => target.LowLimit );
        }

        private void SetHighValue( float value )
        {
            SetValue( value, highInput,
                (float v) => target.SetHighLimit( v ),
                () => target.HighLimit );
        }

        private void SetValue( float value, GameObject input, Action< float > setTargetFunc,
            Func< float > targetValueFunc )
        {
            setTargetFunc( value );
            value = targetValueFunc(); // maybe clamped, so re-read
            TMP_InputField field = input.GetComponent< TMP_InputField >();
            string text = GameUtil.GetFormattedTemperature(value, GameUtil.TimeSlice.None,
                GameUtil.TemperatureInterpretation.Absolute, true);
            if( field.text != text )
                field.text = text;
        }

        private void EmptyInputs()
        {
            Action< TMP_InputField > resetInput = (TMP_InputField field) =>
            {
                if( !string.IsNullOrEmpty( field.text ))
                    field.text = "";
            };
            resetInput( lowInput.GetComponent< TMP_InputField >());
            resetInput( highInput.GetComponent< TMP_InputField >());
        }

        private void UpdateToolTip()
        {
            string tooltip;
            if( !target.IsDisabled())
                tooltip  = string.Format(STRINGS.TEMPERATURELIMITS.TOOLTIP_RANGE,
                    GameUtil.GetFormattedTemperature(target.LowLimit, GameUtil.TimeSlice.None,
                        GameUtil.TemperatureInterpretation.Absolute, true),
                    GameUtil.GetFormattedTemperature(target.HighLimit, GameUtil.TimeSlice.None,
                        GameUtil.TemperatureInterpretation.Absolute, true));
            else
                tooltip = STRINGS.TEMPERATURELIMITS.TOOLTIP_NOTSET;
            PUIElements.SetToolTip( lowInput, tooltip );
            PUIElements.SetToolTip( highInput, tooltip );
        }
    }

    [HarmonyPatch(typeof(FetchManager))]
    public class FetchManager_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(IsFetchablePickup))]
        public static void IsFetchablePickup(ref bool __result, Pickupable pickup, FetchChore chore, Storage destination)
        {
            if( !__result )
                return;
            TemperatureLimits limits = destination.GetComponent< TemperatureLimits >();
            if( limits == null || limits.IsDisabled())
                return;
            __result = limits.AllowedByTemperature( pickup.PrimaryElement.Temperature );
        }
    }

    // Clearable means objects explicitly marked for sweeping. The code apparently does not
    // use IsFetchablePickup() and somehow only compares fetches, so patch it to check too.
    // Class is internal, needs to be patched manually.
    public class ClearableManager_Patch
    {
        public static void Patch( Harmony harmony )
        {
            MethodInfo info = AccessTools.Method( "ClearableManager:CollectChores" );
            if( info != null )
                harmony.Patch( info, transpiler: new HarmonyMethod(
                    typeof( ClearableManager_Patch ).GetMethod( "CollectChores" )));
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
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch ClearableManager.CollectChores()");
            return codes;
        }

        public static bool CollectChores_Hook( FetchChore chore, Pickupable pickupable )
        {
            TemperatureLimits limits = chore.destination?.GetComponent< TemperatureLimits >();
            if( limits == null || limits.IsDisabled())
                return true;
            if( pickupable?.PrimaryElement != null )
                return limits.AllowedByTemperature( pickupable.PrimaryElement.Temperature );
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
            bool found1 = false;
            bool found2 = false;
            int rootChoreLoad = -1;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                if( codes[ i ].opcode == OpCodes.Ldarg_0 && i + 1 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i + 1 ].operand.ToString() == "FetchChore rootChore" )
                {
                    rootChoreLoad = i;
                }
                // The function has code:
                // if (... && rootContext.consumerState.consumer.CanReach(pickupable2))
                // Add:
                // if (... && Begin_Hook1( rootChore, pickupable2 ))
                if( rootChoreLoad != -1 && codes[ i ].opcode == OpCodes.Ldloc_S && i + 2 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Callvirt && codes[ i + 1 ].operand.ToString() == "Boolean CanReach(IApproachable)"
                    && codes[ i + 2 ].opcode == OpCodes.Brfalse_S )
                {
                    codes.Insert( i + 3, codes[ rootChoreLoad ].Clone());
                    codes.Insert( i + 4, codes[ rootChoreLoad + 1 ].Clone()); // load 'rootChore'
                    codes.Insert( i + 5, codes[ i ].Clone()); // load 'pickupable2'
                    codes.Insert( i + 6, new CodeInstruction( OpCodes.Call,
                        typeof( FetchAreaChore_StatesInstance_Patch ).GetMethod( nameof( Begin_Hook1 ))));
                    codes.Insert( i + 7, codes[ i + 2 ].Clone()); // if false
                    found1 = true;
                }
                // The function has code:
                // if (... && fetchChore2.forbidHash == rootChore.forbidHash)
                // Add:
                // if (... && Begin_Hook2( rootChore, fetchChore2 ))
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
                    codes.Insert( i + 9, codes[ i + 1 ].Clone()); // load 'fetchChore2'
                    codes.Insert( i + 10, new CodeInstruction( OpCodes.Call,
                        typeof( FetchAreaChore_StatesInstance_Patch ).GetMethod( nameof( Begin_Hook2 ))));
                    codes.Insert( i + 11, codes[ i ].Clone()); // if false
                    found2 = true;
                }
            }
            if(!found1 || !found2)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch FetchAreaChore.StatesInstance.Begin()");
            return codes;
        }

        public static bool Begin_Hook1( FetchChore rootChore, Pickupable pickupable2 )
        {
            TemperatureLimits limits = rootChore.destination?.GetComponent< TemperatureLimits >();
            if( limits == null || limits.IsDisabled())
                return true;
            return limits.AllowedByTemperature( pickupable2.PrimaryElement.Temperature );
        }

        public static bool Begin_Hook2( FetchChore rootChore, FetchChore fetchChore2 )
        {
            TemperatureLimits limits = rootChore?.destination?.GetComponent< TemperatureLimits >();
            Pickupable pickupable2 = fetchChore2?.fetchTarget;
            if( limits == null || limits.IsDisabled() || pickupable2 == null )
                return true;
            return limits.AllowedByTemperature( pickupable2.PrimaryElement.Temperature );
        }
    }

    [HarmonyPatch(typeof(GlobalChoreProvider))]
    public class GlobalChoreProvider_Patch
    {
        // List of allowed temperature ranges for each tag.
        private class TagData
        {
            // Low/high limit. If not set (null), there's no limit.
            public List< ValueTuple< float, float >> limits;
        }
        // Stored for each GlobalChoreProvider.
        private class PerProviderData
        {
            public Dictionary< Tag, TagData > tagData = new Dictionary< Tag, TagData >();
        }
        private static Dictionary< GlobalChoreProvider, PerProviderData > storageFetchableTagsWithTemperature
            = new Dictionary< GlobalChoreProvider, PerProviderData >();
        // Optimization, look it up just once in the first hook.
        private static PerProviderData currentProvider = null;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ClearableHasDestination))]
        public static void ClearableHasDestination(GlobalChoreProvider __instance, ref bool __result, Pickupable pickupable)
        {
            if( !__result ) // Has no destination already without temperature check.
                return;
            PerProviderData perProvider = storageFetchableTagsWithTemperature[ __instance ];
            if( perProvider == null )
                return;
            KPrefabID kPrefabID = pickupable.KPrefabID;
            TagData tagData;
            if( !perProvider.tagData.TryGetValue( kPrefabID.PrefabTag, out tagData ))
            {
                __result = false; // tag not included => not allowed
                return;
            }
            if( tagData.limits == null ) // All allowed.
                return;
            if( pickupable.PrimaryElement == null )
                return;
            float temperature = pickupable.PrimaryElement.Temperature;
            foreach( ValueTuple< float, float > limit in tagData.limits )
            {
                if( limit.Item1 <= temperature && temperature <= limit.Item2 )
                    return; // ok, found a valid range
            }
            __result = false; // no storage that'd allow the temperature
        }

        // This function updates a hash of allowed tags for ClearableHasDestination.
        // Patch it to build our information that includes temperature limits.
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(UpdateStorageFetchableBits))]
        public static IEnumerable<CodeInstruction> UpdateStorageFetchableBits(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            // Insert 'UpdateStorageFetchableBits_Hook1( this )' at the beginning.
            codes.Insert( 0, new CodeInstruction( OpCodes.Ldarg_0 )); // load 'this'
            codes.Insert( 1, new CodeInstruction( OpCodes.Call,
                typeof( GlobalChoreProvider_Patch ).GetMethod( nameof( UpdateStorageFetchableBits_Hook1 ))));
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
//                Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
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
                    found = true;
                }
            }
            if(!found)
                Debug.LogWarning("DeliveryTemperatureLimit: Failed to patch GlobalChoreProvider.UpdateStorageFetchableBits()");
            return codes;
        }

        public static void UpdateStorageFetchableBits_Hook1(GlobalChoreProvider provider)
        {
            if( !storageFetchableTagsWithTemperature.TryGetValue( provider, out currentProvider ))
            {
                currentProvider = new PerProviderData();
                storageFetchableTagsWithTemperature[ provider ] = currentProvider;
            }
            currentProvider.tagData.Clear();
        }

        public static void UpdateStorageFetchableBits_Hook2(FetchChore chore)
        {
            TemperatureLimits limits = chore.destination.GetComponent< TemperatureLimits >();
            if( limits == null || limits.IsDisabled())
            {
                foreach( Tag tag in chore.tags )
                {
                    TagData tagData;
                    if( !currentProvider.tagData.TryGetValue( tag, out tagData ))
                    {
                        tagData = new TagData();
                        currentProvider.tagData[ tag ] = tagData;
                    }
                    else if( tagData.limits != null )
                        tagData.limits = null; // All allowed.
                }
                return;
            }
            foreach( Tag tag in chore.tags )
            {
                TagData tagData;
                if( !currentProvider.tagData.TryGetValue( tag, out tagData ))
                {
                    tagData = new TagData();
                    // We will be adding a limit, so set up the list (which means not all are allowed).
                    tagData.limits = new List< ValueTuple< float, float >>();
                    currentProvider.tagData[ tag ] = tagData;
                }
                if( tagData.limits == null ) // All allowed.
                    continue;
                bool found = false;
                foreach( ValueTuple< float, float > limitItem in tagData.limits )
                {
                    if( limitItem.Item1 <= limits.LowLimit && limits.HighLimit <= limitItem.Item2 )
                    {
                        found = true;
                        break; // ok, included in another range
                    }
                }
                if( !found )
                    tagData.limits.Add( ValueTuple.Create( limits.LowLimit, limits.HighLimit ));
            }
        }
    }

    // Now add to all buildings where this makes sense.
    [HarmonyPatch(typeof(StorageLockerSmartConfig))]
    public class StorageLockerSmartConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(StorageLockerConfig))]
    public class StorageLockerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(ObjectDispenserConfig))]
    public class ObjectDispenserConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(OrbitalCargoModuleConfig))]
    public class OrbitalCargoModuleConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(SolidConduitInboxConfig))]
    public class SolidConduitInboxConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

#if false
    [HarmonyPatch(typeof(BottleEmptierConfig))]
    public class BottleEmptierConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(BottleEmptierGasConfig))]
    public class BottleEmptierGasConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(WaterCoolerConfig))]
    public class WaterCoolerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(JuicerConfig))]
    public class JuicerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(SublimationStationConfig))]
    public class SublimationStationConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(AlgaeHabitatConfig))]
    public class AlgaeHabitatConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(WoodGasGeneratorConfig))]
    public class WoodGasGeneratorConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(ResearchCenterConfig))]
    public class ResearchCenterConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(AdvancedResearchCenterConfig))]
    public class AdvancedResearchCenterConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(RustDeoxidizerConfig))]
    public class RustDeoxidizerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(MechanicalSurfboardConfig))]
    public class MechanicalSurfboardConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(IceMachineConfig))]
    public class IceMachineConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(WashBasinConfig))]
    public class WashBasinConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(FarmStationConfig))]
    public class FarmStationConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(EspressoMachineConfig))]
    public class EspressoMachineConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(OuthouseConfig))]
    public class OuthouseConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(SodaFountainConfig))]
    public class SodaFountainConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(PlanterBoxConfig))]
    public class PlanterBoxConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(CompostConfig))]
    public class CompostConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(AirFilterConfig))]
    public class AirFilterConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(AlgaeDistilleryConfig))]
    public class AlgaeDistilleryConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(WaterPurifierConfig))]
    public class WaterPurifierConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(OxyliteRefineryConfig))]
    public class OxyliteRefineryConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(MineralDeoxidizerConfig))]
    public class MineralDeoxidizerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(HandSanitizerConfig))]
    public class HandSanitizerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(FertilizerMakerConfig))]
    public class FertilizerMakerConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(DiningTableConfig))]
    public class DiningTableConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }

    [HarmonyPatch(typeof(CreatureFeederConfig))]
    public class CreatureFeederConfig_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<TemperatureLimits>();
        }
    }
 #endif
}
