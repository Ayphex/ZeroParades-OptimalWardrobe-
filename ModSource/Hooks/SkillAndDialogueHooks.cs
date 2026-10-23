// === FILE: ModSource/Hooks/SkillAndDialogueHooks.cs ===
// Directive: MASTER ARCHITECTURAL DIRECTIVE: OPTIMAL WARDROBE v6.2.0
// Ephemeral Active Check Injection, Post-Roll History Hover & Zero-Allocation Presenters.
// Inviolable Law 3: Ephemeral active check injection + post-roll historical deltas in memory.
// Safe IL2CPP Hooking Law: Never use named or ref/out parameters that violate IL2CPP ABI.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using ZAUM.C4.UI.Dialogues;
using ZAUM.FELD.C4.Dialogues.Management;
using ZAUM.FELD.C4.Dialogues.Model.Cards;
using ZAUM.FELD.C4.Dialogues.Model.Entities;
using ZAUM.FELD.C4.Skills.Management;
using ZeroParadesOptimalWardrobe.Core;

namespace ZeroParadesOptimalWardrobe.Hooks
{
    public static class SkillAndDialogueHooks
    {
        // Ephemeral in-memory session store for rolled check card deltas (Zero Save-File Pollution)
        public static readonly Dictionary<IntPtr, int> HistoricalRollDeltas = new Dictionary<IntPtr, int>();

        public static void ClearHistoricalRollDeltas()
        {
            HistoricalRollDeltas.Clear();
        }

        private static readonly string[] PreallocatedPlusStrings = InitPlusStrings();

        private static string[] InitPlusStrings()
        {
            var arr = new string[21];
            for (int i = 0; i <= 20; i++)
            {
                arr[i] = $"+{i}";
            }
            return arr;
        }

        public static string GetFormattedDelta(int delta)
        {
            if (delta >= 0 && delta <= 20) return PreallocatedPlusStrings[delta];
            return $"+{delta}";
        }
    }

    // =========================================================================
    // 1. RTWHITECHECKCARD & RTREDCHECKCARD TRYGETACTIVEMODIFIERS PATCHES
    // =========================================================================

    [HarmonyPatch(typeof(RtWhiteCheckCard), nameof(RtWhiteCheckCard.TryGetActiveModifiers))]
    public static class RtWhiteCheckCard_TryGetActiveModifiers_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            RtWhiteCheckCard __instance,
            ref bool __result,
            ref Il2CppSystem.Collections.Generic.List<ChecksActiveModifiersInfoItem> activeModifiers)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                string skillId = null;
                try { skillId = __instance.SkillID ?? __instance.m_skillID; } catch { }
                if (string.IsNullOrEmpty(skillId))
                {
                    IntPtr pStr = NativePointerHelpers.ReadPtrSafe(__instance.Pointer, BlueprintOffsets.RtCheckCard.Offset_SkillID);
                    if (NativePointerHelpers.IsValidIl2CppString(pStr))
                    {
                        try { skillId = IL2CPP.Il2CppStringToManaged(pStr); } catch { }
                    }
                }

                if (string.IsNullOrEmpty(skillId)) return;
                string normSkillId = WardrobeConditioningEngine.NormalizeSkillId(skillId);

                int finalDelta = 0;
                if (SkillAndDialogueHooks.HistoricalRollDeltas.TryGetValue(__instance.Pointer, out int histDelta) && histDelta > 0)
                {
                    finalDelta = histDelta;
                }
                else
                {
                    finalDelta = WardrobeConditioningEngine.IsSkillSuppressed(normSkillId) ? 0 : WardrobeConditioningEngine.GetFinalSkillDelta(normSkillId);
                }

                ChecksActiveModifiersInfoItem existingItem = null;
                if (activeModifiers != null)
                {
                    for (int i = 0; i < activeModifiers.Count; i++)
                    {
                        var mod = activeModifiers[i];
                        if (mod != null && mod.Pointer != IntPtr.Zero)
                        {
                            string exp = null;
                            try { exp = mod.Explanation; } catch { }
                            if (exp != null && exp.Contains("Optimal Wardrobe"))
                            {
                                existingItem = mod;
                                break;
                            }
                        }
                    }
                }

                if (finalDelta > 0)
                {
                    if (existingItem != null)
                    {
                        existingItem.Value = finalDelta;
                    }
                    else
                    {
                        if (activeModifiers == null)
                        {
                            activeModifiers = new Il2CppSystem.Collections.Generic.List<ChecksActiveModifiersInfoItem>();
                        }

                        var item = new ChecksActiveModifiersInfoItem();
                        item.Explanation = "Optimal Wardrobe (Stash Assist)";
                        item.Value = finalDelta;

                        activeModifiers.Add(item);
                    }
                    Plugin.ActiveModifierList = activeModifiers;
                    __result = true;
                }
                else if (existingItem != null && activeModifiers != null)
                {
                    activeModifiers.Remove(existingItem);
                }
            }
            catch { }
        }
    }

    // =========================================================================
    // 2. NATIVE ACTIVE CHECK MODIFIER UI INJECTION
    // =========================================================================

    public static class SkillCheckUIInjector
    {
        public static void InjectModifier(
            SkillCheckModifiersController controller,
            RtWhiteCheckCard checkCard,
            RtSkillEntity skillEntity,
            ref int modsTotal)
        {
            if (controller == null || controller.Pointer == IntPtr.Zero) return;

            string skillId = null;
            if (skillEntity != null)
            {
                try { skillId = skillEntity.ShortName; } catch { }
                if (string.IsNullOrEmpty(skillId))
                {
                    try { skillId = skillEntity.Name; } catch { }
                }
            }
            if (string.IsNullOrEmpty(skillId) && checkCard != null)
            {
                try { skillId = checkCard.SkillID ?? checkCard.m_skillID; } catch { }
            }
            if (string.IsNullOrEmpty(skillId))
            {
                var modal = controller.GetComponentInParent<SkillCheckModal>();
                if (modal != null && modal.m_cachedSkillEntity != null)
                {
                    try { skillId = modal.m_cachedSkillEntity.ShortName ?? modal.m_cachedSkillEntity.Name; } catch { }
                }
            }
            if (string.IsNullOrEmpty(skillId)) return;

            string normSkillId = WardrobeConditioningEngine.NormalizeSkillId(skillId);

            int finalDelta = 0;
            if (checkCard != null && checkCard.Pointer != IntPtr.Zero && SkillAndDialogueHooks.HistoricalRollDeltas.TryGetValue(checkCard.Pointer, out int hDelta) && hDelta > 0)
            {
                finalDelta = hDelta;
            }
            else
            {
                finalDelta = WardrobeConditioningEngine.IsSkillSuppressed(normSkillId) ? 0 : WardrobeConditioningEngine.GetFinalSkillDelta(normSkillId);
            }

            string deltaStr = SkillAndDialogueHooks.GetFormattedDelta(finalDelta);

            if (finalDelta <= 0)
            {
                // Clean up any previously injected Optimal Wardrobe entry
                if (controller.m_activeModifierEntries != null)
                {
                    for (int i = controller.m_activeModifierEntries.Count - 1; i >= 0; i--)
                    {
                        var entry = controller.m_activeModifierEntries[i];
                        if (entry != null && entry.gameObject != null && (entry.name.Contains("OptimalWardrobe") || (entry.m_modifierDescription != null && entry.m_modifierDescription.Target != null && entry.m_modifierDescription.Target.text != null && entry.m_modifierDescription.Target.text.Contains("Optimal Wardrobe"))))
                        {
                            controller.m_activeModifierEntries.RemoveAt(i);
                            UnityEngine.Object.Destroy(entry.gameObject);
                        }
                    }
                }
                if (controller.m_modsContentLayout != null)
                {
                    for (int i = controller.m_modsContentLayout.childCount - 1; i >= 0; i--)
                    {
                        var child = controller.m_modsContentLayout.GetChild(i);
                        if (child != null && child.gameObject != null && child.name.Contains("OptimalWardrobe"))
                        {
                            UnityEngine.Object.Destroy(child.gameObject);
                        }
                    }
                    var rt = controller.m_modsContentLayout.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    }
                }
                return;
            }

            // Check if already present in m_activeModifierEntries or m_modsContentLayout
            bool alreadyPresent = false;
            if (controller.m_activeModifierEntries != null)
            {
                for (int i = 0; i < controller.m_activeModifierEntries.Count; i++)
                {
                    var entry = controller.m_activeModifierEntries[i];
                    if (entry != null && entry.m_modifierDescription != null && entry.m_modifierDescription.Target != null)
                    {
                        string desc = entry.m_modifierDescription.Target.text;
                        if (desc != null && desc.Contains("Optimal Wardrobe"))
                        {
                            alreadyPresent = true;
                            if (entry.m_modifierValue != null && entry.m_modifierValue.Target != null)
                            {
                                entry.m_modifierValue.Target.text = deltaStr;
                            }
                            entry.m_modifierDescription.Target.text = "Optimal Wardrobe (Stash Assist)";
                            break;
                        }
                    }
                }
            }

            if (!alreadyPresent && controller.m_modsContentLayout != null)
            {
                for (int i = 0; i < controller.m_modsContentLayout.childCount; i++)
                {
                    var child = controller.m_modsContentLayout.GetChild(i);
                    if (child != null && child.name.Contains("OptimalWardrobe"))
                    {
                        alreadyPresent = true;
                        var tmps = child.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                        if (tmps != null)
                        {
                            foreach (var t in tmps)
                            {
                                if (t == null) continue;
                                if (t.name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                                    t.text = deltaStr;
                                else
                                    t.text = "Optimal Wardrobe (Stash Assist)";
                            }
                        }
                        break;
                    }
                }
            }

            if (alreadyPresent)
            {
                if (controller.m_modsContentLayout != null)
                {
                    var rt = controller.m_modsContentLayout.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    }
                }
                return;
            }

            // Fallback: If CreateCheckCardModifierEntries did NOT already create the visual entry:
            try
            {
                controller.AddModifier("Optimal Wardrobe (Stash Assist)", finalDelta);
            }
            catch { }

            bool hasVisualEntry = false;
            if (controller.m_activeModifierEntries != null)
            {
                for (int i = 0; i < controller.m_activeModifierEntries.Count; i++)
                {
                    var e = controller.m_activeModifierEntries[i];
                    if (e != null && (e.name.Contains("OptimalWardrobe") || (e.m_modifierDescription != null && e.m_modifierDescription.Target != null && e.m_modifierDescription.Target.text != null && e.m_modifierDescription.Target.text.Contains("Optimal Wardrobe"))))
                    {
                        hasVisualEntry = true;
                        break;
                    }
                }
            }

            if (!hasVisualEntry && controller.m_modsContentLayout != null)
            {
                SkillCheckModifierEntry newEntry = null;
                if (controller.m_modEntryPrefab != null)
                {
                    newEntry = UnityEngine.Object.Instantiate(controller.m_modEntryPrefab, controller.m_modsContentLayout);
                }
                else if (controller.m_activeModifierEntries != null && controller.m_activeModifierEntries.Count > 0 && controller.m_activeModifierEntries[0] != null)
                {
                    newEntry = UnityEngine.Object.Instantiate(controller.m_activeModifierEntries[0], controller.m_modsContentLayout);
                }

                if (newEntry != null)
                {
                    newEntry.name = "SkillCheckModifierEntry_OptimalWardrobe";
                    try
                    {
                        newEntry.SetModifier(finalDelta, "Optimal Wardrobe (Stash Assist)");
                    }
                    catch { }
                    if (newEntry.m_modifierValue != null && newEntry.m_modifierValue.Target != null)
                    {
                        newEntry.m_modifierValue.Target.text = deltaStr;
                    }
                    if (newEntry.m_modifierDescription != null && newEntry.m_modifierDescription.Target != null)
                    {
                        newEntry.m_modifierDescription.Target.text = "Optimal Wardrobe (Stash Assist)";
                    }
                    var tmps = newEntry.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                    if (tmps != null)
                    {
                        foreach (var t in tmps)
                        {
                            if (t == null) continue;
                            if (t.name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                                t.text = deltaStr;
                            else
                                t.text = "Optimal Wardrobe (Stash Assist)";
                        }
                    }
                    newEntry.gameObject.SetActive(true);
                    if (controller.m_activeModifierEntries != null)
                    {
                        controller.m_activeModifierEntries.Add(newEntry);
                    }
                }
            }

            if (controller.m_modsContentLayout != null)
            {
                controller.m_modsContentLayout.gameObject.SetActive(true);
                var rt = controller.m_modsContentLayout.GetComponent<RectTransform>();
                if (rt != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
            }

            modsTotal += finalDelta;
        }
    }

    [HarmonyPatch(typeof(SkillCheckModifiersController), nameof(SkillCheckModifiersController.Show))]
    public static class SkillCheckModifiersController_Show_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            SkillCheckModifiersController __instance,
            RtWhiteCheckCard checkCard,
            RtSkillEntity skillEntity,
            Color facultyColor,
            bool isLocked,
            ref int modsTotal)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                SkillCheckUIInjector.InjectModifier(__instance, checkCard, skillEntity, ref modsTotal);
            }
            catch { }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(
            Exception __exception,
            SkillCheckModifiersController __instance,
            RtWhiteCheckCard checkCard,
            RtSkillEntity skillEntity,
            Color facultyColor,
            bool isLocked,
            ref int modsTotal)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return null;

            if (__exception != null)
            {
                try
                {
                    SkillCheckUIInjector.InjectModifier(__instance, checkCard, skillEntity, ref modsTotal);
                }
                catch { }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(SkillCheckModifiersController), nameof(SkillCheckModifiersController.TrySetModifiers))]
    public static class SkillCheckModifiersController_TrySetModifiers_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            SkillCheckModifiersController __instance,
            RtSkillEntity skillEntity,
            RtWhiteCheckCard checkCard,
            bool isLocked,
            ref int totalValue)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                SkillCheckUIInjector.InjectModifier(__instance, checkCard, skillEntity, ref totalValue);
            }
            catch { }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(
            Exception __exception,
            SkillCheckModifiersController __instance,
            RtSkillEntity skillEntity,
            RtWhiteCheckCard checkCard,
            bool isLocked,
            ref int totalValue)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return null;

            if (__exception != null)
            {
                try
                {
                    SkillCheckUIInjector.InjectModifier(__instance, checkCard, skillEntity, ref totalValue);
                }
                catch { }
            }
            return null;
        }
    }

    // =========================================================================
    // 3. SKILL VALUE EVALUATION INJECTION (C4SkillManager.ApplyModifierOnSkillValue)
    // =========================================================================

    [HarmonyPatch(typeof(C4SkillManager), nameof(C4SkillManager.ApplyModifierOnSkillValue))]
    public static class SkillManager_ApplyModifierOnSkillValue_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int skillValue, RtSkillEntity skillEntity, int skillInfoArray, bool checkPenalties, ref int __result)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || skillEntity == null)
                return;

            if (LifecycleGuard.IsCheckActive)
                return;

            if (Plugin.Instance == null || !Plugin.Instance.AffectPassiveChecks.Value)
                return;

            string skillId = skillEntity.ShortName ?? skillEntity.Name;
            string normSkillId = WardrobeConditioningEngine.NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(normSkillId) || WardrobeConditioningEngine.IsSkillSuppressed(normSkillId)) return;
            int delta = WardrobeConditioningEngine.GetFinalSkillDelta(normSkillId);
            if (delta <= 0) return;

            __result += delta;
        }
    }

    // =========================================================================
    // 4. GUARANTEED POST-ROLL UNLINKING IN FINALIZER (Inviolable Law 3)
    // =========================================================================

    [HarmonyPatch(typeof(RtWhiteCheckCard), nameof(RtWhiteCheckCard.PerformDiceRoll))]
    public static class WhiteCheckCard_PerformDiceRoll_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(RtWhiteCheckCard __instance)
        {
            LifecycleGuard.IsCheckActive = true;
            Plugin.OnCheckStarted();

            if (__instance != null && __instance.Pointer != IntPtr.Zero)
            {
                string skillId = null;
                try { skillId = __instance.SkillID ?? __instance.m_skillID; } catch { }
                if (string.IsNullOrEmpty(skillId))
                {
                    IntPtr pStr = NativePointerHelpers.ReadPtrSafe(__instance.Pointer, BlueprintOffsets.RtCheckCard.Offset_SkillID);
                    if (NativePointerHelpers.IsValidIl2CppString(pStr))
                    {
                        try { skillId = IL2CPP.Il2CppStringToManaged(pStr); } catch { }
                    }
                }
                string normSkillId = WardrobeConditioningEngine.NormalizeSkillId(skillId);
                int rollDelta = (!string.IsNullOrEmpty(normSkillId) && !WardrobeConditioningEngine.IsSkillSuppressed(normSkillId))
                    ? WardrobeConditioningEngine.GetFinalSkillDelta(normSkillId)
                    : 0;

                if (rollDelta > 0)
                {
                    SkillAndDialogueHooks.HistoricalRollDeltas[__instance.Pointer] = rollDelta;
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(RtWhiteCheckCard __instance)
        {
            LifecycleGuard.IsCheckActive = false;
            Plugin.OnCheckFinished();
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            LifecycleGuard.IsCheckActive = false;
            Plugin.OnCheckFinished();
            return __exception;
        }
    }

    // =========================================================================
    // 5. REDUNDANT AIR-GAP: C4DialogueManager Result Hooks
    // =========================================================================

    [HarmonyPatch(typeof(C4DialogueManager), nameof(C4DialogueManager.FireRedWhiteCheckResult))]
    public static class DialogueManager_FireRedWhiteCheckResult_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            Plugin.OnCheckFinished();
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.OnCheckFinished();
        }
    }

    [HarmonyPatch(typeof(C4DialogueManager), nameof(C4DialogueManager.ResetActiveCheckVariables))]
    public static class DialogueManager_ResetActiveCheckVariables_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.OnCheckFinished();
        }
    }

}
