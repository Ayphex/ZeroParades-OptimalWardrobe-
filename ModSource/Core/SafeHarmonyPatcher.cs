// === FILE: ModSource/Core/SafeHarmonyPatcher.cs ===
// Zero-Injector, ICF-Safe & Thunk-Safe IL2CPP Harmony Patching Guard for Unity 6000.3+.
// Prevents DobbyDetour aborts on 5-byte jmp thunks / tiny stubs and prevents double-detouring
// Identical COMDAT Folded (ICF) native method pointers.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;

namespace ZeroParadesOptimalWardrobe.Core
{
    public static class SafeHarmonyPatcher
    {
        private static readonly object SyncLock = new object();
        private static readonly HashSet<IntPtr> s_PatchedNativePointers = new HashSet<IntPtr>();

        /// <summary>
        /// Harmony [HarmonyPrepare] callback.
        /// When original == null, Harmony is checking class-level readiness (returns true).
        /// When original != null, validates the native IL2CPP MethodPointer before Dobby detours it.
        /// </summary>
        public static bool PrepareMethod(MethodBase original)
        {
            if (original == null)
            {
                return true;
            }

            if (!TryValidateIl2CppPatchTarget(original, out IntPtr nativePtr, out string skipReason))
            {
                string typeName = original.DeclaringType != null ? original.DeclaringType.Name : "<UnknownType>";
                Plugin.WriteTrace($"[SAFE-PATCH] Skipping {typeName}.{original.Name}: {skipReason}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies all [HarmonyPatch]-annotated classes in the plugin assembly individually,
        /// isolating any missing/changed method signatures so a single patch failure never aborts Load().
        /// </summary>
        public static void ApplyAllPatches(Harmony harmony)
        {
            if (harmony == null) return;

            Type[] types;
            try
            {
                types = AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[SAFE-PATCH] Failed enumerating plugin types: {ex.Message}");
                return;
            }

            int appliedClasses = 0;
            int skippedClasses = 0;

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null) continue;

                bool hasHarmonyAttr = false;
                try
                {
                    var attrs = type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false);
                    hasHarmonyAttr = attrs != null && attrs.Length > 0;
                }
                catch { }

                if (!hasHarmonyAttr) continue;

                try
                {
                    var processor = harmony.CreateClassProcessor(type);
                    var replacements = processor.Patch();
                    if (replacements != null && replacements.Count > 0)
                    {
                        appliedClasses++;
                    }
                    else
                    {
                        skippedClasses++;
                    }
                }
                catch (Exception ex)
                {
                    skippedClasses++;
                    Plugin.Logger?.LogWarning($"[SAFE-PATCH] Isolated patch failure in {type.Name}: {ex.Message}");
                }
            }

            Plugin.WriteTrace($"[SAFE-PATCH] Patch pass complete: {appliedClasses} patch groups active, {skippedClasses} skipped.");
        }

        public static unsafe bool TryValidateIl2CppPatchTarget(
            MethodBase method,
            out IntPtr nativeMethodPtr,
            out string skipReason)
        {
            nativeMethodPtr = IntPtr.Zero;
            skipReason = null;

            if (method == null || method.DeclaringType == null)
            {
                skipReason = "null method or declaring type";
                return false;
            }

            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters)
            {
                skipReason = "abstract or generic method";
                return false;
            }

            FieldInfo ptrField = null;
            try
            {
                ptrField = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            }
            catch (Exception ex)
            {
                skipReason = "failed resolving NativeMethodInfoPtr field (" + ex.Message + ")";
                return false;
            }

            if (ptrField == null)
            {
                try
                {
                    var fieldAccessor = Il2CppInteropUtils.GetIl2CppFieldInfoPointerFieldForGeneratedFieldAccessor(method);
                    if (fieldAccessor != null)
                    {
                        skipReason = "IL2CPP field accessor cannot be native-detoured";
                        return false;
                    }
                }
                catch { }

                // Non-IL2CPP managed method
                return true;
            }

            try
            {
                RuntimeHelpers.RunClassConstructor(method.DeclaringType.TypeHandle);

                object rawVal = ptrField.GetValue(null);
                IntPtr methodInfoPtr = rawVal is IntPtr ptr ? ptr : IntPtr.Zero;
                if (methodInfoPtr == IntPtr.Zero)
                {
                    skipReason = "NativeMethodInfoPtr is zero";
                    return false;
                }

                INativeMethodInfoStruct wrapped = UnityVersionHandler.Wrap((Il2CppMethodInfo*)methodInfoPtr);
                if (wrapped == null)
                {
                    skipReason = "UnityVersionHandler.Wrap returned null";
                    return false;
                }

                nativeMethodPtr = wrapped.MethodPointer;
                if (nativeMethodPtr == IntPtr.Zero)
                {
                    skipReason = "native MethodPointer is zero";
                    return false;
                }

                lock (SyncLock)
                {
                    if (!s_PatchedNativePointers.Add(nativeMethodPtr))
                    {
                        skipReason = $"duplicate/ICF-folded MethodPointer 0x{nativeMethodPtr.ToInt64():X}";
                        return false;
                    }
                }

                // Validate x64 prologue bytes to prevent DobbyDetour aborts on jmp thunks / int3-padded stubs
                // (Unity 6000.3+ IL2CPP) and avoid detouring ICF-shared trivial no-op stubs.
                byte* p = (byte*)nativeMethodPtr.ToPointer();
                byte b0 = p[0];
                byte b1 = p[1];

                if (b0 == 0xCC)
                {
                    skipReason = $"int3 breakpoint/padding (0xCC) at 0x{nativeMethodPtr.ToInt64():X}";
                    return false;
                }

                if (b0 == 0xC3 || b0 == 0xC2 || (b0 == 0xF3 && b1 == 0xC3))
                {
                    skipReason = $"trivial ret stub (ICF hazard) at 0x{nativeMethodPtr.ToInt64():X}";
                    return false;
                }

                if (b0 == 0xEB)
                {
                    skipReason = $"short jmp rel8 stub (0xEB) at 0x{nativeMethodPtr.ToInt64():X}";
                    return false;
                }

                if (b0 == 0xE9)
                {
                    skipReason = $"jmp rel32 thunk (0xE9) at 0x{nativeMethodPtr.ToInt64():X}";
                    return false;
                }

                if (b0 == 0xFF && b1 == 0x25)
                {
                    skipReason = $"indirect jmp thunk (0xFF 0x25) at 0x{nativeMethodPtr.ToInt64():X}";
                    return false;
                }

                // Reject functions shorter than 5 bytes (e.g. `xor eax, eax; ret; int3`)
                for (int i = 1; i < 5; i++)
                {
                    if (p[i] == 0xC3 && p[i + 1] == 0xCC)
                    {
                        skipReason = $"tiny {i + 1}-byte stub before int3 padding at 0x{nativeMethodPtr.ToInt64():X}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                skipReason = "prologue inspection error (" + ex.Message + ")";
                return false;
            }
        }
    }
}
