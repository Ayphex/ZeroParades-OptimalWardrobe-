// === FILE: ModSource/Hooks/InventoryHooks.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA
// C4InventoryManager Dirty Triggers & Non-Destructive UI Tooltip Tagging.
// Inviolable Law 2: Zero persistent mutation to RtItemEntity or heap modifiers.
// Inviolable Law 4: Non-destructive visual tagging in SetItemDescription.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime;
using TMPro;
using ZAUM.C4.UI.Inventory;
using ZAUM.C4.UI.Modifiers;
using ZAUM.FELD.C4.Dialogues.Model.Entities;
using ZAUM.FELD.C4.Inventory.Management;
using ZAUM.FELD.Tools.Console;
using ZeroParadesOptimalWardrobe.Core;

namespace ZeroParadesOptimalWardrobe.Hooks
{
    // =========================================================================
    // 1. INVENTORY MUTATION PATCHES (Event-Driven Recalculation Triggers)
    // =========================================================================

    [HarmonyPatch]
    public static class InventoryMutationPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();
            try
            {
                var invMgrType = typeof(C4InventoryManager);
                var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "AddItem", "RemoveItem", "DropItem", "EquipItem", "UnequipItem", "AddAndEquip", "Equip", "Unequip"
                };

                foreach (var m in invMgrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.IsGenericMethod || m.IsAbstract) continue;

                    bool hasByRef = false;
                    foreach (var p in m.GetParameters())
                    {
                        if (p.ParameterType.IsByRef)
                        {
                            hasByRef = true;
                            break;
                        }
                    }
                    if (hasByRef) continue;

                    if (targetNames.Contains(m.Name) || m.Name.Equals("RefreshGearModifiersForAllEquippedGear", StringComparison.OrdinalIgnoreCase))
                    {
                        methods.Add(m);
                    }
                }
            }
            catch { }
            return methods;
        }

        public static void Postfix(MethodBase __originalMethod, object __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || LifecycleGuard.IsEngineInLoadingState || __instance == null)
                return;

            if (__instance is C4InventoryManager invMgr && invMgr.Pointer != IntPtr.Zero)
            {
                unsafe
                {
                    NativeC4InventoryManager* pInv = (NativeC4InventoryManager*)invMgr.Pointer;
                    if (pInv == null || pInv->IsInitialized == 0)
                    {
                        WardrobeConditioningEngine.ClearItemStatsCache();
                        return; // Gate held: do not evaluate during deserialization
                    }
                }
            }

            // Mark dirty ONLY; do NOT call Evaluate() synchronously on every intermediate equip/unequip sub-step.
            // Lazy evaluation on tooltip/check query and the 0.25s debounce tick in LifecycleGuard handle recalculation cleanly.
            WardrobeConditioningEngine.MarkDirty(__originalMethod != null ? __originalMethod.Name : "InventoryMutation");
        }
    }

    // =========================================================================
    // 2. INVENTORY LIFECYCLE & DESERIALIZATION CASCADE HOOKS
    // =========================================================================

    [HarmonyPatch]
    public static class InventoryLifecyclePatches
    {
        [HarmonyPatch(typeof(C4InventoryManager), "add_OnInventoryLoaded")]
        [HarmonyPostfix]
        public static void OnAddOnInventoryLoadedPostfix(C4InventoryManager __instance)
        {
            if (__instance != null && __instance.Pointer != IntPtr.Zero)
            {
                unsafe
                {
                    NativeC4InventoryManager* pInv = (NativeC4InventoryManager*)__instance.Pointer;
                    if (pInv != null && pInv->IsInitialized != 0)
                    {
                        LifecycleGuard.IsEngineInLoadingState = false;
                        WardrobeConditioningEngine.MarkDirty("OnInventoryLoaded");
                        WardrobeConditioningEngine.Evaluate();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(C4InventoryManager), nameof(C4InventoryManager.LoadFromInventoryObject))]
        [HarmonyPostfix]
        public static void OnLoadFromInventoryObjectPostfix(C4InventoryManager __instance)
        {
            if (__instance != null && __instance.Pointer != IntPtr.Zero)
            {
                LifecycleGuard.IsEngineInLoadingState = false;
                WardrobeConditioningEngine.MarkDirty("LoadFromInventoryObject");
                WardrobeConditioningEngine.Evaluate();
            }
        }
    }

    // =========================================================================
    // 3. SHOP UPGRADE ADVISOR (Differential Wardrobe Evaluation Engine)
    // =========================================================================

    public static class ShopUpgradeAdvisor
    {
        public struct UpgradeEvaluation
        {
            public bool IsUnowned;
            public bool IsWearable;
            public int BestDelta;
            public string BestCanonicalSkill;
            public string TooltipAnnotation;
        }

        public static UpgradeEvaluation EvaluateCandidateItem(RtItemEntity candidateItem)
        {
            var result = new UpgradeEvaluation();
            if (candidateItem == null || candidateItem.Pointer == IntPtr.Zero)
                return result;

            // Contextual State Deduction: An item is "unowned" if not in stash or equipped
            bool isOwned = WardrobeConditioningEngine.IsItemOwned(candidateItem);
            result.IsUnowned = !isOwned;
            if (isOwned)
            {
                return result;
            }
            int rawSlot = (int)candidateItem.GearSlot;
            int slot = WardrobeConditioningEngine.ResolveEffectiveSlot(candidateItem, rawSlot);
            if (slot <= 0)
            {
                result.IsWearable = false;
                return result; // Non-wearable item category (Consumable, Tool, Quest/Other)
            }

            result.IsWearable = true;
            ItemStats shopStats = WardrobeConditioningEngine.GetOrCreateItemStats(candidateItem);

            int maxDelta = 0;
            string bestSkill = null;
            var upgrades = new List<string>();

            // Math:
            // Stat_owned_max(s, k) = max(Stat_equipped(s, k), Stat_stash_best(s, k))
            // Delta_upgrade(k) = max(0, Stat_shop(s, k) - Stat_owned_max(s, k))
            for (int k = 0; k < 15; k++)
            {
                if (WardrobeConditioningEngine.IsSkillSuppressed(k))
                    continue;

                int shopVal = shopStats.GetModifier(k);
                if (shopVal <= 0) continue;

                int ownedMax = WardrobeConditioningEngine.GetMaxOwnedStat(slot, k);
                int delta = Math.Max(0, shopVal - ownedMax);

                if (delta > 0)
                {
                    string canonicalSkill = WardrobeConditioningEngine.CanonicalSkillDisplayNames[k];
                    upgrades.Add($"+{delta} {canonicalSkill}");
                    if (delta > maxDelta)
                    {
                        maxDelta = delta;
                        bestSkill = canonicalSkill;
                    }
                }
            }

            result.BestDelta = maxDelta;
            result.BestCanonicalSkill = bestSkill;

            if (upgrades.Count > 0)
            {
                string upgradeStr = string.Join(", ", upgrades);
                result.TooltipAnnotation = $"<color=#00FFAA><b>[Store Upgrade: {upgradeStr} (Beats Owned)]</b></color>";
            }
            else
            {
                result.TooltipAnnotation = "<color=#888888>[Store: Outclassed by Owned Gear]</color>";
            }

            return result;
        }
    }

    // =========================================================================
    // 4. NON-DESTRUCTIVE UI TOOLTIP PRESENTATION PATCHES
    // =========================================================================

    public static class InventoryTooltipInjector
    {
        public static void InjectBiSToDetailsUI(InventoryItemDetailsController controller, string entityIdFallback = null, IntPtr itemPtrFallback = default)
        {
            if (controller == null || controller.Pointer == IntPtr.Zero) return;

            string entityId = entityIdFallback;
            if (string.IsNullOrEmpty(entityId))
            {
                entityId = controller.m_currentItem?.EntityID;
            }

            IntPtr itemPtr = itemPtrFallback;
            var invMgr = controller.m_inventoryManager ?? WardrobeConditioningEngine.GetInventoryManager();
            RtItemEntity rtEntity = null;
            if (!string.IsNullOrEmpty(entityId) && invMgr != null)
            {
                invMgr.TryGetEntity(entityId, out rtEntity);
                if (rtEntity != null && rtEntity.Pointer != IntPtr.Zero && itemPtr == IntPtr.Zero)
                {
                    itemPtr = rtEntity.Pointer;
                }
            }

            if (string.IsNullOrEmpty(entityId) && itemPtr == IntPtr.Zero) return;

            string annotation = null;
            bool isStoreCandidate = false;

            if (rtEntity != null && !WardrobeConditioningEngine.IsItemOwned(rtEntity))
            {
                var eval = ShopUpgradeAdvisor.EvaluateCandidateItem(rtEntity);
                if (eval.IsUnowned && eval.IsWearable && !string.IsNullOrEmpty(eval.TooltipAnnotation))
                {
                    annotation = eval.TooltipAnnotation;
                    isStoreCandidate = true;
                }
            }

            if (string.IsNullOrEmpty(annotation))
            {
                try
                {
                    annotation = WardrobeConditioningEngine.GetTooltipAnnotation(entityId, itemPtr);
                    if (string.IsNullOrEmpty(annotation) && rtEntity != null)
                    {
                        if (!string.IsNullOrEmpty(rtEntity.ShortName))
                            annotation = WardrobeConditioningEngine.GetTooltipAnnotation(rtEntity.ShortName, itemPtr);
                        if (string.IsNullOrEmpty(annotation) && !string.IsNullOrEmpty(rtEntity.Name))
                            annotation = WardrobeConditioningEngine.GetTooltipAnnotation(rtEntity.Name, itemPtr);
                    }

                    // UNIVERSAL TOOLTIP GUARANTEE: Fallback for wearable items (e.g. Splash Boots, Neck pseudo-slot 99)
                    if (string.IsNullOrEmpty(annotation) && rtEntity != null && WardrobeConditioningEngine.ResolveEffectiveSlot(rtEntity) > 0)
                    {
                        annotation = WardrobeConditioningEngine.GetOutclassedDiagnosticAnnotation(rtEntity);
                    }
                }
                catch (Exception ex)
                {
                    annotation = $"<color=red>[Error: {ex.GetType().Name} - {ex.Message}]</color>";
                    Plugin.Logger?.LogError($"[TOOLTIP-ERROR] InjectBiSToDetailsUI: {ex}");
                }
            }

            if (string.IsNullOrEmpty(annotation)) return;

            // 1. TextMeshPro description update
            if (controller.m_itemDescriptionText != null)
            {
                var tmpTarget = controller.m_itemDescriptionText.Target;
                if (tmpTarget != null)
                {
                    string curText = tmpTarget.text ?? "";
                    // Strip any stale [Optimal: ...], [Store: ...], [Stash: ...], [Outclassed...], [Extraction Failed:...], or [Error:...
                    int existingIdx = curText.IndexOf("\n\n<color=#", StringComparison.Ordinal);
                    if (existingIdx < 0)
                    {
                        existingIdx = curText.IndexOf("\n\n<color=red>", StringComparison.Ordinal);
                    }
                    if (existingIdx >= 0 && (curText.IndexOf("[Optimal:", existingIdx, StringComparison.Ordinal) >= 0 ||
                                             curText.IndexOf("[Store", existingIdx, StringComparison.Ordinal) >= 0 ||
                                             curText.IndexOf("[Stash:", existingIdx, StringComparison.Ordinal) >= 0 ||
                                             curText.IndexOf("[Outclassed", existingIdx, StringComparison.Ordinal) >= 0 ||
                                             curText.IndexOf("[Extraction Failed:", existingIdx, StringComparison.Ordinal) >= 0 ||
                                             curText.IndexOf("[Error:", existingIdx, StringComparison.Ordinal) >= 0))
                    {
                        curText = curText.Substring(0, existingIdx);
                    }

                    if (!string.IsNullOrEmpty(annotation) && 
                        !curText.Contains("[Optimal:") && 
                        !curText.Contains("[Store") && 
                        !curText.Contains("[Stash:") && 
                        !curText.Contains("[Outclassed") && 
                        !curText.Contains("[Extraction Failed:") && 
                        !curText.Contains("[Error:"))
                    {
                        string updated = curText + "\n\n" + annotation;
                        tmpTarget.text = updated;
                        tmpTarget.SetAllDirty();
                    }
                    else if (string.IsNullOrEmpty(annotation))
                    {
                        tmpTarget.text = curText;
                        tmpTarget.SetAllDirty();
                    }
                }
            }

            // 2. ModifiersController badge injection
            if (controller.m_modifiersController != null)
            {
                try
                {
                    bool hasBadge = false;
                    controller.m_modifiersController.ForEachModifier((Il2CppSystem.Action<ModifierEntryUI>)((entry) =>
                    {
                        if (entry != null && entry.m_description != null && 
                            (entry.m_description.Contains("Optimal:") || 
                             entry.m_description.Contains("Store") || 
                             entry.m_description.Contains("Stash:") || 
                             entry.m_description.Contains("Outclassed") ||
                             entry.m_description.Contains("Extraction Failed") ||
                             entry.m_description.Contains("Error:")))
                        {
                            hasBadge = true;
                        }
                    }));

                    if (!hasBadge && controller.m_modifiersController.m_modifierEntriesContainer != null)
                    {
                        var tmps = controller.m_modifiersController.m_modifierEntriesContainer.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                        if (tmps != null)
                        {
                            foreach (var t in tmps)
                            {
                                if (t != null && t.text != null && 
                                    (t.text.Contains("Optimal:") || 
                                     t.text.Contains("Store") || 
                                     t.text.Contains("Stash:") || 
                                     t.text.Contains("Outclassed") ||
                                     t.text.Contains("Extraction Failed") ||
                                     t.text.Contains("Error:")))
                                {
                                    hasBadge = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!hasBadge)
                    {
                        var entry = controller.m_modifiersController.AddDescriptionModifier(annotation, false, "");
                        if (entry != null)
                        {
                            Color badgeColor = isStoreCandidate
                                ? (annotation.Contains("Beats Owned") ? new Color(0f, 1f, 0.667f, 1f) : new Color(0.533f, 0.533f, 0.533f, 1f))
                                : ((annotation.Contains("[Extraction Failed:") || annotation.Contains("[Error:"))
                                    ? Color.red
                                    : ((annotation.Contains("[Stash:") || annotation.Contains("[Outclassed")) ? new Color(0.533f, 0.533f, 0.533f, 1f) : new Color(1f, 0.843f, 0f, 1f)));

                            if (entry.m_titleText != null && entry.m_titleText.Target != null)
                            {
                                entry.m_titleText.Target.color = badgeColor;
                            }
                            if (entry.m_descriptionText != null && entry.m_descriptionText.Target != null)
                            {
                                entry.m_descriptionText.Target.color = badgeColor;
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(InventoryItemDetailsController), nameof(InventoryItemDetailsController.RefreshTexts))]
    public static class InventoryItemDetailsController_RefreshTexts_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance, InventoryItem inventoryItem, RtItemEntity itemEntity)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                string entityId = inventoryItem?.EntityID ?? itemEntity?.Name;
                IntPtr ptr = itemEntity != null ? itemEntity.Pointer : IntPtr.Zero;
                InventoryTooltipInjector.InjectBiSToDetailsUI(__instance, entityId, ptr);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InventoryItemDetailsController), nameof(InventoryItemDetailsController.UpdateItemDetails))]
    public static class InventoryItemDetailsController_UpdateItemDetails_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                InventoryTooltipInjector.InjectBiSToDetailsUI(__instance);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InventoryItemDetailsController), nameof(InventoryItemDetailsController.SetupItemDetails))]
    public static class InventoryItemDetailsController_SetupItemDetails_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                InventoryTooltipInjector.InjectBiSToDetailsUI(__instance);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InventoryItemDetailsController), nameof(InventoryItemDetailsController.SetItemDescription))]
    public static class ItemDetailsDescriptionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InventoryItemDetailsController __instance, ref string description)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                InventoryItem item = __instance.m_currentItem;
                if (item == null) return;

                string entityId = item.EntityID;
                RtItemEntity rtEntity = null;
                var invMgr = __instance.m_inventoryManager ?? WardrobeConditioningEngine.GetInventoryManager();
                if (invMgr != null && !string.IsNullOrEmpty(entityId))
                {
                    invMgr.TryGetEntity(entityId, out rtEntity);
                }

                string annotation = null;

                try
                {
                    // Check for Store Candidate Item (Contextual State Deduction: Unowned if not in stash or equipped)
                    if (rtEntity != null && !WardrobeConditioningEngine.IsItemOwned(rtEntity))
                    {
                        var eval = ShopUpgradeAdvisor.EvaluateCandidateItem(rtEntity);
                        if (eval.IsUnowned && eval.IsWearable && !string.IsNullOrEmpty(eval.TooltipAnnotation))
                        {
                            if (description != null && !description.Contains("[Store"))
                            {
                                description = description + "\n\n" + eval.TooltipAnnotation;
                            }
                            return;
                        }
                    }

                    // Owned item BiS annotation
                    if (!string.IsNullOrEmpty(entityId))
                    {
                        annotation = WardrobeConditioningEngine.GetTooltipAnnotation(entityId, rtEntity != null ? rtEntity.Pointer : default);
                        if (string.IsNullOrEmpty(annotation) && rtEntity != null)
                        {
                            if (!string.IsNullOrEmpty(rtEntity.ShortName))
                                annotation = WardrobeConditioningEngine.GetTooltipAnnotation(rtEntity.ShortName, rtEntity.Pointer);
                            if (string.IsNullOrEmpty(annotation) && !string.IsNullOrEmpty(rtEntity.Name))
                                annotation = WardrobeConditioningEngine.GetTooltipAnnotation(rtEntity.Name, rtEntity.Pointer);
                        }

                        // UNIVERSAL TOOLTIP GUARANTEE: Fallback for wearable items (e.g. Splash Boots, Neck pseudo-slot 99)
                        if (string.IsNullOrEmpty(annotation) && rtEntity != null && WardrobeConditioningEngine.ResolveEffectiveSlot(rtEntity) > 0)
                        {
                            annotation = WardrobeConditioningEngine.GetOutclassedDiagnosticAnnotation(rtEntity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    annotation = $"<color=red>[Error: {ex.GetType().Name} - {ex.Message}]</color>";
                    Plugin.Logger?.LogError($"[TOOLTIP-ERROR] BiS Evaluation failed for {entityId}: {ex}");
                }

                if (!string.IsNullOrEmpty(annotation))
                {
                    if (description != null && 
                        !description.Contains("[Optimal:") && 
                        !description.Contains("[Store") && 
                        !description.Contains("[Stash:") && 
                        !description.Contains("[Outclassed") && 
                        !description.Contains("[Error:") && 
                        !description.Contains("[Extraction Failed:"))
                    {
                        description = description + "\n\n" + annotation;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"Error in ItemDetailsDescriptionPatch.Prefix: {ex}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                InventoryTooltipInjector.InjectBiSToDetailsUI(__instance);
            }
            catch { }
        }
    }

[HarmonyPatch(typeof(ModifiersController), nameof(ModifiersController.SetModifiers), new Type[] { typeof(RtItemEntity), typeof(ZAUM.C4.GameTools.GameToolsManager) })]
    public static class ModifiersController_SetModifiers_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ModifiersController __instance, RtItemEntity itemEntity)
        {
            if (__instance == null || __instance.Pointer == IntPtr.Zero || itemEntity == null || itemEntity.Pointer == IntPtr.Zero)
                return;

            // Check if Shop Advisor is enabled in configuration
            if (!Plugin.ShopAdvisorEnabled.Value)
                return;

            bool isOwned = WardrobeConditioningEngine.IsItemOwned(itemEntity);

            try
            {
                // 1. Check if candidate item is unowned (store item)
                if (!isOwned)
                {
                    var eval = ShopUpgradeAdvisor.EvaluateCandidateItem(itemEntity);

                    if (eval.IsUnowned && eval.IsWearable && !string.IsNullOrEmpty(eval.TooltipAnnotation))
                    {
                        // 2. Resolve the modifier entries container (layout group root)
                        UnityEngine.Transform container = null;
                        try { container = __instance.m_modifierEntriesContainer; } catch { }
                        if (container == null)
                            return;

                        // 3. Clean up pooled artifacts from recycled UI cards
                        const string annotationName = "OptimalWardrobe_ShopAnnotation";
                        try
                        {
                            for (int i = container.childCount - 1; i >= 0; i--)
                            {
                                var child = container.GetChild(i);
                                if (child != null && child.name == annotationName)
                                {
                                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                                }
                            }
                        }
                        catch { }

                        // 4. Spawn a fresh annotation element into the layout group
                        var annotationObj = new UnityEngine.GameObject(annotationName);
                        var tmp = annotationObj.AddComponent<TMPro.TextMeshProUGUI>();
                        tmp.text = eval.TooltipAnnotation;
                        tmp.fontSize = 16f;
                        annotationObj.transform.SetParent(container, false);
                    }
                }
            }
            catch { }
        }
    }

// =========================================================================
// 3. FELD CONSOLE MUTE PATCHES (Suppress Benign Native Engine Spam)
// =========================================================================

[HarmonyPatch(typeof(FeldConsole), "LogWarning", new Type[] { typeof(Il2CppSystem.Object) })]
public static class FeldConsole_LogWarning_Patch
{
    public static bool Prefix(Il2CppSystem.Object message)
    {
        if (message == null) return true;
        string msg = message.ToString();
        if (msg.Contains("equipped in the inventory") || 
            msg.Contains("Calling modifiers getter for") || 
            msg.Contains("Wasn't able to get modifier value from"))
        {
            return false; // Suppress native ZA/UM noise from polluting Player.log
        }
        return true;
    }
}

[HarmonyPatch(typeof(FeldConsole), "LogError", new Type[] { typeof(Il2CppSystem.Object) })]
public static class FeldConsole_LogError_Patch
{
    public static bool Prefix(Il2CppSystem.Object message)
    {
        if (message == null) return true;
        string msg = message.ToString();
        if (msg.Contains("Calling modifiers getter for") || 
            msg.Contains("Wasn't able to get modifier value from"))
        {
            return false; // Suppress benign missing skill info errors
        }
        return true;
    }
}

}
