// === FILE: ModSource/Core/WardrobeConditioningEngine.cs ===
// Directive: WARDROBE-02-CLEAN-SLATE-ENGINE-AND-QA
// Two-Phase Conditioning Math Engine & O(1) BiS Lookup Table.
// Inviolable Law 1: Phase A (Exclusive BiS Delta) + Phase B (Potential-Gated Conditioning).
// Inviolable Law 2: Zero heap writes to RtItemEntity or persistent GearModifiers.

using System;
using System.Collections.Generic;
using UnityEngine;
using ZAUM.FELD.C4.Dialogues.Model.Entities;
using ZAUM.FELD.C4.Inventory.Management;
using ZAUM.FELD.C4.Skills.Management;
using ZeroParadesOptimalWardrobe.Hooks;

namespace ZeroParadesOptimalWardrobe.Core
{
    public enum BiSClass : byte
    {
        None = 0,
        CosmeticOnly = 1,
        OutclassedEquipped = 2,
        SharedBiS = 3,
        UniqueBiS = 4,
        EquippedSharedBiS = 5,
        EquippedBiS = 6,
        OutclassedStash = 7
    }

    public static class WardrobeConditioningEngine
    {
        private static readonly object SyncLock = new object();

        // Active Blittable Shadow State
        public static WardrobeShadowState ShadowState = new WardrobeShadowState();

        // O(1) BiS Lookup Tables
        private static readonly Dictionary<IntPtr, BiSClass> _bisClassByPointer = new Dictionary<IntPtr, BiSClass>();
        private static readonly Dictionary<string, BiSClass> _bisClassByEntityId = new Dictionary<string, BiSClass>(StringComparer.OrdinalIgnoreCase);

        // Precomputed Final Deltas per Skill ID
        private static readonly Dictionary<string, int> _precomputedDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _itemBonusSummaryByEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<IntPtr, string> _itemBonusSummaryByPointer = new Dictionary<IntPtr, string>();

        // Pre-cached Formatted Tooltip Strings
        private static readonly Dictionary<string, string> _cachedTooltipByEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<IntPtr, string> _cachedTooltipByPointer = new Dictionary<IntPtr, string>();
        private static readonly Dictionary<IntPtr, List<string>> _itemSkillSummariesByPointer = new Dictionary<IntPtr, List<string>>();

        // Dynamic ItemStats Cache & Differential Engine Buffers (Dynamic m_gearSlot Partitioning)
        private static readonly Dictionary<IntPtr, ItemStats> _itemStatsCache = new Dictionary<IntPtr, ItemStats>();
        private static readonly HashSet<IntPtr> _ownedItemPointers = new HashSet<IntPtr>();
        private static readonly HashSet<string> _ownedItemEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, int[]> _maxOwnedStatBySlotAndSkill = new Dictionary<int, int[]>();

        // Cached Unity Managers
        private static C4InventoryManager _cachedInvMgr;
        private static C4SkillManager _cachedSkillMgr;

        private static bool _isDirty = true;
        private static bool _isEvaluating = false;

        public static bool IsDirty => _isDirty;

        // Internal Engine IDs (indices 0..14) - must match C4SkillManager internal ordering
        public static readonly string[] ZeroParadesSkills = new string[]
        {
            "coordination",   // 0
            "vigour",         // 1
            "muscle",         // 2
            "senses",         // 3
            "presence",       // 4
            "motivation",     // 5
            "affect",         // 6
            "nerve",          // 7
            "wits",           // 8
            "awareness",      // 9
            "entanglement",   // 10
            "focus",          // 11
            "inspiration",    // 12
            "recall",         // 13
            "inference"       // 14
        };

        // Official player-facing Operant Dossier display names (indices 0..14)
        public static readonly string[] CanonicalSkillDisplayNames = new string[]
        {
            "Coordination", // 0
            "Doppelgäng",   // 1 (umlaut ä)
            "Instincts",    // 2
            "Sensors",      // 3
            "Shadowplay",   // 4
            "Blueprints",   // 5
            "Cold Read",    // 6 (space)
            "Nerve",        // 7
            "Personalism",  // 8
            "Statehood",    // 9
            "Entanglement", // 10
            "Grey Matter",  // 11 (space)
            "Poetics",      // 12
            "Records",      // 13
            "Technoflex"    // 14
        };

        public static readonly string[] KnownSkills = ZeroParadesSkills;
        public static readonly string[] AllSkills = ZeroParadesSkills;

        #region Skill Normalization Subsystem
        public static string NormalizeSkillId(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return string.Empty;
            string lower = rawId.Trim().ToLowerInvariant();
            lower = lower.Replace('/', '_').Replace('\\', '_').Replace('-', '_');

            if (lower.StartsWith("skill_")) lower = lower.Substring(6);
            if (lower.StartsWith("persona_")) lower = lower.Substring(8);
            if (lower.StartsWith("c4_")) lower = lower.Substring(3);

switch (lower)
            {
                // Canonical to Internal Mapping
                case "coordination": return "coordination";
                case "doppelgang": case "doppelgäng": case "doppelgaeng": return "vigour";
                case "instincts": return "muscle";
                case "sensors": case "senses": return "senses";
                case "shadowplay": case "shadow_play": return "presence";
                case "blueprints": return "motivation";
                case "coldread": case "cold_read": case "cold read": return "affect";
                case "nerve": return "nerve";
                case "personalism": return "wits";
                case "statehood": return "awareness";
                case "entanglement": return "entanglement";
                case "greymatter": case "grey_matter": case "grey matter": case "graymatter": case "gray_matter": case "gray matter": return "focus";
                case "poetics": return "inspiration";
                case "records": return "recall";
                case "technoflex": case "techno_flex": return "inference";
                
                // Universal Pass-Through for Internal IDs
                case "focus": return "focus";
                case "recall": return "recall";
                case "inference": return "inference";
                case "presence": return "presence";
                case "affect": return "affect";
                case "wits": return "wits";
                case "awareness": return "awareness";
                case "inspiration": return "inspiration";
                case "muscle": return "muscle";
                case "vigour": return "vigour";
                case "motivation": return "motivation";
            }

            // Fallback Substring Matching
            if (lower.Contains("coordination")) return "coordination";
            if (lower.Contains("doppelgang") || lower.Contains("doppelgäng") || lower.Contains("doppelgaeng") || lower.Contains("vigour")) return "vigour";
            if (lower.Contains("instinct") || lower.Contains("muscle")) return "muscle";
            if (lower.Contains("sensor") || lower.Contains("senses")) return "senses";
            if (lower.Contains("shadowplay") || lower.Contains("shadow_play") || lower.Contains("presence")) return "presence";
            if (lower.Contains("blueprint") || lower.Contains("motivation")) return "motivation";
            if (lower.Contains("coldread") || lower.Contains("cold_read") || lower.Contains("affect")) return "affect";
            if (lower.Contains("nerve")) return "nerve";
            if (lower.Contains("personalism") || lower.Contains("wits")) return "wits";
            if (lower.Contains("statehood") || lower.Contains("awareness")) return "awareness";
            if (lower.Contains("entanglement")) return "entanglement";
            if (lower.Contains("greymatter") || lower.Contains("grey_matter") || lower.Contains("graymatter") || lower.Contains("focus")) return "focus";
            if (lower.Contains("poetic") || lower.Contains("inspiration")) return "inspiration";
            if (lower.Contains("record") || lower.Contains("recall")) return "recall";
            if (lower.Contains("technoflex") || lower.Contains("techno_flex") || lower.Contains("inference")) return "inference";

            return lower;
        }

        public static string NormalizeToEngineKey(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            string key = CleanSkillKey(id);
            return key switch
            {
                "coordination" or "hand_eye_coordination" => "coordination",
                "doppelgaeng" or "doppelgang" or "doppelgäng" or "mimicry" => "doppelgang",
                "instinct" or "instincts" or "reflex" => "instincts",
                "perception" or "senses" or "sensor" or "sensors" => "sensors",
                "presence" or "shadow_play" or "shadowplay" => "presence",
                "blueprint" or "blueprints" or "structure" => "blueprints",
                "cold_read" or "coldread" or "scrutiny" => "coldread",
                "composure" or "nerve" or "volition" => "nerve",
                "affect" or "personalism" or "rapport" => "personalism",
                "authority" or "statehood" => "statehood",
                "entanglement" or "shivers" => "entanglement",
                "cognition" or "gray_matter" or "graymatter" or "grey_matter" or "greymatter" => "greymatter",
                "concept" or "poetic" or "poetics" => "poetics",
                "archive" or "record" or "records" => "records",
                "inference" or "techno_flex" or "technoflex" => "inference",
                _ => key
            };
        }

        private static string CleanSkillKey(string raw)
        {
            string s = raw.Trim().ToLowerInvariant()
                          .Replace("/", "_")
                          .Replace("\\", "_")
                          .Replace("-", "_")
                          .Replace(" ", "");

            if (s.StartsWith("skill_")) s = s.Substring(6);
            if (s.StartsWith("persona_")) s = s.Substring(8);
            if (s.StartsWith("c4_")) s = s.Substring(3);
            return s;
        }
        #endregion

        public static bool TryExtractModifier(
            string id, 
            int rawValue, 
            string description, 
            string sourcePropertyPath, 
            string sourceEntityId, 
            out int skillIndex, 
            out int value)
        {
            skillIndex = -1;
            value = 0;

            // 1. Resolve skill from all available metadata fields in priority order
            string norm = NormalizeSkillId(id);
            if (string.IsNullOrEmpty(norm)) norm = NormalizeSkillId(sourcePropertyPath);
            if (string.IsNullOrEmpty(norm)) norm = NormalizeSkillId(description);
            if (string.IsNullOrEmpty(norm)) norm = NormalizeSkillId(sourceEntityId);

            if (!string.IsNullOrEmpty(norm))
            {
                skillIndex = GetSkillIndex(norm);
            }

            if (skillIndex < 0 || skillIndex >= 15)
            {
                // Fallback: check combined string
                string combined = $"{id} {sourcePropertyPath} {description} {sourceEntityId}".ToLowerInvariant();
                norm = NormalizeSkillId(combined);
                if (!string.IsNullOrEmpty(norm))
                {
                    skillIndex = GetSkillIndex(norm);
                }
            }

            if (skillIndex < 0 || skillIndex >= 15)
            {
                return false;
            }

            // 2. Resolve integer value
            if (rawValue != 0)
            {
                value = rawValue;
                return true;
            }

            // Fallback: extract value from description (e.g. "+1 Personalism", "-1 Nerve")
            if (!string.IsNullOrEmpty(description))
            {
                var m = System.Text.RegularExpressions.Regex.Match(description, @"([+-]?\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int parsedVal) && parsedVal != 0)
                {
                    value = parsedVal;
                    return true;
                }
                if (description.Contains("+"))
                {
                    value = 1;
                    return true;
                }
                if (description.Contains("-"))
                {
                    value = -1;
                    return true;
                }
            }

            return false;
        }

        public static string GetCanonicalSkillName(string rawId)
        {
            int idx = GetSkillIndex(rawId);
            return (idx >= 0 && idx < CanonicalSkillDisplayNames.Length)
                ? CanonicalSkillDisplayNames[idx]
                : string.Empty;
        }

        public static string GetCanonicalName(string internalId)
        {
            if (string.IsNullOrEmpty(internalId)) return string.Empty;
            switch (internalId.ToLowerInvariant())
            {
                case "coordination": return "Coordination";
                case "vigour": return "Doppelgäng";
                case "muscle": return "Instincts";
                case "senses": return "Sensors";
                case "presence": return "Shadowplay";
                case "motivation": return "Blueprints";
                case "affect": return "Cold Read";
                case "nerve": return "Nerve";
                case "wits": return "Personalism";
                case "awareness": return "Statehood";
                case "entanglement": return "Entanglement";
                case "focus": return "Grey Matter";
                case "inspiration": return "Poetics";
                case "recall": return "Records";
                case "inference": return "Technoflex";
                default:
                    return char.ToUpper(internalId[0]) + internalId.Substring(1);
            }
        }

        private static readonly Dictionary<string, int> _skillToIndex = InitSkillIndices();

        private static Dictionary<string, int> InitSkillIndices()
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ZeroParadesSkills.Length; i++)
            {
                dict[ZeroParadesSkills[i]] = i;
            }
            return dict;
        }

        public static int GetSkillIndex(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return -1;
            string norm = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(norm)) return -1;
            return _skillToIndex.TryGetValue(norm, out int idx) ? idx : -1;
        }

        public static C4InventoryManager GetInventoryManager()
        {
            if (_cachedInvMgr == null || _cachedInvMgr.Pointer == IntPtr.Zero || _cachedInvMgr.WasCollected)
            {
                _cachedInvMgr = UnityEngine.Object.FindObjectOfType<C4InventoryManager>();
            }
            return _cachedInvMgr;
        }

        public static C4SkillManager GetSkillManager()
        {
            if (_cachedSkillMgr == null || _cachedSkillMgr.Pointer == IntPtr.Zero || _cachedSkillMgr.WasCollected)
            {
                _cachedSkillMgr = UnityEngine.Object.FindObjectOfType<C4SkillManager>();
            }
            return _cachedSkillMgr;
        }

        public static unsafe bool IsSkillSuppressed(string skillId)
        {
            int idx = GetSkillIndex(skillId);
            if (idx < 0 || idx >= 64) return false;
            NativeModConfig* pCfg = Plugin.PtrNativeConfig;
            if (pCfg == null) return false;
            return (pCfg->SuppressedSkillsMask & (1UL << idx)) != 0;
        }

        public static unsafe bool IsSkillSuppressed(int skillIndex)
        {
            if (skillIndex < 0 || skillIndex >= 64) return false;
            NativeModConfig* pCfg = Plugin.PtrNativeConfig;
            if (pCfg == null) return false;
            return (pCfg->SuppressedSkillsMask & (1UL << skillIndex)) != 0;
        }

        public static unsafe void SetSkillSuppressed(int skillIndex, bool suppressed)
        {
            if (skillIndex < 0 || skillIndex >= 64) return;
            NativeModConfig* pCfg = Plugin.PtrNativeConfig;
            if (pCfg != null)
            {
                ulong bit = 1UL << skillIndex;
                if (suppressed)
                {
                    pCfg->SuppressedSkillsMask |= bit;
                }
                else
                {
                    pCfg->SuppressedSkillsMask &= ~bit;
                }
                if (Plugin.Instance != null && Plugin.Instance.SuppressedSkillsMask != null)
                {
                    Plugin.Instance.SuppressedSkillsMask.Value = pCfg->SuppressedSkillsMask;
                }
            }
            string skName = (skillIndex >= 0 && skillIndex < AllSkills.Length) ? AllSkills[skillIndex] : skillIndex.ToString();
            MarkDirty($"SkillSuppressed_{skName}_{suppressed}");
        }

        public static void MarkDirty(string reason = null)
        {
            _isDirty = true;
        }

        public static void ClearItemStatsCache()
        {
            lock (SyncLock)
            {
                _itemStatsCache.Clear();
            }
        }

        public static bool IsItemOwned(RtItemEntity item)
        {
            if (item == null || item.Pointer == IntPtr.Zero) return false;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }
            lock (SyncLock)
            {
                if (_ownedItemPointers.Contains(item.Pointer))
                    return true;
                if (!string.IsNullOrEmpty(item.Name) && _ownedItemEntityIds.Contains(item.Name))
                    return true;
                if (!string.IsNullOrEmpty(item.ShortName) && _ownedItemEntityIds.Contains(item.ShortName))
                    return true;
                return false;
            }
        }

        public static int GetMaxOwnedStat(int slot, int skillIndex)
        {
            if (skillIndex < 0 || skillIndex >= 15) return 0;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }
            lock (SyncLock)
            {
                if (_maxOwnedStatBySlotAndSkill.TryGetValue(slot, out var stats) && stats != null && skillIndex < stats.Length)
                {
                    return stats[skillIndex];
                }
                return 0;
            }
        }

        public static string GenerateModifierDiagnosticDump(RtItemEntity rtEntity)
        {
            if (rtEntity == null || rtEntity.Pointer == IntPtr.Zero) return "rtEntity=null";
            try
            {
                var coll = rtEntity.GearModifiersCollection ?? rtEntity.m_gearModifiersCollection;
                if (coll == null || coll.Pointer == IntPtr.Zero) return "coll=null";

                var parts = new List<string>();

                // Check SkillModifiers (SkillFeatureModifier)
                try
                {
                    var sm = coll.SkillModifiers;
                    if (sm != null && sm.Count > 0)
                    {
                        int maxCheck = Math.Min(3, sm.Count);
                        for (int i = 0; i < maxCheck; i++)
                        {
                            var m = sm[i];
                            if (m != null)
                            {
                                parts.Add($"SkillMod[{i}]: Type={m.Type}, ID='{m.ID}', Val={m.Value}, Desc='{m.Description}', Path='{m.SourcePropertyPath}', SrcId='{m.SourceEntityId}'");
                            }
                        }
                    }
                }
                catch (Exception ex) { parts.Add($"SkillModErr={ex.Message}"); }

                // Check PersonaModifiers (PropertyFeatureModifier)
                try
                {
                    var pm = coll.PersonaModifiers;
                    if (pm != null && pm.Count > 0)
                    {
                        int maxCheck = Math.Min(3, pm.Count);
                        for (int i = 0; i < maxCheck; i++)
                        {
                            var m = pm[i];
                            if (m != null)
                            {
                                parts.Add($"PersonaMod[{i}]: ID='{m.ID}', Val={m.Value}, Desc='{m.Description}', Path='{m.SourcePropertyPath}', SrcId='{m.SourceEntityId}'");
                            }
                        }
                    }
                }
                catch (Exception ex) { parts.Add($"PersonaModErr={ex.Message}"); }

                // Check PressureModifiers (LimitFeatureModifier)
                try
                {
                    var lm = coll.PressureModifiers;
                    if (lm != null && lm.Count > 0)
                    {
                        int maxCheck = Math.Min(3, lm.Count);
                        for (int i = 0; i < maxCheck; i++)
                        {
                            var m = lm[i];
                            if (m != null)
                            {
                                parts.Add($"PressureMod[{i}]: Type={m.Type}, ID='{m.ID}', Val={m.Value}, Desc='{m.Description}', Path='{m.SourcePropertyPath}', SrcId='{m.SourceEntityId}'");
                            }
                        }
                    }
                }
                catch (Exception ex) { parts.Add($"PressureModErr={ex.Message}"); }

                if (parts.Count == 0)
                {
                    parts.Add($"CollType={coll.GetType().Name}, RawCount={coll.Count}, HasSkill={coll.HasSkillModifiers}, HasPersona={coll.HasPersonaModifiers}, HasPressure={coll.HasPressureModifiers}, HasMods={coll.HasModifiers}");
                }

                return string.Join("; ", parts);
            }
            catch (Exception ex)
            {
                return $"DumpException={ex.GetType().Name}: {ex.Message}";
            }
        }

        public static string GetExtractionFailureAnnotation(RtItemEntity rtEntity)
        {
            if (rtEntity == null || rtEntity.Pointer == IntPtr.Zero) return null;
            try
            {
                var coll = rtEntity.GearModifiersCollection ?? rtEntity.m_gearModifiersCollection;
                if (coll == null || coll.Pointer == IntPtr.Zero) return null;

                int collCount = 0;
                try { collCount = coll.Count; } catch { }

                int listCount = 0;
                try { if (coll.SkillModifiers != null) listCount += coll.SkillModifiers.Count; } catch { }
                try { if (coll.PersonaModifiers != null) listCount += coll.PersonaModifiers.Count; } catch { }
                try { if (coll.PressureModifiers != null) listCount += coll.PressureModifiers.Count; } catch { }

                int totalFound = Math.Max(collCount, listCount);
                if (totalFound > 0)
                {
                    ItemStats itmStats = GetOrCreateItemStats(rtEntity);
                    bool hasAnyExtracted = false;
                    for (int k = 0; k < 15; k++)
                    {
                        if (itmStats.GetModifier(k) != 0)
                        {
                            hasAnyExtracted = true;
                            break;
                        }
                    }
                    if (!hasAnyExtracted)
                    {
                        string dump = GenerateModifierDiagnosticDump(rtEntity);
                        return $"<color=red>[Extraction Failed: Found {totalFound} mods, but read 0. Fields: {dump}]</color>";
                    }
                }
            }
            catch { }
            return null;
        }

        public static string GetOutclassedDiagnosticAnnotation(RtItemEntity rtEntity)
        {
            if (rtEntity == null || rtEntity.Pointer == IntPtr.Zero) return "<color=#888888>[Outclassed]</color>";

            // DIRECTIVE 2: Failsafe Diagnostic Dump check
            string failAnnotation = GetExtractionFailureAnnotation(rtEntity);
            if (!string.IsNullOrEmpty(failAnnotation))
            {
                return failAnnotation;
            }

            int slot = ResolveEffectiveSlot(rtEntity);
            ItemStats itmStats = GetOrCreateItemStats(rtEntity);
            string diag = null;
            lock (SyncLock)
            {
                if (_maxOwnedStatBySlotAndSkill.TryGetValue(slot, out var maxArr) && maxArr != null)
                {
                    for (int k = 0; k < 15; k++)
                    {
                        if (IsSkillSuppressed(k)) continue;
                        int val = itmStats.GetModifier(k);
                        if (val > 0)
                        {
                            string skillKey = GetCanonicalName(ZeroParadesSkills[k]);
                            diag = $"Max {skillKey}: {maxArr[k]}";
                            break;
                        }
                    }
                }
            }
            string diagSuffix = !string.IsNullOrEmpty(diag) ? $" | {diag}" : "";
            string slotTag = slot > 0 ? $" (Slot: {slot}{diagSuffix})" : "";
            return $"<color=#888888>[Outclassed]{slotTag}</color>";
        }

        public const int PseudoNeckSlot = 7;

        public static bool IsNeckItem(RtItemEntity item)
        {
            if (item == null) return false;
            try
            {
                if ((int)item.GearSlot == (int)ZAUM.FELD.C4.Dialogues.Model.Entities.GearSlot.Neck) return true;
                return IsNeckString(item.Name) || IsNeckString(item.ShortName) || IsNeckString(item.FullName);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNeckString(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            string lower = str.ToLowerInvariant();
            if (lower.Contains("neck") || 
                lower.Contains("scarf") || 
                lower.Contains("bowtie") || 
                lower.Contains("bow tie") || 
                lower.Contains("cravat") || 
                lower.Contains("choker") || 
                lower.Contains("ascot") || 
                lower.Contains("collar") || 
                lower.Contains("necklace") || 
                lower.Contains("pendant") || 
                lower.Contains("necktie"))
            {
                return true;
            }
            if (lower == "tie" || lower.StartsWith("tie ") || lower.EndsWith(" tie") || lower.Contains(" tie ") || lower.Contains("-tie"))
            {
                return true;
            }
            return false;
        }

        public static int ResolveEffectiveSlot(RtItemEntity item, int rawSlot = -1)
        {
            if (item == null) return rawSlot > 0 ? rawSlot : 0;
            try
            {
                int slot = rawSlot >= 0 ? rawSlot : (int)item.GearSlot;
                if (slot == (int)ZAUM.FELD.C4.Dialogues.Model.Entities.GearSlot.Neck || IsNeckItem(item))
                {
                    return (int)ZAUM.FELD.C4.Dialogues.Model.Entities.GearSlot.Neck; // Native Slot 7
                }
                return slot;
            }
            catch
            {
                return rawSlot > 0 ? rawSlot : 0;
            }
        }

        public static unsafe ItemStats GetOrCreateItemStats(RtItemEntity item)
        {
            if (item == null || item.Pointer == IntPtr.Zero)
                return default;

            IntPtr ptr = item.Pointer;
            lock (SyncLock)
            {
                if (_itemStatsCache.TryGetValue(ptr, out var cached))
                    return cached;
            }

            ItemStats stats = default;
            stats.IsValid = 1;
            int rawSlot = (int)item.GearSlot;
            int effectiveSlot = ResolveEffectiveSlot(item, rawSlot);
            stats.GearSlot = effectiveSlot;

            try
            {
                // DIRECTIVE 1: Read item stats dynamically from RtItemEntity.GearModifiersCollection
                // Parse SkillModifiers, PersonaModifiers, and PressureModifiers WITHOUT guarding by boolean flags
                var coll = item.GearModifiersCollection ?? item.m_gearModifiersCollection;
                if (coll != null && coll.Pointer != IntPtr.Zero)
                {
                    // 1. SkillModifiers (SkillFeatureModifier)
                    try
                    {
                        var smList = coll.SkillModifiers;
                        if (smList != null)
                        {
                            int count = smList.Count;
                            for (int i = 0; i < count; i++)
                            {
                                var mod = smList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    short current = stats.GetModifier(skIdx);
                                    stats.SetModifier(skIdx, (short)(current + val));
                                }
                            }
                        }
                    }
                    catch { }

                    // 2. PersonaModifiers (PropertyFeatureModifier)
                    try
                    {
                        var pmList = coll.PersonaModifiers;
                        if (pmList != null)
                        {
                            int count = pmList.Count;
                            for (int i = 0; i < count; i++)
                            {
                                var mod = pmList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    short current = stats.GetModifier(skIdx);
                                    stats.SetModifier(skIdx, (short)(current + val));
                                }
                            }
                        }
                    }
                    catch { }

                    // 3. PressureModifiers (LimitFeatureModifier)
                    try
                    {
                        var lmList = coll.PressureModifiers;
                        if (lmList != null)
                        {
                            int count = lmList.Count;
                            for (int i = 0; i < count; i++)
                            {
                                var mod = lmList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    short current = stats.GetModifier(skIdx);
                                    stats.SetModifier(skIdx, (short)(current + val));
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            lock (SyncLock)
            {
                _itemStatsCache[ptr] = stats;
            }
            return stats;
        }

        public static void OnSceneUnloaded()
        {
            lock (SyncLock)
            {
                ShadowState.Reset();
                _bisClassByPointer.Clear();
                _bisClassByEntityId.Clear();
                _precomputedDeltas.Clear();
                _itemBonusSummaryByEntityId.Clear();
                _itemBonusSummaryByPointer.Clear();
                _cachedTooltipByPointer.Clear();
                _cachedTooltipByEntityId.Clear();
                _itemStatsCache.Clear();
                _ownedItemPointers.Clear();
                _ownedItemEntityIds.Clear();
                _maxOwnedStatBySlotAndSkill.Clear();
                _itemSkillSummariesByPointer.Clear();
                _cachedInvMgr = null;
                _cachedSkillMgr = null;
                _isDirty = true;
            }
            SkillAndDialogueHooks.ClearHistoricalRollDeltas();
        }

        public static BiSClass GetBiSClassification(IntPtr itemPtr)
        {
            if (itemPtr == IntPtr.Zero) return BiSClass.None;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }
            lock (SyncLock)
            {
                return _bisClassByPointer.TryGetValue(itemPtr, out var cls) ? cls : BiSClass.None;
            }
        }

        public static BiSClass GetBiSClassification(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return BiSClass.None;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }
            lock (SyncLock)
            {
                return _bisClassByEntityId.TryGetValue(entityId, out var cls) ? cls : BiSClass.None;
            }
        }

        public static unsafe int GetFinalSkillDelta(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }
            string norm = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(norm)) return 0;

            int idx = GetSkillIndex(norm);
            if (idx >= 0 && idx < 64)
            {
                NativeModConfig* pCfg = Plugin.PtrNativeConfig;
                if (pCfg != null && (pCfg->SuppressedSkillsMask & (1UL << idx)) != 0)
                {
                    return 0; // Inviolable Law 2: Single bitwise operation gating
                }
            }

            lock (SyncLock)
            {
                if (_precomputedDeltas.TryGetValue(norm, out int delta))
                {
                    return delta;
                }
            }
            return 0;
        }

        public static string GetTooltipAnnotation(string entityId, IntPtr itemPtr = default)
        {
            if (string.IsNullOrEmpty(entityId) && itemPtr == IntPtr.Zero) return string.Empty;
            if (_isDirty && !_isEvaluating && LifecycleGuard.IsSaveLoadGateOpen())
            {
                Evaluate();
            }

            lock (SyncLock)
            {
                if (itemPtr != IntPtr.Zero && _cachedTooltipByPointer.TryGetValue(itemPtr, out var tip) && !string.IsNullOrEmpty(tip))
                {
                    return tip;
                }

                if (!string.IsNullOrEmpty(entityId) && _cachedTooltipByEntityId.TryGetValue(entityId, out tip) && !string.IsNullOrEmpty(tip))
                {
                    return tip;
                }
            }

            return string.Empty;
        }

        public static unsafe bool Evaluate(bool force = false)
        {
            if (_isEvaluating) return false;
            if (!force && !_isDirty) return false;
            if (!LifecycleGuard.IsSaveLoadGateOpen()) return false;

            _isEvaluating = true;
            try
            {
                lock (SyncLock)
                {
                    var invMgr = GetInventoryManager();
                    if (invMgr == null || invMgr.Pointer == IntPtr.Zero) return false;

                    unsafe
                    {
                        NativeC4InventoryManager* pInv = (NativeC4InventoryManager*)invMgr.Pointer;
                        if (pInv == null || pInv->IsInitialized == 0)
                        {
                            ClearItemStatsCache();
                            return false; // Gate held: do not evaluate during deserialization
                        }
                    }

                    var skillMgr = GetSkillManager();
                    if (skillMgr == null || skillMgr.Pointer == IntPtr.Zero) return false;

                    float rc = Plugin.Instance != null ? Plugin.Instance.ConditioningRatio.Value : 0.75f;
                    bool suppressPenalties = Plugin.Instance != null && Plugin.Instance.SuppressEquippedPenalties.Value;

                    ShadowState.Reset();
                    _bisClassByPointer.Clear();
                    _bisClassByEntityId.Clear();
                    _precomputedDeltas.Clear();
                    _itemBonusSummaryByEntityId.Clear();
                    _itemBonusSummaryByPointer.Clear();
                    _cachedTooltipByPointer.Clear();
                    _cachedTooltipByEntityId.Clear();

                    // -----------------------------------------------------------------
                    // PHASE A: Live Entity Master List BiS Pooling (Directive v6.8.0)
                    // Targets live RtItemEntity instances via C4InventoryManager.GetAllItems() directly.
                    // Eliminates uninitialized entity dropout and zero-modifier save-data defects.
                    // Raw indexed for-loop prevents IL2CPP iterator allocations and iterator dropouts.
                    // -----------------------------------------------------------------
                    _ownedItemPointers.Clear();
                    _ownedItemEntityIds.Clear();
                    _maxOwnedStatBySlotAndSkill.Clear();
                    _itemSkillSummariesByPointer.Clear();
                    _itemStatsCache.Clear(); // Guarantee fresh live modifiers for all evaluated entities

                    Dictionary<int, RtItemEntity> equippedBySlot = new Dictionary<int, RtItemEntity>();
                    Dictionary<int, ItemStats> equippedStatsBySlot = new Dictionary<int, ItemStats>();
                    Dictionary<int, List<RtItemEntity>> unequippedBySlot = new Dictionary<int, List<RtItemEntity>>();
                    Dictionary<int, List<RtItemEntity>> allOwnedItemsBySlot = new Dictionary<int, List<RtItemEntity>>();
                    HashSet<IntPtr> equippedPointers = new HashSet<IntPtr>();

                    HashSet<string> ownedInventoryEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var invObj = invMgr.Inventory;
                        if (invObj != null && invObj.Pointer != IntPtr.Zero && invObj.Items != null)
                        {
                            int invItemCount = invObj.Items.Count;
                            for (int j = 0; j < invItemCount; j++)
                            {
                                var record = invObj.Items[j];
                                if (record != null && !string.IsNullOrEmpty(record.EntityID))
                                {
                                    ownedInventoryEntityIds.Add(record.EntityID);
                                }
                            }
                        }
                    }
                    catch { }

                    // -------------------------------------------------------------
                    // PRE-COMPUTE EQUIPPED ITEMS: Build local HashSets to avoid
                    // calling IsItemEquipped() for each database item (causes
                    // native FeldConsole warnings: "There is no item called X 
                    // equipped in the inventory")
                    // -------------------------------------------------------------
                    HashSet<string> equippedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    HashSet<IntPtr> equippedEntityPointers = new HashSet<IntPtr>();

                    // Check all standard gear slots (1-9) for equipped items
                    for (int slotCheck = 1; slotCheck <= 9; slotCheck++)
                    {
                        try
                        {
                            if (invMgr.TryGetGearEntityFromSlot((ZAUM.FELD.C4.Dialogues.Model.Entities.GearSlot)slotCheck, out var eqEntity) &&
                                eqEntity != null && eqEntity.Pointer != IntPtr.Zero)
                            {
                                equippedEntityPointers.Add(eqEntity.Pointer);
                                if (!string.IsNullOrEmpty(eqEntity.Name))
                                    equippedEntityIds.Add(eqEntity.Name);
                                if (!string.IsNullOrEmpty(eqEntity.ShortName))
                                    equippedEntityIds.Add(eqEntity.ShortName);
                            }
                        }
                        catch { }
                    }

                    var allLiveItems = invMgr.GetAllItems();
                    if (allLiveItems != null)
                    {
                        var readOnlyColl = allLiveItems.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RtItemEntity>>();
                        int liveCount = readOnlyColl != null ? readOnlyColl.Count : 0;

                        for (int i = 0; i < liveCount; i++)
                        {
                            var itm = allLiveItems[i];
                            if (itm == null || itm.Pointer == IntPtr.Zero) continue;

                            int rawSlot = (int)itm.GearSlot;
                            int slot = ResolveEffectiveSlot(itm, rawSlot);
                            if (slot <= 0) continue; // Skip non-wearables (tools, consumables, quest items)

                            // -------------------------------------------------------------
                            // DIRECTIVE 1: The Ownership Filter (Purging the BiS Pool)
                            // Filter GetAllItems() so only items the player actually possesses
                            // enter the Absolute BiS pool.
                            // -------------------------------------------------------------
                            bool isOwned = false;
                            bool isEquipped = false;

                            // 1. Live Equip Status Check - Use pre-computed HashSets (no native warnings)
                            if (!string.IsNullOrEmpty(itm.Name) && equippedEntityIds.Contains(itm.Name))
                            {
                                isEquipped = true;
                                isOwned = true;
                            }
                            else if (!isEquipped && !string.IsNullOrEmpty(itm.ShortName) && equippedEntityIds.Contains(itm.ShortName))
                            {
                                isEquipped = true;
                                isOwned = true;
                            }
                            else if (equippedEntityPointers.Contains(itm.Pointer))
                            {
                                isEquipped = true;
                                isOwned = true;
                            }

                            // 2. Native Inventory Possession & Count Checks
                            if (!isOwned)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(itm.Name))
                                    {
                                        if (invMgr.HasItemOfType(itm.Name, true) || invMgr.GetItemCarryAmount(itm.Name, true, false) > 0)
                                        {
                                            isOwned = true;
                                        }
                                    }
                                    if (!isOwned && !string.IsNullOrEmpty(itm.ShortName))
                                    {
                                        if (invMgr.HasItemOfType(itm.ShortName, true) || invMgr.GetItemCarryAmount(itm.ShortName, true, false) > 0)
                                        {
                                            isOwned = true;
                                        }
                                    }
                                }
                                catch { }
                            }

                            // 3. Fallback: Save/Inventory Object Records Verification
                            if (!isOwned && ownedInventoryEntityIds.Count > 0)
                            {
                                if ((!string.IsNullOrEmpty(itm.Name) && ownedInventoryEntityIds.Contains(itm.Name)) ||
                                    (!string.IsNullOrEmpty(itm.ShortName) && ownedInventoryEntityIds.Contains(itm.ShortName)))
                                {
                                    isOwned = true;
                                }
                            }

                            // Purge unowned database items from the Absolute BiS pool
                            if (!isOwned && !isEquipped)
                            {
                                continue;
                            }

                            _ownedItemPointers.Add(itm.Pointer);
                            if (!string.IsNullOrEmpty(itm.Name)) _ownedItemEntityIds.Add(itm.Name);
                            if (!string.IsNullOrEmpty(itm.ShortName)) _ownedItemEntityIds.Add(itm.ShortName);

                            ItemStats itmStats = GetOrCreateItemStats(itm);

                            if (isEquipped)
                            {
                                equippedBySlot[slot] = itm;
                                equippedPointers.Add(itm.Pointer);
                                equippedStatsBySlot[slot] = itmStats;
                                if (slot < 10) ShadowState.EquippedItemEntityPointers[slot] = itm.Pointer.ToInt64();
                            }
                            else
                            {
                                if (!unequippedBySlot.TryGetValue(slot, out var uList))
                                {
                                    uList = new List<RtItemEntity>();
                                    unequippedBySlot[slot] = uList;
                                }
                                uList.Add(itm);
                            }

                            if (!allOwnedItemsBySlot.TryGetValue(slot, out var ownedList))
                            {
                                ownedList = new List<RtItemEntity>();
                                allOwnedItemsBySlot[slot] = ownedList;
                            }
                            ownedList.Add(itm);
                        }
                    }

                    // 3. Compute absolute highest stats across ALL owned items per slot (0..14)
                    foreach (var kvp in allOwnedItemsBySlot)
                    {
                        int s = kvp.Key;
                        var itemsInSlot = kvp.Value;
                        int[] maxStatsArr = new int[15];
                        _maxOwnedStatBySlotAndSkill[s] = maxStatsArr;

                        for (int i = 0; i < itemsInSlot.Count; i++)
                        {
                            ItemStats itmStats = GetOrCreateItemStats(itemsInSlot[i]);
                            for (int k = 0; k < 15; k++)
                            {
                                if (IsSkillSuppressed(k)) continue;
                                int val = itmStats.GetModifier(k);
                                if (suppressPenalties && val < 0) val = 0;
                                if (val > maxStatsArr[k])
                                {
                                    maxStatsArr[k] = val;
                                }
                            }
                        }
                    }

                    // 4. Absolute BiS Classification & Tooltip Generation for every owned item
                    foreach (var kvp in allOwnedItemsBySlot)
                    {
                        int s = kvp.Key;
                        var itemsInSlot = kvp.Value;
                        if (!_maxOwnedStatBySlotAndSkill.TryGetValue(s, out var maxStatsArr) || maxStatsArr == null)
                        {
                            maxStatsArr = new int[15];
                            _maxOwnedStatBySlotAndSkill[s] = maxStatsArr;
                        }

                        for (int i = 0; i < itemsInSlot.Count; i++)
                        {
                            var itm = itemsInSlot[i];
                            ItemStats itmStats = GetOrCreateItemStats(itm);

                            bool isEquipped = equippedPointers.Contains(itm.Pointer);
                            if (!isEquipped && equippedBySlot.TryGetValue(s, out var eq) && eq != null)
                            {
                                if ((!string.IsNullOrEmpty(eq.Name) && string.Equals(eq.Name, itm.Name, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrEmpty(eq.ShortName) && string.Equals(eq.ShortName, itm.ShortName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    isEquipped = true;
                                }
                            }

                            List<string> bisSkills = new List<string>();
                            string firstComparisonDiag = null;

                            for (int k = 0; k < 15; k++)
                            {
                                if (IsSkillSuppressed(k)) continue;
                                int val = itmStats.GetModifier(k);
                                if (suppressPenalties && val < 0) val = 0;

                                int maxForSlot = maxStatsArr[k];
                                string skillKey = GetCanonicalName(ZeroParadesSkills[k]);

                                if (val > 0)
                                {
                                    if (firstComparisonDiag == null)
                                    {
                                        firstComparisonDiag = $"Max {skillKey}: {maxForSlot}";
                                    }

                                    if (val >= maxForSlot)
                                    {
                                        string canonicalName = CanonicalSkillDisplayNames[k];
                                        bisSkills.Add($"+{val} {canonicalName}");
                                    }
                                }
                            }

                            string failAnnotation = GetExtractionFailureAnnotation(itm);
                            if (!string.IsNullOrEmpty(failAnnotation))
                            {
                                SetBiSClassification(itm, BiSClass.CosmeticOnly, failAnnotation, s, overrideTooltip: failAnnotation);
                            }
                            else if (bisSkills.Count > 0)
                            {
                                BiSClass cls = isEquipped ? BiSClass.EquippedBiS : BiSClass.UniqueBiS;
                                string bonusSummary = string.Join(", ", bisSkills);
                                SetBiSClassification(itm, cls, bonusSummary, s);
                            }
                            else
                            {
                                SetBiSClassification(itm, BiSClass.CosmeticOnly, firstComparisonDiag, s);
                            }
                        }
                    }

                    // 5. Active Check Math: Compute raw deltas between unequipped gear and equipped gear
                    Dictionary<string, int> deltaRawBySkill = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, int> statBestBySkill = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, int> statEquippedBySkill = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (var sk in KnownSkills)
                    {
                        deltaRawBySkill[sk] = 0;
                        statBestBySkill[sk] = 0;
                        statEquippedBySkill[sk] = 0;
                    }

                    HashSet<int> activeSlots = new HashSet<int>(allOwnedItemsBySlot.Keys);

                    foreach (int s in activeSlots)
                    {
                        unequippedBySlot.TryGetValue(s, out var unequippedList);
                        equippedStatsBySlot.TryGetValue(s, out var eqStats);

                        for (int k = 0; k < 15; k++)
                        {
                            string sk = ZeroParadesSkills[k];
                            if (IsSkillSuppressed(k)) continue;

                            int eqRawVal = eqStats.GetModifier(k);
                            int eqVal = (suppressPenalties && eqRawVal < 0) ? 0 : eqRawVal;
                            statEquippedBySkill[sk] = Math.Max(statEquippedBySkill[sk], eqVal);

                            int bestUnequipped = 0;
                            if (unequippedList != null)
                            {
                                for (int i = 0; i < unequippedList.Count; i++)
                                {
                                    ItemStats itmStats = GetOrCreateItemStats(unequippedList[i]);
                                    int val = itmStats.GetModifier(k);
                                    if (suppressPenalties && val < 0) val = 0;
                                    if (val > bestUnequipped)
                                    {
                                        bestUnequipped = val;
                                    }
                                }
                            }

                            if (bestUnequipped > statBestBySkill[sk])
                            {
                                statBestBySkill[sk] = bestUnequipped;
                            }

                            int deltaSlot = Math.Max(0, bestUnequipped - eqVal);
                            if (deltaSlot > 0)
                            {
                                deltaRawBySkill[sk] += deltaSlot;
                            }
                        }
                    }

                    // -----------------------------------------------------------------
                    // PHASE B: Potential-Gated Conditioning Math
                    // -----------------------------------------------------------------
                    var allEvaluatedSkills = new HashSet<string>(AllSkills, StringComparer.OrdinalIgnoreCase);
                    foreach (var k in deltaRawBySkill.Keys) allEvaluatedSkills.Add(k);

                    bool diminishingReturns = Plugin.Instance == null || Plugin.Instance.DiminishingReturnsEnabled.Value;
                    float bonusCap = (Plugin.Instance != null && Plugin.Instance.StashAssistBonusCap != null) ? Plugin.Instance.StashAssistBonusCap.Value : 6f;
                    float dimThreshold = Plugin.DiminishingThreshold != null ? Plugin.DiminishingThreshold.Value : 3f;
                    float dimRatio = Plugin.DiminishingRatio != null ? Plugin.DiminishingRatio.Value : 0.75f;

                    foreach (var sk in allEvaluatedSkills)
                    {
                        if (IsSkillSuppressed(sk))
                        {
                            _precomputedDeltas[sk] = 0;
                            continue;
                        }

                        int rawDelta = deltaRawBySkill.TryGetValue(sk, out int rd) ? rd : 0;

                        // Scaled Delta: Apply Configurable Diminishing Returns (Linear Falloff)
                        float effectiveRaw = rawDelta;
                        if (diminishingReturns && effectiveRaw > dimThreshold)
                        {
                            effectiveRaw = dimThreshold + ((effectiveRaw - dimThreshold) * dimRatio);
                        }
                        
                        float scaledDelta = effectiveRaw * rc;

                        // Cap the final value strictly by the configured StashAssistBonusCap (Ck)
                        int finalDelta = rawDelta > 0 ? Mathf.RoundToInt(Mathf.Clamp(scaledDelta, 0.0f, bonusCap)) : 0;

                        _precomputedDeltas[sk] = finalDelta;
                    }

                    ShadowState.RecalculateTotals();
                    _isDirty = false;
                    return true;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                _isEvaluating = false;
            }
        }

        private static void ExtractItemModifiers(RtItemEntity item, out Dictionary<string, int> mods)
        {
            mods = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (item == null || item.Pointer == IntPtr.Zero) return;

            try
            {
                var coll = item.GearModifiersCollection ?? item.m_gearModifiersCollection;
                if (coll != null && coll.Pointer != IntPtr.Zero)
                {
                    try
                    {
                        var smList = coll.SkillModifiers;
                        if (smList != null)
                        {
                            for (int i = 0; i < smList.Count; i++)
                            {
                                var mod = smList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    AddSingleMod(mods, ZeroParadesSkills[skIdx], val);
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var pmList = coll.PersonaModifiers;
                        if (pmList != null)
                        {
                            for (int i = 0; i < pmList.Count; i++)
                            {
                                var mod = pmList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    AddSingleMod(mods, ZeroParadesSkills[skIdx], val);
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var lmList = coll.PressureModifiers;
                        if (lmList != null)
                        {
                            for (int i = 0; i < lmList.Count; i++)
                            {
                                var mod = lmList[i];
                                if (mod != null && TryExtractModifier(mod.ID, mod.Value, mod.Description, mod.SourcePropertyPath, mod.SourceEntityId, out int skIdx, out int val))
                                {
                                    AddSingleMod(mods, ZeroParadesSkills[skIdx], val);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void AddSingleMod(Dictionary<string, int> mods, string key, int val)
        {
            if (mods.TryGetValue(key, out int existing))
                mods[key] = existing + val;
            else
                mods[key] = val;
        }

        private static string FormatTooltipString(BiSClass cls, string bonusSummary, int slot = 0)
        {
            switch (cls)
            {
                case BiSClass.EquippedBiS:
                case BiSClass.EquippedSharedBiS:
                {
                    string bonusSuffix = !string.IsNullOrEmpty(bonusSummary) ? $" ({bonusSummary})" : "";
                    return $"<color=#FFD700>[Optimal: Equipped BiS{bonusSuffix}]</color>";
                }
                case BiSClass.UniqueBiS:
                case BiSClass.SharedBiS:
                {
                    string bonusSuffix = !string.IsNullOrEmpty(bonusSummary) ? $" ({bonusSummary})" : "";
                    return $"<color=#FFD700>[Optimal: BiS{bonusSuffix}]</color>";
                }
                case BiSClass.OutclassedEquipped:
                case BiSClass.OutclassedStash:
                case BiSClass.CosmeticOnly:
                default:
                {
                    string diag = !string.IsNullOrEmpty(bonusSummary) ? $" | {bonusSummary}" : "";
                    string slotTag = slot > 0 ? $" (Slot: {slot}{diag})" : "";
                    return $"<color=#888888>[Outclassed]{slotTag}</color>";
                }
            }
        }

        private static void SetBiSClassification(RtItemEntity item, BiSClass cls, string bonusSummary = null, int slot = 0, string overrideTooltip = null)
        {
            if (item == null || item.Pointer == IntPtr.Zero) return;

            _bisClassByPointer[item.Pointer] = cls;
            if (!string.IsNullOrEmpty(bonusSummary))
            {
                _itemBonusSummaryByPointer[item.Pointer] = bonusSummary;
            }

            int resolvedSlot = slot > 0 ? slot : ResolveEffectiveSlot(item);
            string formattedTip = !string.IsNullOrEmpty(overrideTooltip)
                ? overrideTooltip
                : FormatTooltipString(cls, bonusSummary, resolvedSlot);

            if (!string.IsNullOrEmpty(formattedTip))
            {
                _cachedTooltipByPointer[item.Pointer] = formattedTip;
            }

            Action<string> updateEntitySummary = (key) =>
            {
                if (string.IsNullOrEmpty(key)) return;
                _bisClassByEntityId[key] = cls;
                if (!string.IsNullOrEmpty(bonusSummary))
                {
                    _itemBonusSummaryByEntityId[key] = bonusSummary;
                }
                if (!string.IsNullOrEmpty(formattedTip))
                {
                    _cachedTooltipByEntityId[key] = formattedTip;
                }
            };

            if (!string.IsNullOrEmpty(item.ShortName))
            {
                updateEntitySummary(item.ShortName);
            }
            if (!string.IsNullOrEmpty(item.Name))
            {
                updateEntitySummary(item.Name);
            }
        }
    }
}
