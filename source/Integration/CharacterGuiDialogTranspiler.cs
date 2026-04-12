using HarmonyLib;
using PlayerInventoryLib.GUI;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace PlayerInventoryLib.Integration;

[HarmonyPatchCategory("PlayerInventoryLib:Transpilers")]
[HarmonyPatch(typeof(GuiManager), nameof(GuiManager.RegisterDefaultDialogs))]
public static class RegisterDefaultDialogsTranspiler
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        ConstructorInfo originalCtor = AccessTools.Constructor(
            typeof(GuiDialogCharacter),
            [typeof(ICoreClientAPI)]
        );

        ConstructorInfo replacementCtor = AccessTools.Constructor(
            typeof(CustomGuiDialogCharacter),
            [typeof(ICoreClientAPI)]
        );

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo ctor && ctor == originalCtor)
            {
                yield return new CodeInstruction(OpCodes.Newobj, replacementCtor);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}