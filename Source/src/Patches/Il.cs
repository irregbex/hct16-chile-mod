using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    /// <summary>
    /// Small helpers for the few transpilers: each one swaps ONE call in a game method for a call to the mod
    /// (same arguments on the stack, same return type), which keeps the game's own code in the game.
    /// </summary>
    internal static class Il
    {
        /// <summary>Every call to 'target' becomes a call to 'replacement'; logs how many were swapped.</summary>
        public static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> code, MethodBase target, MethodInfo replacement, string where, int expectAtLeast = 1, int onlyFirst = 0)
        {
            int n = 0;
            foreach (CodeInstruction ci in code)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, target) && (onlyFirst == 0 || n < onlyFirst))
                {
                    n++;
                    var swapped = new CodeInstruction(OpCodes.Call, replacement);
                    swapped.labels.AddRange(ci.labels);
                    swapped.blocks.AddRange(ci.blocks);
                    yield return swapped;
                    continue;
                }
                yield return ci;
            }
            if (n < expectAtLeast) Debug.LogWarning("[HCT15] patch '" + where + "': " + target.Name + " not found (" + n + " swapped) - that part of the mod is off");
        }

        public static MethodInfo Getter(Type t, string property) { return AccessTools.PropertyGetter(t, property); }
    }
}
