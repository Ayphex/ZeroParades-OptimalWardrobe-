// === FILE: ModSource/Plugin.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA v33.1.0
// Clean-Slate Modular Architecture for Optimal Wardrobe.
// Zero-ClassInjector & Zero-DelegateSupport design for full Unity 6000.3+ IL2CPP
// compatibility (including stock BepInEx 6 setups with UnityLogListening = false).

using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.SceneManagement;
using ZAUM.FELD.C4.Dialogues.Model.Cards;
using ZeroParadesOptimalWardrobe.Core;

namespace ZeroParadesOptimalWardrobe
{
    // Logging alias for internal engine calls
    public static class FeldLog
    {
        public static void Log(string message) => Plugin.WriteTrace(message);
        public static void LogWarning(string message) => Plugin.Logger?.LogWarning(message);
        public static void LogError(string message) => Plugin.Logger?.LogError(message);
    }

    [BepInPlugin("com.keith.zeroparades.optimalwardrobe", "Zero Parades: Optimal Wardrobe", "33.1.0")]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Logger { get; private set; }

        // ====================================================================
        // SEALED RUNTIME INTEROP & CORE ENGINE STATE
        // ====================================================================
        public static unsafe NativeModConfig* PtrNativeConfig;
        public static object ActiveModifierList = null;
        public static ChecksActiveModifiersInfoItem SyntheticModifier = null;
        public static IntPtr SyntheticModifierPtr = IntPtr.Zero;

        // Unmanaged Native Config Buffer
        private static IntPtr _nativeConfigPtr = IntPtr.Zero;

        // Retained legacy pointer fields (unused; zero unrooted IL2CPP heap allocations)
        public static IntPtr PtrStashAssist = IntPtr.Zero;
        public static IntPtr PtrUniqueBis = IntPtr.Zero;
        public static IntPtr PtrSharedBis = IntPtr.Zero;
        public static IntPtr PtrCosmetic = IntPtr.Zero;

        // Legacy compatibility placeholder (no MonoBehaviour / ClassInjector used)
        public static object SyncInstance = null;

        public static void WriteTrace(string line)
        {
            Logger?.LogInfo(line);
        }

        public static void OnCheckStarted()
        {
            LifecycleGuard.IsCheckActive = true;
            WriteTrace("[CHECK-LIFECYCLE] Check Triggered: Stash assistance active.");
        }

        public static void OnCheckFinished()
        {
            LifecycleGuard.IsCheckActive = false;
            if (ActiveModifierList != null)
            {
                try
                {
                    if (ActiveModifierList is Il2CppSystem.Collections.Generic.List<ChecksActiveModifiersInfoItem> typedList)
                    {
                        for (int i = typedList.Count - 1; i >= 0; i--)
                        {
                            var mod = typedList[i];
                            if (mod == null || mod.WasCollected || mod.Pointer == IntPtr.Zero)
                                continue;

                            string exp = null;
                            try { exp = mod.Explanation; } catch { }
                            if (exp != null && exp.Contains("Optimal Wardrobe"))
                            {
                                typedList.RemoveAt(i);
                            }
                        }
                    }
                    else if (SyntheticModifier != null && !SyntheticModifier.WasCollected && ActiveModifierList is Il2CppSystem.Collections.IList il2cppList)
                    {
                        il2cppList.Remove(SyntheticModifier);
                    }
                }
                catch { }
                ActiveModifierList = null;
            }
            WriteTrace("[CHECK-LIFECYCLE] Check Finished: Cleanly unlinked modifiers post-roll.");
        }

        public static int GetDesiredDeltaFast(string skillId)
        {
            if (WardrobeConditioningEngine.IsSkillSuppressed(skillId)) return 0;
            int delta = WardrobeConditioningEngine.GetFinalSkillDelta(skillId);
            return delta;
        }

        // ====================================================================
        // CATEGORY 1: CORE MODULES & FEATURES (STATIC ENTRIES)
        // ====================================================================
        public static ConfigEntry<bool> ShopAdvisorEnabled;
        public static ConfigEntry<bool> BestInSlotTagsEnabled;
        public static ConfigEntry<bool> StashAssistanceEnabled;
        public static ConfigEntry<bool> s_AffectPassiveChecks;
        public static ConfigEntry<bool> s_SuppressEquippedPenalties;
        public static ConfigEntry<bool> IncludeTools;

        // ====================================================================
        // CATEGORY 2: BALANCE & MATH TUNING (STATIC ENTRIES)
        // ====================================================================
        public static ConfigEntry<float> RcRatio;
        public static ConfigEntry<int> CkBonusCap;
        public static ConfigEntry<int> MaxDeltaCap;
        public static ConfigEntry<int> BaseStatCap;
        public static ConfigEntry<bool> s_DiminishingReturnsEnabled;
        public static ConfigEntry<int> DiminishingThreshold;
        public static ConfigEntry<float> DiminishingRatio;

        // ====================================================================
        // CATEGORY 3: ROLEPLAY STAT SUPPRESSION (15 SKILLS)
        // ====================================================================
        public static ConfigEntry<bool>[] SkillSuppressionEntries = new ConfigEntry<bool>[15];

        // Legacy backing entries for sealed compatibility
        public static ConfigEntry<bool> s_ModEnabled;
        public static ConfigEntry<bool> s_ForceRecalculateNow;
        public static ConfigEntry<ulong> s_SuppressedSkillsMask;

        // ====================================================================
        // BACKWARD-COMPATIBILITY INSTANCE PROPERTIES (FOR SEALED FILES)
        // ====================================================================
        public ConfigEntry<bool> ModEnabled => s_ModEnabled;
        public ConfigEntry<bool> ForceRecalculateNow => s_ForceRecalculateNow;
        public ConfigEntry<bool> SuppressEquippedPenalties => s_SuppressEquippedPenalties;
        public ConfigEntry<float> ConditioningRatio => RcRatio;
        public ConfigEntry<bool> DiminishingReturnsEnabled => s_DiminishingReturnsEnabled;
        public ConfigEntry<bool> AffectPassiveChecks => s_AffectPassiveChecks;
        public ConfigEntry<int> StashAssistBonusCap => CkBonusCap;
        public ConfigEntry<ulong> SuppressedSkillsMask => s_SuppressedSkillsMask;

        private static readonly string[] s_SkillDisplayNames = new string[15]
        {
            "Coordination", "Doppelgang", "Instincts", "Sensors", "Shadowplay",
            "Blueprints", "ColdRead", "Nerve", "Personalism", "Statehood",
            "Entanglement", "GreyMatter", "Poetics", "Records", "Technoflex"
        };

        public override void Load()
        {
            Instance = this;
            Logger = base.Log;

            WriteTrace("=== Optimal Wardrobe v33.1.0 Initializing (Zero-Injector Headless Modular Engine) ===");

            // 1. Bind configuration & allocate unmanaged config struct BEFORE syncing parameters
            BindConfiguration();
            InitNativeModConfig();
            SyncAllToEngine();

            // 2. Hook live config file changes (pure managed event, no IL2CPP delegate conversion)
            Config.SettingChanged += OnSettingChanged;

            // 3. Start pure-managed startup grace clock (no ClassInjector / MonoBehaviour needed)
            LifecycleGuard.ResetStartupGrace();

            // 4. Apply Core Gameplay Patches safely with native MethodPointer / ICF / thunk validation
            var harmony = new Harmony("com.keith.zeroparades.optimalwardrobe");
            SafeHarmonyPatcher.ApplyAllPatches(harmony);

            WriteTrace("=== Optimal Wardrobe v33.1.0 Loaded Successfully. ===");
        }

        private void BindConfiguration()
        {
            // Category 1: Core Modules
            ShopAdvisorEnabled = Config.Bind("1. Core Modules", "ShopUpgradeAdvisor", true, "Pre-purchase store analysis. Tags store items as UPGRADE or REDUNDANT against stash.");
            BestInSlotTagsEnabled = Config.Bind("1. Core Modules", "BestInSlotBadges", true, "Inventory organization badges. Injects [OPTIMAL] tags.");
            StashAssistanceEnabled = Config.Bind("1. Core Modules", "StashAssistance", true, "Unequipped stash wardrobe assists active dice checks.");
            s_AffectPassiveChecks = Config.Bind(
                "1. Core Modules",
                "AffectPassiveChecks",
                false,
                "Extends unequipped wardrobe assistance to passive world checks."
            );
            s_SuppressEquippedPenalties = Config.Bind("1. Core Modules", "SuppressEquippedPenalties", true, "Zeroes out negative stat debuffs on equipped clothing.");
            IncludeTools = Config.Bind("1. Core Modules", "IncludeHeldTools", true, "Includes held utility tools (flashlights, prybars) in conditioning.");

            // Category 2: Balance & Math Tuning
            RcRatio = Config.Bind("2. Balance & Math Tuning", "ConditioningRatio_Rc", 1.0f, new ConfigDescription("Multiplier (0.0 to 1.0).", new AcceptableValueRange<float>(0.0f, 1.0f)));
            CkBonusCap = Config.Bind("2. Balance & Math Tuning", "StashAssistBonusCap_Ck", 6, new ConfigDescription("Max bonus cap (+1 to +10).", new AcceptableValueRange<int>(1, 10)));
            MaxDeltaCap = Config.Bind("2. Balance & Math Tuning", "MaxDeltaCap", 6, new ConfigDescription("Max stat delta increase ceiling.", new AcceptableValueRange<int>(1, 10)));
            BaseStatCap = Config.Bind("2. Balance & Math Tuning", "BaseStatCap", 6, new ConfigDescription("Base stat soft-cap before diminishing returns apply.", new AcceptableValueRange<int>(1, 10)));
            s_DiminishingReturnsEnabled = Config.Bind("2. Balance & Math Tuning", "DiminishingReturnsEnabled", false, "Applies progressive falloff past threshold.");
            DiminishingThreshold = Config.Bind("2. Balance & Math Tuning", "DiminishingThreshold", 3, new ConfigDescription("Bonus threshold where diminishing returns begin.", new AcceptableValueRange<int>(1, 6)));
            DiminishingRatio = Config.Bind("2. Balance & Math Tuning", "DiminishingRatio", 0.75f, new ConfigDescription("Multiplier for bonus points exceeding threshold.", new AcceptableValueRange<float>(0.1f, 0.9f)));

            // Category 3: Roleplay Stat Suppression
            for (int i = 0; i < 15; i++)
            {
                string skillName = s_SkillDisplayNames[i];
                SkillSuppressionEntries[i] = Config.Bind("3. Roleplay Stat Suppression", $"Suppress_{skillName}", false, $"If true, {skillName} will never receive assistance from unequipped stash wardrobe.");
            }

            // Legacy entries for sealed compatibility
            s_ModEnabled = Config.Bind("Internal", "ModEnabled", true, "Internal switch.");
            s_ForceRecalculateNow = Config.Bind("Internal", "ForceRecalculateNow", false, "Internal switch.");
            s_SuppressedSkillsMask = Config.Bind("Internal", "SuppressedSkillsMask", 0UL, "Internal bitmask.");
        }

        public static void SyncAllToEngine()
        {
            try
            {
                unsafe
                {
                    if (PtrNativeConfig != null && s_ModEnabled != null)
                    {
                        PtrNativeConfig->ModEnabled = s_ModEnabled.Value ? 1 : 0;
                    }
                }

                NativeEngineStructs.UpdateConfig(
                    ShopAdvisorEnabled.Value,
                    BestInSlotTagsEnabled.Value,
                    StashAssistanceEnabled.Value,
                    s_AffectPassiveChecks.Value,
                    s_SuppressEquippedPenalties.Value,
                    IncludeTools.Value,
                    RcRatio.Value,
                    CkBonusCap.Value,
                    MaxDeltaCap.Value,
                    BaseStatCap.Value,
                    s_DiminishingReturnsEnabled.Value,
                    DiminishingThreshold.Value,
                    DiminishingRatio.Value
                );
            }
            catch (Exception ex)
            {
                WriteTrace($"[CONFIG-SYNC] NativeEngineStructs sync: {ex.Message}");
            }

            for (int i = 0; i < 15; i++)
            {
                if (SkillSuppressionEntries[i] != null)
                {
                    bool suppressed = SkillSuppressionEntries[i].Value;
                    WardrobeConditioningEngine.SetSkillSuppressed(i, suppressed);
                }
            }

            WardrobeConditioningEngine.MarkDirty("ConfigSync");
            WriteTrace("[CONFIG-SYNC] All parameters synchronized to engine.");
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            WriteTrace($"[CONFIG-CHANGED] Setting '{e.ChangedSetting.Definition.Key}' updated live.");
            SyncAllToEngine();
        }

        public static void RestoreDefaults()
        {
            Instance.Config.Clear();
            Instance.BindConfiguration();
            SyncAllToEngine();
            Instance.Config.Save();
            WriteTrace("[CONFIG-RESET] Restored all configuration values to defaults.");
        }

        // ====================================================================
        // NATIVE CONFIG INITIALIZATION (PURE UNMANAGED MEMORY, ZERO IL2CPP CALLS)
        // ====================================================================
        public static unsafe void InitNativeModConfig()
        {
            try
            {
                if (_nativeConfigPtr == IntPtr.Zero)
                {
                    int size = sizeof(NativeModConfig);
                    _nativeConfigPtr = Marshal.AllocHGlobal(size);
                    byte* pZero = (byte*)_nativeConfigPtr.ToPointer();
                    for (int i = 0; i < size; i++)
                    {
                        pZero[i] = 0;
                    }
                    PtrNativeConfig = (NativeModConfig*)_nativeConfigPtr.ToPointer();
                }

                if (PtrNativeConfig != null)
                {
                    PtrNativeConfig->ModEnabled = (s_ModEnabled == null || s_ModEnabled.Value) ? 1 : 0;
                    PtrNativeConfig->MaxItemStacks = 99;
                    PtrNativeConfig->ConditioningRatio = RcRatio != null ? RcRatio.Value : 1.0f;
                    PtrNativeConfig->SuppressPenalties = (s_SuppressEquippedPenalties == null || s_SuppressEquippedPenalties.Value) ? 1 : 0;
                    PtrNativeConfig->DiminishingReturns = (s_DiminishingReturnsEnabled != null && s_DiminishingReturnsEnabled.Value) ? 1 : 0;
                    PtrNativeConfig->AutoEquipOptimal = 1;
                    PtrNativeConfig->StashAssistBonus = 2;
                    PtrNativeConfig->StashAssistBonusCap = CkBonusCap != null ? CkBonusCap.Value : 6;
                    PtrNativeConfig->SuppressedSkillsMask = s_SuppressedSkillsMask != null ? s_SuppressedSkillsMask.Value : 0UL;
                }

                WriteTrace($"[GENESIS-NATIVE] NativeModConfig allocated in unmanaged memory at 0x{_nativeConfigPtr.ToInt64():X}.");
            }
            catch (Exception ex)
            {
                WriteTrace("NativeModConfig allocation error: " + ex.Message);
            }
        }

        // Retained as safe no-ops to preserve public method surface without allocating
        // unrooted IL2CPP strings or premature native game objects during bootstrap.
        public static void InitNativeStringPointers() { }
        public static void InitSyntheticModifier() { }
        public static void EnsureSyncBehaviour() { }

        public static void OnNativeSceneUnloaded(Scene scene) => LifecycleGuard.OnSceneUnloaded(scene);
    }
}
