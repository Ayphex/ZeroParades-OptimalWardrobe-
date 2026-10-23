// === FILE: ModSource/Core/BlueprintOffsets.cs ===
// Directive: WARDROBE-01-BLUEPRINT-INTEROP-AND-QA-CONTRACT
// Reconciled compile-time invariants directly against ZAUM_Master_Blueprint.json (64-bit IL2CPP alignment).
// Inviolable Law 1: Only instance_size and offset_hex are compile-time constants.
// Volatile runtime ASLR pointers (klass, method_ptr, native_class_ptr) are NEVER hardcoded.

using System;

namespace ZeroParadesOptimalWardrobe.Core
{
    // =========================================================================
    // SECTION 1: CORE ENUM DEFINITIONS (Exact IL2CPP Runtime Integer Values)
    // =========================================================================

    public enum GearSlot : int
    {
        None = 0,
        Held_Tool = 1,
        Shoes = 2,
        Pants = 3,
        Gloves = 4,
        Shirt = 5,
        Jacket = 6,
        Neck = 7,
        Face = 8,
        Hat = 9
    }

    public enum ItemCategory : int
    {
        Other = 0,
        Consumable = 1,
        Clothing = 2,
        Tool = 3
    }

    public enum Faculty : int
    {
        Physical = 0,
        Social = 1,
        Intellectual = 2
    }

    public enum SkillType : int
    {
        Coordination = 0,
        Doppelgang = 1,
        Instincts = 2,
        Sensors = 3,
        Shadowplay = 4,
        Blueprints = 5,
        ColdRead = 6,
        Nerve = 7,
        Personalism = 8,
        Statehood = 9,
        Entanglement = 10,
        GreyMatter = 11,
        Poetics = 12,
        Records = 13,
        Technoflex = 14
    }

    [Flags]
    public enum SkillInfoFlags : int
    {
        None = 0,
        Temp_Skill_Modifiers = 2,
        Temp_Faculty_Modifiers = 4,
        Gear_Modifiers = 8,
        Conditioning_Modifiers = 16,
        All = 2147483647 // 0x7FFFFFFF
    }

    public enum SkillModifierType : int
    {
        CurrentValue = 0,
        RelativeMaxRate = 1
    }

    public enum CheckType : int
    {
        None = 0,
        ActiveCheck = 1,
        PassiveCheck = 2,
        AnyCheck = 3,
        ActiveCheckFail = 4,
        PassiveCheckFail = 5,
        AnyCheckFail = 6,
        Counter = 7,
        NPC = 8,
        ItemGained = 9
    }

    public enum SkillThresholds : int
    {
        Trivial_6 = 6,
        Trivial_7 = 7,
        Easy_8 = 8,
        Easy_9 = 9,
        Medium_10 = 10,
        Medium_11 = 11,
        Challenging_12 = 12,
        Formidable_13 = 13,
        Legendary_14 = 14,
        Heroic_15 = 15,
        Godly_16 = 16,
        Godly_17 = 17,
        Impossible_18 = 18,
        Impossible_19 = 19,
        Impossible_20 = 20
    }

    // =========================================================================
    // SECTION 2: COMPILE-TIME INSTANCE SIZES & FIELD OFFSETS
    // =========================================================================

    public static class BlueprintOffsets
    {
        // ---------------------------------------------------------------------
        // C4InventoryManager (size: 192 / 0xC0)
        // ---------------------------------------------------------------------
        public static class C4InventoryManager
        {
            public const int InstanceSize = 192; // 0xC0
            public const int Offset_SkillManager = 0x68;
            public const int Offset_LimitsManager = 0x70;
            public const int Offset_DialogueManager = 0x78;
            public const int Offset_StatusEffectsManager = 0x80;
            public const int Offset_SleepManager = 0x88;
            public const int Offset_PersonasManager = 0x90;
            public const int Offset_ConditioningManager = 0x98;
            public const int Offset_IsInitialized = 0xA0; // byte (1 = initialized)
            public const int Offset_ItemsPool = 0xA8;
            public const int Offset_InventoryItemSlotAmount = 0xB0;
            public const int Offset_InventoryObject = 0xB8;
        }

        // ---------------------------------------------------------------------
        // RtItemEntity (size: 176 / 0xB0)
        // ---------------------------------------------------------------------
        public static class RtItemEntity
        {
            public const int InstanceSize = 176; // 0xB0
            public const int Offset_ShortDescription = 0x60; // Il2CppString* (READ-ONLY)
            public const int Offset_ItemCategory = 0x68;     // int (ItemCategory)
            public const int Offset_GearSlot = 0x6C;         // int (GearSlot)
            public const int Offset_Cost = 0x70;             // int
            public const int Offset_AmountGivenOnPurchase = 0x74; // int
            public const int Offset_MaxSlotUsage = 0x78;     // int
            public const int Offset_XpGain = 0x7C;           // int
            public const int Offset_Flow = 0x80;             // int
            public const int Offset_GearModifiersCollection = 0x88; // ModifiersCollection*
            public const int Offset_ParentItem = 0x90;       // Il2CppString*
            public const int Offset_ConsumableModifiers = 0x98; // ConsumableModifiers*
            public const int Offset_ConsumableOrbitalActivationVarName = 0xA0;
            public const int Offset_ConsumableUsageAmountVarName = 0xA8;
        }

        // ---------------------------------------------------------------------
        // C4SkillManager (size: 192 / 0xC0)
        // ---------------------------------------------------------------------
        public static class C4SkillManager
        {
            public const int InstanceSize = 192; // 0xC0
            public const int Offset_IsInitialized = 0x68; // byte
            public const int Offset_ModifiersObject = 0x70; // SkillModifiersObject*
            public const int Offset_CachedSkillList = 0x78; // List<RtSkillEntity>*
            public const int Offset_CachedFacultyList = 0x80;
            public const int Offset_CachedAttributeList = 0x88;
            public const int Offset_DissonantSkills = 0x90;
            public const int Offset_DialogueManager = 0x98;
            public const int Offset_StatusEffectsManager = 0xA0;
            public const int Offset_ConditioningManager = 0xA8;
            public const int Offset_LimitsManager = 0xB0;
            public const int Offset_PersonasManager = 0xB8;
        }

        // ---------------------------------------------------------------------
        // SkillModifiersObject (size: 56 / 0x38)
        // ---------------------------------------------------------------------
        public static class SkillModifiersObject
        {
            public const int InstanceSize = 56; // 0x38
            public const int Offset_TemporarySkillModifiers = 0x10;
            public const int Offset_TemporaryFacultyModifiers = 0x18;
            public const int Offset_GearModifiers = 0x20; // Dictionary<string, Dictionary<string, int>>*
            public const int Offset_ConditioningCurrentValueModifiers = 0x28;
            public const int Offset_ConditioningMaxRateModifiers = 0x30;
        }

        // ---------------------------------------------------------------------
        // RtWhiteCheckCard & RtRedCheckCard (size: 248 / 0xF8)
        // ---------------------------------------------------------------------
        public static class RtCheckCard
        {
            public const int InstanceSize = 248; // 0xF8
            public const int Offset_RollType = 0x30;
            public const int Offset_Threshold = 0xB0; // int
            public const int Offset_SkillID = 0xB8;   // Il2CppString*
            public const int Offset_RequiredItemID = 0xC0; // Il2CppString*
            public const int Offset_DiceResultName = 0xC8; // Il2CppString*
            public const int Offset_CachedModifiers = 0xD0; // List<Dictionary<string, RtPropertyData>>*
            public const int Offset_PerformedRoll = 0xD8;  // byte
            public const int Offset_CachedExerted = 0xD9;  // byte
            public const int Offset_CachedAdvantageStatus = 0xDC; // int
            public const int Offset_CachedResultState = 0xE0;     // int
            public const int Offset_CachedFirstDieResult = 0xE4;  // int
            public const int Offset_CachedSecondDieResult = 0xE8; // int
            public const int Offset_CachedThirdDieResult = 0xEC;  // int
            public const int Offset_CachedResponseIndex = 0xF0;   // int
        }

        // ---------------------------------------------------------------------
        // SkillCheckModifiersController (size: 232 / 0xE8)
        // ---------------------------------------------------------------------
        public static class SkillCheckModifiersController
        {
            public const int InstanceSize = 232; // 0xE8
            public const int Offset_UnlockedCheckLayout = 0x70;
            public const int Offset_LockedCheckLayout = 0x78;
            public const int Offset_ModsContentLayout = 0x80;
            public const int Offset_ModEntryPrefab = 0x88;
            public const int Offset_LockedText = 0x90;
            public const int Offset_DescriptionSeperator = 0x98;
            public const int Offset_CannotExertModifierKey = 0xA0;
            public const int Offset_DisadvantagedModifierKey = 0xA8;
            public const int Offset_ActiveModifierEntries = 0xB0;
            public const int Offset_SortedEntries = 0xB8;
            public const int Offset_ModifiersEntryPool = 0xC0;
            public const int Offset_IsInitialized = 0xC8; // byte
            public const int Offset_SkillManager = 0xD0;
            public const int Offset_DialogueManager = 0xD8;
            public const int Offset_StatusEffectsManager = 0xE0;
        }

        // ---------------------------------------------------------------------
        // SkillCheckModifierEntry (size: 104 / 0x68)
        // ---------------------------------------------------------------------
        public static class SkillCheckModifierEntry
        {
            public const int InstanceSize = 104; // 0x68
            public const int Offset_ModifierValue = 0x20; // TextMeshProUGUI* / Text*
            public const int Offset_ModifierDescription = 0x28; // TextMeshProUGUI* / Text*
            public const int Offset_ModifierValueFormat = 0x30;
            public const int Offset_FacultyModifierFormat = 0x38;
            public const int Offset_PressureModifierFormat = 0x40;
            public const int Offset_DissonanceModifierFormat = 0x48;
            public const int Offset_IsInitialized = 0x50; // byte
            public const int Offset_DialogueManager = 0x58;
        }

        // ---------------------------------------------------------------------
        // ModifiersController (size: 112 / 0x70)
        // Inviolable Law 2: All s_... static fields (offset 0x00) are excluded!
        // ---------------------------------------------------------------------
        public static class ModifiersController
        {
            public const int InstanceSize = 112; // 0x70
            public const int Offset_ModifierEntriesContainer = 0x10;
            public const int Offset_ModifiersPrefabsPreset = 0x18;
            public const int Offset_AllowInputInModifiers = 0x20;
            public const int Offset_EnabledModifierTypes = 0x28;
            public const int Offset_ActiveModifierEntries = 0x30;
            public const int Offset_ModifierEntriesPool = 0x38;
            public const int Offset_ActiveModifierGroups = 0x40;
            public const int Offset_IsInitialized = 0x48; // byte
            public const int Offset_DialogueManager = 0x50;
            public const int Offset_LocalizationManager = 0x58;
            public const int Offset_FacultiesAndSkillsVisualsManager = 0x60;
            public const int Offset_LimitsManager = 0x68;
        }

        // ---------------------------------------------------------------------
        // CoreMenuButton (size: 456 / 0x1C8)
        // ---------------------------------------------------------------------
        public static class CoreMenuButton
        {
            public const int InstanceSize = 456; // 0x1C8
            public const int Offset_SplineConnector = 0x1A0;
            public const int Offset_LocalizedButtonText = 0x1A8;
            public const int Offset_InputContent = 0x1B0;
            public const int Offset_LocalizationManager = 0x1B8;
            public const int Offset_LocalizationKey = 0x1C0;
        }

        // ---------------------------------------------------------------------
        // MainMenuUIController (size: 320 / 0x140)
        // ---------------------------------------------------------------------
        public static class MainMenuUIController
        {
            public const int InstanceSize = 320; // 0x140
            public const int Offset_VersionText = 0x120;
            public const int Offset_StandardOpeningTransitionAnimation = 0x128;
            public const int Offset_GameOverTransitionAnimation = 0x130;
            public const int Offset_DoGameOverAnimation = 0x138;
            public const int Offset_IsImmediately = 0x139;
        }

        // ---------------------------------------------------------------------
        // InventoryItemDetailsController (size: 136 / 0x88)
        // ---------------------------------------------------------------------
        public static class InventoryItemDetailsController
        {
            public const int InstanceSize = 136; // 0x88
            public const int Offset_ItemImage = 0x20;
            public const int Offset_ItemNameText = 0x28;
            public const int Offset_ItemDescriptionText = 0x30;
            public const int Offset_ItemDetailsProperties = 0x38;
            public const int Offset_EmptyLineEntry = 0x40;
            public const int Offset_EmptyBackground = 0x48;
            public const int Offset_ModifiersController = 0x50;
            public const int Offset_ScrollbarVisibility = 0x58;
            public const int Offset_DialogueManager = 0x60;
            public const int Offset_GameToolsManager = 0x68;
            public const int Offset_InventoryManager = 0x70;
            public const int Offset_LocalizationManager = 0x78;
            public const int Offset_CurrentItem = 0x80;
        }

        // ---------------------------------------------------------------------
        // SaveManager (size: 104 / 0x68)
        // ---------------------------------------------------------------------
        public static class SaveManager
        {
            public const int InstanceSize = 104; // 0x68
            public const int Offset_Settings = 0x30;
            public const int Offset_Handlers = 0x38;
            public const int Offset_Triggers = 0x40;
            public const int Offset_TypeToHandlersLookUp = 0x48;
            public const int Offset_SaveRequestsHandler = 0x50;
            public const int Offset_IsInitialized = 0x58; // byte
            public const int Offset_IsInitializing = 0x59; // byte
        }

        // ---------------------------------------------------------------------
        // SettingsMenuController (size: 232 / 0xE8)
        // ---------------------------------------------------------------------
        public static class SettingsMenuController
        {
            public const int InstanceSize = 232; // 0xE8
            public const int Offset_InputManager = 0x68;
            public const int Offset_OnBackBtnClickEvent = 0x88;
            public const int Offset_OnBackMenuBtnClickEvent = 0x90;
            public const int Offset_ControlSchemeBtn = 0x98;
            public const int Offset_BackBtn = 0xA0;
            public const int Offset_BackMenuBtn = 0xA8;
            public const int Offset_ExitBtn = 0xB0;
            public const int Offset_CanvasGroup = 0xC8;
            public const int Offset_CurrentMenu = 0xD0;
            public const int Offset_P1Input = 0xD8;
            public const int Offset_IsVisible = 0xE0;
        }

        // ---------------------------------------------------------------------
        // TabButtonMenu (size: 424 / 0x1A8)
        // ---------------------------------------------------------------------
        public static class TabButtonMenu
        {
            public const int InstanceSize = 424; // 0x1A8
            public const int Offset_TargetMenu = 0x1A0;
        }
    }
}
