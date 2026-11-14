// === FILE: ModSource/Core/LifecycleGuard.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA
// Zero-Pollution Scene & LoadingState Air-Gap Lifecycle Guard.
// Uses pure managed Stopwatch timing instead of an injected MonoBehaviour so
// ClassInjector is never triggered on Unity 6000.3+ IL2CPP.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZAUM.FELD.C4.Inventory.Management;

namespace ZeroParadesOptimalWardrobe.Core
{
    public static class LifecycleGuard
    {
        private const float DefaultStartupGraceSeconds = 7.0f;
        private static readonly Stopwatch s_StartupStopwatch = Stopwatch.StartNew();

        public static volatile bool IsEngineInLoadingState = true;
        public static volatile bool IsCheckActive = false;
        public static volatile bool HasEnteredWorldState = false;
        public static float StartupGraceTimer = DefaultStartupGraceSeconds;
        public static int SceneTransitionCount = 0;

        public static void ResetStartupGrace()
        {
            StartupGraceTimer = DefaultStartupGraceSeconds;
            s_StartupStopwatch.Restart();
        }

        public static bool IsStartupGraceActive
        {
            get
            {
                if (StartupGraceTimer <= 0f)
                {
                    return false;
                }

                float elapsed = (float)s_StartupStopwatch.Elapsed.TotalSeconds;
                if (elapsed >= StartupGraceTimer)
                {
                    StartupGraceTimer = 0f;
                    return false;
                }

                return true;
            }
        }

        public static void Tick(float deltaTime)
        {
            if (StartupGraceTimer > 0f && deltaTime > 0f)
            {
                StartupGraceTimer = Math.Max(0f, StartupGraceTimer - deltaTime);
            }
        }

        public static bool IsSaveLoadGateOpen()
        {
            if (Plugin.s_ModEnabled != null && !Plugin.s_ModEnabled.Value)
            {
                return false;
            }

            if (IsStartupGraceActive)
            {
                return false;
            }

            try
            {
                var lc = UnityEngine.Object.FindObjectOfType<ZAUM.C4.UI.Loading.LoadingUIController>();
                if (lc != null && !lc.WasCollected && lc.Pointer != IntPtr.Zero && lc.gameObject != null && lc.gameObject.activeInHierarchy)
                {
                    IsEngineInLoadingState = true;
                    return false;
                }
            }
            catch { }

            C4InventoryManager invMgr = null;
            try
            {
                invMgr = WardrobeConditioningEngine.GetInventoryManager();
                if (invMgr == null || invMgr.WasCollected || invMgr.Pointer == IntPtr.Zero)
                {
                    return false;
                }

                unsafe
                {
                    NativeC4InventoryManager* pInv = (NativeC4InventoryManager*)invMgr.Pointer;
                    if (pInv == null || pInv->IsInitialized == 0)
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            IsEngineInLoadingState = false;
            return true;
        }

        public static void OnSceneUnloaded(Scene scene)
        {
            string sName = scene.name ?? "Scene";

            IsEngineInLoadingState = true;
            IsCheckActive = false;
            if (sName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) || sName.Equals("Core", StringComparison.OrdinalIgnoreCase))
            {
                HasEnteredWorldState = false;
            }

            // Notify conditioning engine to purge transient state
            WardrobeConditioningEngine.OnSceneUnloaded();
        }

        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            string sName = scene.name ?? "Scene";
            SceneTransitionCount++;

            if (sName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                IsEngineInLoadingState = false;
                StartupGraceTimer = 0f;
            }
        }
    }

    // =========================================================================
    // HARMONY AIR-GAP PATCHES: GameStateManager Transitions
    // =========================================================================

    [HarmonyPatch]
    public static class LoadingStateLifecyclePatches
    {
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        private static Type FindZaumType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name ?? "";
                if (!asmName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.StartsWith("ZAUM", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t != null && string.Equals(t.Name, typeName, StringComparison.Ordinal))
                            return t;
                    }
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();
            try
            {
                Type gsmType = FindZaumType("GameStateManager");
                Type lsType = FindZaumType("LoadingState");

                if (gsmType != null)
                {
                    var mEnterLoad = gsmType.GetMethod("EnterLoadingState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mEnterLoad != null)
                    {
                        methods.Add(mEnterLoad);
                    }
                    var mEnterWorld = gsmType.GetMethod("EnterWorldState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mEnterWorld != null)
                    {
                        methods.Add(mEnterWorld);
                    }
                    var mEnterMenu = gsmType.GetMethod("EnterMainMenuState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mEnterMenu != null)
                    {
                        methods.Add(mEnterMenu);
                    }
                }

                if (lsType != null)
                {
                    var mEnter = lsType.GetMethod("EnterState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mEnter != null)
                    {
                        methods.Add(mEnter);
                    }
                }
            }
            catch { }
            return methods;
        }

        public static void Postfix(MethodBase __originalMethod)
        {
            if (__originalMethod == null) return;
            string name = __originalMethod.Name;
            if (name == "EnterLoadingState" || name == "EnterState")
            {
                LifecycleGuard.IsEngineInLoadingState = true;
                LifecycleGuard.IsCheckActive = false;
                LifecycleGuard.HasEnteredWorldState = false;
                WardrobeConditioningEngine.OnSceneUnloaded();
            }
            else if (name == "EnterWorldState")
            {
                LifecycleGuard.IsEngineInLoadingState = false;
                LifecycleGuard.HasEnteredWorldState = true;
                LifecycleGuard.StartupGraceTimer = 0f;
                WardrobeConditioningEngine.MarkDirty(name);
            }
            else if (name == "EnterMainMenuState")
            {
                LifecycleGuard.IsEngineInLoadingState = false;
                LifecycleGuard.IsCheckActive = false;
                LifecycleGuard.HasEnteredWorldState = false;
                LifecycleGuard.StartupGraceTimer = 0f;
                WardrobeConditioningEngine.OnSceneUnloaded();
            }
        }
    }
}
