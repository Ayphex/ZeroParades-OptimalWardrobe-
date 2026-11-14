// === FILE: ModSource/Hooks/InventoryHooks.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA
// C4InventoryManager Dirty Triggers & Non-Destructive UI Tooltip Tagging.
// Zero-DelegateSupport & Zero-ClassInjector implementation for Unity 6000.3+ IL2CPP.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using ZAUM.C4.UI.Inventory;
using ZAUM.C4.UI.Modifiers;
using ZAUM.FELD.C4.Dialogues.Model.Entities;
using ZAUM.FELD.C4.Inventory.Management;
using ZeroParadesOptimalWardrobe.Core;

namespace ZeroParadesOptimalWardrobe.Hooks
{
    // =========================================================================
    // 1. INVENTORY MUTATION PATCHES (Event-Driven Recalculation Triggers)
    // =========================================================================

    [HarmonyPatch]
    public static class InventoryMutationPatch
    {
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

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

            if (__instance is C4InventoryManager invMgr)
            {
                if (invMgr.WasCollected || invMgr.Pointer == IntPtr.Zero)
                    return;

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

            // Mark dirty ONLY; lazy evaluation handles recalculation cleanly once the save/load gate is open.
            WardrobeConditioningEngine.MarkDirty(__originalMethod != null ? __originalMethod.Name : "InventoryMutation");
        }
    }

    // =========================================================================
    // 2. INVENTORY LIFECYCLE & DESERIALIZATION HOOKS
    // =========================================================================

    [HarmonyPatch(typeof(C4InventoryManager), nameof(C4InventoryManager.LoadFromInventoryObject))]
    public static class InventoryLifecyclePatches
    {
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPostfix]
        public static void OnLoadFromInventoryObjectPostfix(C4InventoryManager __instance)
        {
            if (__instance != null && !__instance.WasCollected && __instance.Pointer != IntPtr.Zero)
            {
                // Mark dirty only; do not prematurely clear IsEngineInLoadingState or force synchronous
                // evaluation while other managers may still be mid-deserialization.
                WardrobeConditioningEngine.MarkDirty("LoadFromInventoryObject");
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
            if (candidateItem == null || candidateItem.WasCollected || candidateItem.Pointer == IntPtr.Zero)
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
        private static bool ContainsModAnnotation(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("Optimal:") ||
                   text.Contains("Store") ||
                   text.Contains("Stash:") ||
                   text.Contains("Outclassed") ||
                   text.Contains("Extraction Failed") ||
                   text.Contains("Error:");
        }

        public static void InjectBiSToDetailsUI(InventoryItemDetailsController controller, string entityIdFallback = null, IntPtr itemPtrFallback = default)
        {
            if (Plugin.s_ModEnabled != null && !Plugin.s_ModEnabled.Value) return;
            if (controller == null || controller.WasCollected || controller.Pointer == IntPtr.Zero) return;

            string entityId = entityIdFallback;
            if (string.IsNullOrEmpty(entityId))
            {
                entityId = controller.m_currentItem?.EntityID;
            }

            IntPtr itemPtr = itemPtrFallback;
            var invMgr = controller.m_inventoryManager ?? WardrobeConditioningEngine.GetInventoryManager();
            RtItemEntity rtEntity = null;
            if (!string.IsNullOrEmpty(entityId) && invMgr != null && !invMgr.WasCollected)
            {
                invMgr.TryGetEntity(entityId, out rtEntity);
                if (rtEntity != null && !rtEntity.WasCollected && rtEntity.Pointer != IntPtr.Zero && itemPtr == IntPtr.Zero)
                {
                    itemPtr = rtEntity.Pointer;
                }
            }

            if (string.IsNullOrEmpty(entityId) && itemPtr == IntPtr.Zero) return;

            string annotation = null;
            bool isStoreCandidate = false;

            if (rtEntity != null && !WardrobeConditioningEngine.IsItemOwned(rtEntity))
            {
                if (Plugin.ShopAdvisorEnabled == null || Plugin.ShopAdvisorEnabled.Value)
                {
                    var eval = ShopUpgradeAdvisor.EvaluateCandidateItem(rtEntity);
                    if (eval.IsUnowned && eval.IsWearable && !string.IsNullOrEmpty(eval.TooltipAnnotation))
                    {
                        annotation = eval.TooltipAnnotation;
                        isStoreCandidate = true;
                    }
                }
                else
                {
                    return;
                }
            }

            if (string.IsNullOrEmpty(annotation))
            {
                if (Plugin.BestInSlotTagsEnabled != null && !Plugin.BestInSlotTagsEnabled.Value)
                    return;

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

                    // UNIVERSAL TOOLTIP GUARANTEE: Fallback for wearable items
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
            // NOTE: Never pass C# lambdas cast to Il2CppSystem.Action<T> (e.g. ForEachModifier),
            // because DelegateSupport.ConvertDelegate triggers ClassInjector -> InjectorHelpers.Setup(),
            // which crashes Unity 6000.3+ IL2CPP when UnityLogListening = false.
            if (controller.m_modifiersController != null && !controller.m_modifiersController.WasCollected)
            {
                try
                {
                    bool hasBadge = false;
                    var container = controller.m_modifiersController.m_modifierEntriesContainer;
                    if (container != null)
                    {
                        int childCount = container.childCount;
                        for (int i = 0; i < childCount && !hasBadge; i++)
                        {
                            var child = container.GetChild(i);
                            if (child == null || child.gameObject == null || !child.gameObject.activeSelf)
                                continue;

                            var tmps = child.GetComponentsInChildren<TextMeshProUGUI>(true);
                            if (tmps != null)
                            {
                                foreach (var t in tmps)
                                {
                                    if (t != null && ContainsModAnnotation(t.text))
                                    {
                                        hasBadge = true;
                                        break;
                                    }
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
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance, InventoryItem inventoryItem, RtItemEntity itemEntity)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                string entityId = inventoryItem?.EntityID ?? itemEntity?.Name;
                IntPtr ptr = (itemEntity != null && !itemEntity.WasCollected) ? itemEntity.Pointer : IntPtr.Zero;
                InventoryTooltipInjector.InjectBiSToDetailsUI(__instance, entityId, ptr);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InventoryItemDetailsController), nameof(InventoryItemDetailsController.UpdateItemDetails))]
    public static class InventoryItemDetailsController_UpdateItemDetails_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero)
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
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPostfix]
        public static void Postfix(InventoryItemDetailsController __instance)
        {
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero)
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
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPrefix]
        public static void Prefix(InventoryItemDetailsController __instance, ref string description)
        {
            if (Plugin.s_ModEnabled != null && !Plugin.s_ModEnabled.Value)
                return;

            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero)
                return;

            try
            {
                InventoryItem item = __instance.m_currentItem;
                if (item == null) return;

                string entityId = item.EntityID;
                RtItemEntity rtEntity = null;
                var invMgr = __instance.m_inventoryManager ?? WardrobeConditioningEngine.GetInventoryManager();
                if (invMgr != null && !invMgr.WasCollected && !string.IsNullOrEmpty(entityId))
                {
                    invMgr.TryGetEntity(entityId, out rtEntity);
                }

                string annotation = null;

                try
                {
                    // Check for Store Candidate Item (Contextual State Deduction: Unowned if not in stash or equipped)
                    if (rtEntity != null && !WardrobeConditioningEngine.IsItemOwned(rtEntity))
                    {
                        if (Plugin.ShopAdvisorEnabled == null || Plugin.ShopAdvisorEnabled.Value)
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
                        else
                        {
                            return;
                        }
                    }

                    if (Plugin.BestInSlotTagsEnabled != null && !Plugin.BestInSlotTagsEnabled.Value)
                        return;

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

                        // UNIVERSAL TOOLTIP GUARANTEE: Fallback for wearable items
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
            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen() || __instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero)
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
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => SafeHarmonyPatcher.PrepareMethod(original);

        [HarmonyPostfix]
        public static void Postfix(ModifiersController __instance, RtItemEntity itemEntity)
        {
            if (Plugin.s_ModEnabled != null && !Plugin.s_ModEnabled.Value)
                return;

            if (Plugin.ShopAdvisorEnabled == null || !Plugin.ShopAdvisorEnabled.Value)
                return;

            if (LifecycleGuard.IsStartupGraceActive || !LifecycleGuard.IsSaveLoadGateOpen())
                return;

            if (__instance == null || __instance.WasCollected || __instance.Pointer == IntPtr.Zero ||
                itemEntity == null || itemEntity.WasCollected || itemEntity.Pointer == IntPtr.Zero)
                return;

            try
            {
                // 1. Resolve the modifier entries container (layout group root)
                Transform container = null;
                try { container = __instance.m_modifierEntriesContainer; } catch { }
                if (container == null)
                    return;

                // 2. Always clean up pooled artifacts from recycled UI cards first
                const string annotationName = "OptimalWardrobe_ShopAnnotation";
                try
                {
                    for (int i = container.childCount - 1; i >= 0; i--)
                    {
                        var child = container.GetChild(i);
                        if (child != null && child.name == annotationName)
                        {
                            child.gameObject.SetActive(false);
                            child.SetParent(null, false);
                            UnityEngine.Object.Destroy(child.gameObject);
                        }
                    }
                }
                catch { }

                // 3. Check if candidate item is unowned (store item)
                if (!WardrobeConditioningEngine.IsItemOwned(itemEntity))
                {
                    var eval = ShopUpgradeAdvisor.EvaluateCandidateItem(itemEntity);

                    if (eval.IsUnowned && eval.IsWearable && !string.IsNullOrEmpty(eval.TooltipAnnotation))
                    {
                        // 4. Spawn a fresh annotation element into the layout group
                        var annotationObj = new GameObject(annotationName);
                        var tmp = annotationObj.AddComponent<TextMeshProUGUI>();
                        tmp.text = eval.TooltipAnnotation;
                        tmp.fontSize = 16f;
                        annotationObj.transform.SetParent(container, false);
                    }
                }
            }
            catch { }
        }
    }
}
