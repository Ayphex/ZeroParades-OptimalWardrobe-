// === FILE: ModSource/Core/NativeEngineStructs.cs ===
// Directive: WARDROBE-01-BLUEPRINT-INTEROP-AND-QA-CONTRACT
// Zero-Allocation Blittable Native Engine Structs & Memory-State Helpers.
// Reconciled directly against ZAUM_Master_Blueprint.json (64-bit alignment).
// Inviolable Law 1: Only instance_size and offset_hex are compile-time constants.
// Inviolable Law 3: Unmanaged string header validation at offset 0x10.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZeroParadesOptimalWardrobe.Core;

namespace ZeroParadesOptimalWardrobe
{
    // =========================================================================
    // SECTION 1: ZERO-ALLOCATION BLITTABLE WARDROBE SHADOW STATE
    // =========================================================================

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct WardrobeShadowState
    {
        public const int GearSlotCount = 10; // None=0, Held_Tool=1, Shoes=2..Hat=9
        public const int SkillCount = 15;    // Coordination=0..Technoflex=14
        public const int MatrixSize = GearSlotCount * SkillCount; // 150 entries

        // Blittable inline arrays (zero heap allocation)
        public fixed short SlotSkillModifiers[MatrixSize];
        public fixed int TotalSkillModifiers[SkillCount];
        public fixed long EquippedItemEntityPointers[GearSlotCount];

        public byte IsValid;
        public byte Reserved1;
        public short Reserved2;
        public int Checksum;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIndex(int slot, int skill)
        {
            if (slot < 0 || slot >= GearSlotCount || skill < 0 || skill >= SkillCount)
                return -1;
            return (slot * SkillCount) + skill;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short GetModifier(GearSlot slot, SkillType skill)
        {
            int idx = GetIndex((int)slot, (int)skill);
            if (idx < 0) return 0;
            fixed (short* p = SlotSkillModifiers)
            {
                return p[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetModifier(GearSlot slot, SkillType skill, short delta)
        {
            int idx = GetIndex((int)slot, (int)skill);
            if (idx < 0) return;
            fixed (short* p = SlotSkillModifiers)
            {
                p[idx] = delta;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetTotalModifier(SkillType skill)
        {
            int s = (int)skill;
            if (s < 0 || s >= SkillCount) return 0;
            fixed (int* p = TotalSkillModifiers)
            {
                return p[s];
            }
        }

        public void RecalculateTotals()
        {
            fixed (short* pSlots = SlotSkillModifiers)
            fixed (int* pTotals = TotalSkillModifiers)
            {
                for (int sk = 0; sk < SkillCount; sk++)
                {
                    int sum = 0;
                    for (int sl = 0; sl < GearSlotCount; sl++)
                    {
                        sum += pSlots[(sl * SkillCount) + sk];
                    }
                    pTotals[sk] = sum;
                }
            }

            int sumCheck = 0;
            fixed (int* pTotals = TotalSkillModifiers)
            {
                for (int sk = 0; sk < SkillCount; sk++)
                    sumCheck += pTotals[sk] * (sk + 1);
            }
            Checksum = sumCheck;
            IsValid = 1;
        }

        public void Reset()
        {
            fixed (short* pSlots = SlotSkillModifiers)
            fixed (int* pTotals = TotalSkillModifiers)
            fixed (long* pItems = EquippedItemEntityPointers)
            {
                for (int i = 0; i < MatrixSize; i++) pSlots[i] = 0;
                for (int i = 0; i < SkillCount; i++) pTotals[i] = 0;
                for (int i = 0; i < GearSlotCount; i++) pItems[i] = 0;
            }
            IsValid = 0;
            Checksum = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public unsafe struct ItemStats
    {
        public const int CanonicalSkillCount = 15;
        public int GearSlot; // 0..9 (None=0, Held_Tool=1, Shoes=2, Pants=3, Gloves=4, Shirt=5, Jacket=6, Neck=7, Face=8, Hat=9)
        public fixed short SkillModifiers[CanonicalSkillCount];
        public byte HasPositiveMod;
        public byte IsValid;
        public short Reserved;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short GetModifier(int skillIndex)
        {
            if (skillIndex < 0 || skillIndex >= CanonicalSkillCount) return 0;
            fixed (short* p = SkillModifiers)
            {
                return p[skillIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetModifier(int skillIndex, short val)
        {
            if (skillIndex < 0 || skillIndex >= CanonicalSkillCount) return;
            fixed (short* p = SkillModifiers)
            {
                p[skillIndex] = val;
                if (val > 0) HasPositiveMod = 1;
            }
        }
    }

    // =========================================================================
    // SECTION 2: UNMANAGED POINTER & STRING HEADER VALIDATION HELPERS
    // =========================================================================

    public static unsafe class NativePointerHelpers
    {
        /// <summary>
        /// Inviolable Law 3: Safely inspect string length at offset 0x10.
        /// Rejects null, unaligned, out-of-bounds, and uninitialized asset pointers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidIl2CppString(IntPtr pStr, int minLen = 1, int maxLen = 4096)
        {
            if (pStr == IntPtr.Zero) return false;
            long val = pStr.ToInt64();
            if (val < 0x10000 || (val & 0x7) != 0) return false; // Bad alignment or null-page

            try
            {
                byte* ptr = (byte*)pStr.ToPointer();
                int strLen = *(int*)(ptr + 0x10);
                return strLen >= minLen && strLen <= maxLen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely reads string length at offset 0x10 if valid, or -1 if invalid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SafeGetStringLength(IntPtr pStr)
        {
            if (pStr == IntPtr.Zero) return -1;
            long val = pStr.ToInt64();
            if (val < 0x10000 || (val & 0x7) != 0) return -1;

            try
            {
                byte* ptr = (byte*)pStr.ToPointer();
                int len = *(int*)(ptr + 0x10);
                if (len < 0 || len > 4096) return -1;
                return len;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Reads a 64-bit pointer safely from an unmanaged base + offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr ReadPtrSafe(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                byte* ptr = (byte*)basePtr.ToPointer();
                return *(IntPtr*)(ptr + offset);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Reads an int32 safely from an unmanaged base + offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadIntSafe(IntPtr basePtr, int offset, int fallback = 0)
        {
            if (basePtr == IntPtr.Zero) return fallback;
            try
            {
                byte* ptr = (byte*)basePtr.ToPointer();
                return *(int*)(ptr + offset);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Reads a byte safely from an unmanaged base + offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadByteSafe(IntPtr basePtr, int offset, byte fallback = 0)
        {
            if (basePtr == IntPtr.Zero) return fallback;
            try
            {
                byte* ptr = (byte*)basePtr.ToPointer();
                return *(ptr + offset);
            }
            catch
            {
                return fallback;
            }
        }
    }

    // =========================================================================
    // SECTION 3: NATIVE IL2CPP C++ MEMORY STRUCT LAYOUTS
    // =========================================================================

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeGameStateLoadingState
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x20)] public IntPtr HudController;
        [FieldOffset(0x28)] public IntPtr NotificationManager;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeSaveManager
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x20)] public IntPtr OnInitialized;
        [FieldOffset(0x28)] public IntPtr OnDeinitialized;
        [FieldOffset(0x30)] public IntPtr Settings;
        [FieldOffset(0x38)] public IntPtr Handlers;
        [FieldOffset(0x40)] public IntPtr Triggers;
        [FieldOffset(0x48)] public IntPtr TypeToHandlersLookUp;
        [FieldOffset(0x50)] public IntPtr SaveRequestsHandler;
        [FieldOffset(BlueprintOffsets.SaveManager.Offset_IsInitialized)] public byte IsInitialized;
        [FieldOffset(BlueprintOffsets.SaveManager.Offset_IsInitializing)] public byte IsInitializing;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeChecksActiveModifiersInfoItem
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x10)] public IntPtr Explanation; // Il2CppString*
        [FieldOffset(0x18)] public int Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeSkillModifiersObject
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.SkillModifiersObject.Offset_TemporarySkillModifiers)] public IntPtr TemporarySkillModifiers;
        [FieldOffset(BlueprintOffsets.SkillModifiersObject.Offset_TemporaryFacultyModifiers)] public IntPtr TemporaryFacultyModifiers;
        [FieldOffset(BlueprintOffsets.SkillModifiersObject.Offset_GearModifiers)] public IntPtr GearModifiers;
        [FieldOffset(BlueprintOffsets.SkillModifiersObject.Offset_ConditioningCurrentValueModifiers)] public IntPtr ConditioningCurrentValueModifiers;
        [FieldOffset(BlueprintOffsets.SkillModifiersObject.Offset_ConditioningMaxRateModifiers)] public IntPtr ConditioningMaxRateModifiers;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeC4SkillManager
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_IsInitialized)] public byte IsInitialized;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_ModifiersObject)] public IntPtr ModifiersObject;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_CachedSkillList)] public IntPtr CachedSkillList;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_CachedFacultyList)] public IntPtr CachedFacultyList;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_CachedAttributeList)] public IntPtr CachedAttributeList;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_DissonantSkills)] public IntPtr DissonantSkills;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_DialogueManager)] public IntPtr DialogueManager;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_StatusEffectsManager)] public IntPtr StatusEffectsManager;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_ConditioningManager)] public IntPtr ConditioningManager;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_LimitsManager)] public IntPtr LimitsManager;
        [FieldOffset(BlueprintOffsets.C4SkillManager.Offset_PersonasManager)] public IntPtr PersonasManager;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeC4InventoryManager
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_SkillManager)] public IntPtr SkillManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_LimitsManager)] public IntPtr LimitsManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_DialogueManager)] public IntPtr DialogueManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_StatusEffectsManager)] public IntPtr StatusEffectsManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_SleepManager)] public IntPtr SleepManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_PersonasManager)] public IntPtr PersonasManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_ConditioningManager)] public IntPtr ConditioningManager;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_IsInitialized)] public byte IsInitialized;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_ItemsPool)] public IntPtr ItemsPool;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_InventoryItemSlotAmount)] public IntPtr InventoryItemSlotAmount;
        [FieldOffset(BlueprintOffsets.C4InventoryManager.Offset_InventoryObject)] public IntPtr InventoryObject;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeRtItemEntity
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ShortDescription)] public IntPtr ShortDescription; // Il2CppString* (READ-ONLY)
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ItemCategory)] public int ItemCategory;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_GearSlot)] public int GearSlot;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_Cost)] public int Cost;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_AmountGivenOnPurchase)] public int AmountGivenOnPurchase;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_MaxSlotUsage)] public int MaxSlotUsage;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_XpGain)] public int XpGain;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_Flow)] public int Flow;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_GearModifiersCollection)] public IntPtr GearModifiersCollection;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ParentItem)] public IntPtr ParentItem;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ConsumableModifiers)] public IntPtr ConsumableModifiers;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ConsumableOrbitalActivationVarName)] public IntPtr ConsumableOrbitalActivationVarName;
        [FieldOffset(BlueprintOffsets.RtItemEntity.Offset_ConsumableUsageAmountVarName)] public IntPtr ConsumableUsageAmountVarName;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeC4DialogueManager
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x68)] public byte SelfInitialize;
        [FieldOffset(0x70)] public IntPtr RiggedActiveChecks;
        [FieldOffset(0x80)] public IntPtr Project;
        [FieldOffset(0x90)] public IntPtr TimeManager;
        [FieldOffset(0x98)] public IntPtr CurrentDialogueOwner;
        [FieldOffset(0xB0)] public IntPtr C4ProjectContext;
        [FieldOffset(0xD8)] public IntPtr OnJumpToMainMenuRequested;
        [FieldOffset(0x178)] public IntPtr RedWhiteCheckResult; // Action`8
        [FieldOffset(0x180)] public IntPtr PassiveCheckResult;  // Action`3
        [FieldOffset(0x188)] public IntPtr DeferredCheckTriggered; // Action`9
        [FieldOffset(0x190)] public IntPtr CheckTriggered;      // Action`2
        [FieldOffset(0x198)] public IntPtr TryUnlockActiveCheck;
        [FieldOffset(0x230)] public IntPtr RequestRollSkillValueTrigger;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeRtCheckCard
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_RollType)] public int RollType;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_Threshold)] public int Threshold;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_SkillID)] public IntPtr SkillID;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_RequiredItemID)] public IntPtr RequiredItemID;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_DiceResultName)] public IntPtr DiceResultName;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedModifiers)] public IntPtr CachedModifiers;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_PerformedRoll)] public byte PerformedRoll;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedExerted)] public byte CachedExerted;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedAdvantageStatus)] public int CachedAdvantageStatus;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedResultState)] public int CachedResultState;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedFirstDieResult)] public int CachedFirstDieResult;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedSecondDieResult)] public int CachedSecondDieResult;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedThirdDieResult)] public int CachedThirdDieResult;
        [FieldOffset(BlueprintOffsets.RtCheckCard.Offset_CachedResponseIndex)] public int CachedResponseIndex;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeSkillCheckModifiersController
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_UnlockedCheckLayout)] public IntPtr UnlockedCheckLayout;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_LockedCheckLayout)] public IntPtr LockedCheckLayout;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_ModsContentLayout)] public IntPtr ModsContentLayout;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_ModEntryPrefab)] public IntPtr ModEntryPrefab;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_LockedText)] public IntPtr LockedText;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_DescriptionSeperator)] public IntPtr DescriptionSeperator;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_CannotExertModifierKey)] public IntPtr CannotExertModifierKey;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_DisadvantagedModifierKey)] public IntPtr DisadvantagedModifierKey;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_ActiveModifierEntries)] public IntPtr ActiveModifierEntries;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_SortedEntries)] public IntPtr SortedEntries;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_ModifiersEntryPool)] public IntPtr ModifiersEntryPool;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_IsInitialized)] public byte IsInitialized;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_SkillManager)] public IntPtr SkillManager;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_DialogueManager)] public IntPtr DialogueManager;
        [FieldOffset(BlueprintOffsets.SkillCheckModifiersController.Offset_StatusEffectsManager)] public IntPtr StatusEffectsManager;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeModifiersController
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_ModifierEntriesContainer)] public IntPtr ModifierEntriesContainer;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_ModifiersPrefabsPreset)] public IntPtr ModifiersPrefabsPreset;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_AllowInputInModifiers)] public byte AllowInputInModifiers;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_ActiveModifierEntries)] public IntPtr ActiveModifierEntries;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_ModifierEntriesPool)] public IntPtr ModifierEntriesPool;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_ActiveModifierGroups)] public IntPtr ActiveModifierGroups;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_IsInitialized)] public byte IsInitialized;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_DialogueManager)] public IntPtr DialogueManager;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_LocalizationManager)] public IntPtr LocalizationManager;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_FacultiesAndSkillsVisualsManager)] public IntPtr FacultiesAndSkillsVisualsManager;
        [FieldOffset(BlueprintOffsets.ModifiersController.Offset_LimitsManager)] public IntPtr LimitsManager;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeInventoryItemDetailsController
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ItemImage)] public IntPtr ItemImage;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ItemNameText)] public IntPtr ItemNameText;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ItemDescriptionText)] public IntPtr ItemDescriptionText;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ItemDetailsProperties)] public IntPtr ItemDetailsProperties;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_EmptyLineEntry)] public IntPtr EmptyLineEntry;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_EmptyBackground)] public IntPtr EmptyBackground;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ModifiersController)] public IntPtr ModifiersController;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_ScrollbarVisibility)] public IntPtr ScrollbarVisibility;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_DialogueManager)] public IntPtr DialogueManager;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_GameToolsManager)] public IntPtr GameToolsManager;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_InventoryManager)] public IntPtr InventoryManager;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_LocalizationManager)] public IntPtr LocalizationManager;
        [FieldOffset(BlueprintOffsets.InventoryItemDetailsController.Offset_CurrentItem)] public IntPtr CurrentItem;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeCoreMenuButton
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(BlueprintOffsets.CoreMenuButton.Offset_SplineConnector)] public IntPtr SplineConnector;
        [FieldOffset(BlueprintOffsets.CoreMenuButton.Offset_LocalizedButtonText)] public IntPtr LocalizedButtonText;
        [FieldOffset(BlueprintOffsets.CoreMenuButton.Offset_InputContent)] public IntPtr InputContent;
        [FieldOffset(BlueprintOffsets.CoreMenuButton.Offset_LocalizationManager)] public IntPtr LocalizationManager;
        [FieldOffset(BlueprintOffsets.CoreMenuButton.Offset_LocalizationKey)] public IntPtr LocalizationKey;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeSettingsMenuController
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x68)] public IntPtr InputManager;
        [FieldOffset(0x88)] public IntPtr OnBackBtnClickEvent;
        [FieldOffset(0x90)] public IntPtr OnBackMenuBtnClickEvent;
        [FieldOffset(0xA0)] public IntPtr BackBtn;
        [FieldOffset(0xC8)] public IntPtr CanvasGroup;
        [FieldOffset(0xD0)] public IntPtr CurrentMenu;
        [FieldOffset(0xD8)] public IntPtr P1Input;
        [FieldOffset(0xE0)] public byte IsVisible;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeTabButtonMenu
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x1A0)] public IntPtr TargetMenu;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeSceneManager
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr SceneLoaded;   // UnityAction`2
        [FieldOffset(0x10)] public IntPtr SceneUnloaded; // UnityAction`1
        [FieldOffset(0x18)] public IntPtr ActiveSceneChanged;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NativeRtThoughtEntity
    {
        [FieldOffset(0x00)] public IntPtr Klass;
        [FieldOffset(0x08)] public IntPtr Monitor;
        [FieldOffset(0x18)] public IntPtr ThoughtId;
        [FieldOffset(0x20)] public IntPtr Title;
        [FieldOffset(0x28)] public IntPtr Description;
        [FieldOffset(0x30)] public int State;
        [FieldOffset(0x34)] public float Progress;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeModConfig
    {
        public int ModEnabled;             // 1 = true, 0 = false
        public int MaxItemStacks;          // Default 99
        public float ConditioningRatio;    // Default 0.75f (0.1 to 2.0)
        public int SuppressPenalties;      // 1 = true, 0 = false
        public int DiminishingReturns;     // 1 = true, 0 = false
        public int AutoEquipOptimal;       // 1 = true, 0 = false
        public int StashAssistBonus;       // Dynamic assistance modifier delta
        public int StashAssistBonusCap;    // Default 5 (+1 to +10)
        public ulong SuppressedSkillsMask; // 64-bit bitmask of suppressed skills (0..63)

        // v24.0.0: New modular UI feature flags
        public int ShopUpgradeAdvisor;     // 1 = true, 0 = false
        public int BestInSlotTags;         // 1 = true, 0 = false
        public int StashAssistance;        // 1 = true, 0 = false
        public int IncludeTools;           // 1 = true, 0 = false

        // v27.0.0: Balance & Math Tuning
        public int MaxDeltaCap;            // Default 6 (1 to 10)
        public int BaseStatCap;            // Default 4 (1 to 10)
        public int DiminishingThreshold;   // Default 2 (1 to 6)
        public float DiminishingRatio;     // Default 0.50f (0.1 to 0.9)
    }

    public static class NativeEngineStructs
    {
        public static unsafe void UpdateConfig(
            bool shopAdvisor,
            bool bestInSlotTags,
            bool stashAssistance,
            bool affectPassiveChecks,
            bool suppressPenalties,
            bool includeTools,
            float rcRatio,
            int ckBonusCap,
            int maxDeltaCap,
            int baseStatCap,
            bool diminishingReturns,
            int diminishingThreshold,
            float diminishingRatio)
        {
            var pCfg = Plugin.PtrNativeConfig;
            if (pCfg != null)
            {
                pCfg->ShopUpgradeAdvisor = shopAdvisor ? 1 : 0;
                pCfg->BestInSlotTags = bestInSlotTags ? 1 : 0;
                pCfg->StashAssistance = stashAssistance ? 1 : 0;
                pCfg->SuppressPenalties = suppressPenalties ? 1 : 0;
                pCfg->IncludeTools = includeTools ? 1 : 0;
                pCfg->ConditioningRatio = rcRatio;
                pCfg->StashAssistBonusCap = ckBonusCap;
                pCfg->MaxDeltaCap = maxDeltaCap;
                pCfg->BaseStatCap = baseStatCap;
                pCfg->DiminishingReturns = diminishingReturns ? 1 : 0;
                pCfg->DiminishingThreshold = diminishingThreshold;
                pCfg->DiminishingRatio = diminishingRatio;
            }
        }
    }
}
