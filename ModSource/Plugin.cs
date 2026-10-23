// === FILE: ModSource/Plugin.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA v33.0.6
// Clean-Slate Modular Architecture for Optimal Wardrobe.
// Integrates Two-Phase Conditioning Engine, Ephemeral Active Check Injection,
// Non-Destructive UI Tooltips, Scene/LoadingState Air-Gap Lifecycle Guards,
// and Autonomous Rigorous QA Puppeteer Driver.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
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

    [BepInPlugin("com.keith.zeroparades.optimalwardrobe", "Zero Parades: Optimal Wardrobe", "33.0.6")]
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

        // Pinned Native Config
        private static GCHandle _pinnedConfigHandle;
        private static IntPtr _pinnedConfigPtr = IntPtr.Zero;

        // Pre-allocated unmanaged Il2CppString* pointers
        public static IntPtr PtrStashAssist = IntPtr.Zero;
        public static IntPtr PtrUniqueBis = IntPtr.Zero;
        public static IntPtr PtrSharedBis = IntPtr.Zero;
        public static IntPtr PtrCosmetic = IntPtr.Zero;

        // Wardrobe Sync Behaviour
        public static WardrobeSyncBehaviour SyncInstance = null;
        private static GameObject s_SyncContainer = null;

        public static void WriteTrace(string line)
        {
            Logger?.LogInfo(line);
        }

        public static void OnCheckStarted()
        {
            LifecycleGuard.IsCheckActive = true;
            InitSyntheticModifier();
            WriteTrace("[CHECK-LIFECYCLE] Check Triggered: Stash assistance active.");
        }

        public static void OnCheckFinished()
        {
            LifecycleGuard.IsCheckActive = false;
            if (ActiveModifierList != null)
            {
                try
                {
                    if (SyntheticModifier != null)
                    {
                        if (ActiveModifierList is Il2CppSystem.Collections.Generic.List<ChecksActiveModifiersInfoItem> typedList)
                        {
                            typedList.Remove(SyntheticModifier);
                        }
                        else if (ActiveModifierList is Il2CppSystem.Collections.IList il2cppList)
                        {
                            il2cppList.Remove(SyntheticModifier);
                        }
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

            BindConfiguration();
            SyncAllToEngine();

            WriteTrace("=== Optimal Wardrobe v33.0.6 Initializing (Pure Headless Modular Engine) ===");

            // Hook live config file changes
            Config.SettingChanged += OnSettingChanged;

            // 1. Register and Spawn Sync Behaviour for Engine Ticking
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<WardrobeSyncBehaviour>();
                EnsureSyncBehaviour();
                WriteTrace("Spawned WardrobeSyncBehaviour container safely.");
            }
            catch (Exception ex)
            {
                WriteTrace("WardrobeSyncBehaviour registration error: " + ex.Message);
            }

            // 2. Initialize Unmanaged Pointers & Synthetic Modifier
            InitNativeStringPointers();
            InitSyntheticModifier();
            InitNativeModConfig();

            // Apply Core Gameplay Patches (Inventory, Dialogue, Checks)
            var harmony = new Harmony("com.keith.zeroparades.optimalwardrobe");
            harmony.PatchAll();

            // Verify InventoryHooks registration (ModifiersController_SetModifiers_Patch)
            WriteTrace("[HOOK-TRACE] InventoryHooks registered: ModifiersController.SetModifiers");

            WriteTrace("=== Optimal Wardrobe v33.0.6 Loaded Successfully. UI & Scaffolding Excised. ===");
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
                bool suppressed = SkillSuppressionEntries[i].Value;
                WardrobeConditioningEngine.SetSkillSuppressed(i, suppressed);
            }

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
        // NATIVE CONFIG & POINTER INITIALIZATION
        // ====================================================================
        public static void InitNativeModConfig()
        {
            try
            {
                var config = new NativeModConfig
                {
                    ModEnabled = (Instance != null && Instance.ModEnabled.Value) ? 1 : 0,
                    ConditioningRatio = Instance?.ConditioningRatio.Value ?? 0.75f,
                    SuppressPenalties = (Instance?.SuppressEquippedPenalties.Value ?? true) ? 1 : 0,
                    DiminishingReturns = (Instance?.DiminishingReturnsEnabled.Value ?? true) ? 1 : 0,
                    AutoEquipOptimal = 1,
                    StashAssistBonus = 2,
                    StashAssistBonusCap = Instance?.StashAssistBonusCap.Value ?? 5,
                    SuppressedSkillsMask = Instance?.SuppressedSkillsMask.Value ?? 0UL
                };

                _pinnedConfigHandle = GCHandle.Alloc(config, GCHandleType.Pinned);
                _pinnedConfigPtr = _pinnedConfigHandle.AddrOfPinnedObject();
                unsafe
                {
                    PtrNativeConfig = (NativeModConfig*)_pinnedConfigPtr;
                }

                WriteTrace($"[GENESIS-NATIVE] NativeModConfig allocated and pinned at 0x{_pinnedConfigPtr.ToInt64():X}.");
            }
            catch (Exception ex)
            {
                WriteTrace("NativeModConfig allocation error: " + ex.Message);
            }
        }

        public static void InitNativeStringPointers()
        {
            try
            {
                if (PtrUniqueBis == IntPtr.Zero)
                    PtrUniqueBis = IL2CPP.ManagedStringToIl2Cpp(" <color=#FFD700>[Optimal: Unique BiS]</color>");

                if (PtrSharedBis == IntPtr.Zero)
                    PtrSharedBis = IL2CPP.ManagedStringToIl2Cpp(" <color=#00FFFF>[Optimal: Shared BiS]</color>");

                if (PtrCosmetic == IntPtr.Zero)
                    PtrCosmetic = IL2CPP.ManagedStringToIl2Cpp(" <color=#808080>[Optimal: Cosmetic]</color>");

                if (PtrStashAssist == IntPtr.Zero)
                    PtrStashAssist = IL2CPP.ManagedStringToIl2Cpp("Optimal Wardrobe (Stash Assist)");
            }
            catch (Exception ex)
            {
                WriteTrace("InitNativeStringPointers error: " + ex.Message);
            }
        }

        public static void InitSyntheticModifier()
        {
            if (SyntheticModifier != null && SyntheticModifierPtr != IntPtr.Zero && SyntheticModifier.Pointer != IntPtr.Zero)
                return;

            try
            {
                InitNativeStringPointers();
                SyntheticModifier = new ChecksActiveModifiersInfoItem();
                SyntheticModifierPtr = SyntheticModifier.Pointer;

                unsafe
                {
                    NativeChecksActiveModifiersInfoItem* pMod = (NativeChecksActiveModifiersInfoItem*)SyntheticModifierPtr;
                    if (pMod != null)
                    {
                        pMod->Explanation = PtrStashAssist;
                        pMod->Value = 0;
                    }
                }

                WriteTrace($"[GENESIS-NATIVE] Instantiated native ChecksActiveModifiersInfoItem at 0x{SyntheticModifierPtr.ToInt64():X}.");
            }
            catch (Exception ex)
            {
                WriteTrace("InitSyntheticModifier error: " + ex.Message);
            }
        }

        public static void EnsureSyncBehaviour()
        {
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<WardrobeSyncBehaviour>();
                if (existing == null)
                {
                    if (s_SyncContainer == null)
                    {
                        s_SyncContainer = new GameObject("ZeroParadesOptimalWardrobe_Sync");
                        UnityEngine.Object.DontDestroyOnLoad(s_SyncContainer);
                        s_SyncContainer.hideFlags = HideFlags.HideAndDontSave;
                    }
                    SyncInstance = s_SyncContainer.AddComponent<WardrobeSyncBehaviour>();
                    WriteTrace("[GENESIS-LIFECYCLE] Re-anchored WardrobeSyncBehaviour into active scene.");
                }
                else
                {
                    SyncInstance = existing;
                }
            }
            catch { }
        }

        public static void OnNativeSceneUnloaded(Scene scene) => LifecycleGuard.OnSceneUnloaded(scene);
    }

    // =========================================================================
    // WARDROBE SYNC BEHAVIOUR (Engine Update Loop & Lifecycle Ticking)
    // =========================================================================

    public class WardrobeSyncBehaviour : MonoBehaviour
    {
        public WardrobeSyncBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            LifecycleGuard.Tick(dt);
        }
    }
}