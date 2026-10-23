# Zero Parades: Optimal Wardrobe

An autonomous, modular, and customizable wardrobe conditioning engine and shop advisor for *Zero Parades: Director's Cut*.

## Features
- **Deterministic Math Engine:** Continuously tracks owned gear and evaluates Best-in-Slot permutations across all 15 core skills.
- **Inventory Tooltip Indicators:** Dynamically injects badges and indicators into item tooltips in your inventory, clearly highlighting which pieces in your stash are optimal or Best-in-Slot.
- **Shop Upgrade Advisor:** Native UI injection in store dialogue cards indicating whether vendor gear is an upgrade over your owned stash or outclassed.
- **Zero-Stutter Performance:** Sub-millisecond evaluation cycles with zero runtime garbage allocation.
- **Save/Load Air-Gap:** Strict lifecycle protection preventing state corruption during game deserialization.
- **Modular Configuration:** Seamless dynamic syncing through BepInEx config files.

## Requirements
- *Zero Parades: Director's Cut*
- BepInEx 6 (Unity Mono/IL2CPP build for Unity 6000.3+)

## Building from Source
1. Clone the repository.
2. Open terminal in `/ModSource`.
3. Run `dotnet build -c Release`.