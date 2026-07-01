using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Monsterpatch.CatchBottleRates.Patcher
{
    /// <summary>
    /// BepInEx 5 preloader patcher.
    /// Install the built DLL in BepInEx\patchers, not BepInEx\plugins.
    ///
    /// v0.1.0 final build:
    ///   - All bottle multipliers stay vanilla/effectively 1.00x.
    ///   - Special bottles add flat house-affinity catch bonuses only.
    ///   - One matching active house gives +15 percentage points.
    ///   - Two matching active houses gives +25 percentage points.
    ///   - Final catch chance is capped at 95%.
    ///   - The EASY / MEDIUM / HARD / VERY HARD preview text updates immediately when bottles change.
    /// </summary>
    public static class CatchBottleRatesPatcher
    {
        private const string LogPrefix = "[Monsterpatch CatchBottleRates Patcher] ";

        private const string HelperNamespace = "Monsterpatch.CatchBottleRates";
        private const string HelperName = "CatchBottleRatesRuntime";

        public static IEnumerable<string> TargetDLLs
        {
            get { return new[] { "Assembly-CSharp.dll" }; }
        }

        public static void Initialize()
        {
            Log("v0.1.0 final house-affinity preloader patcher initialized.");
        }

        public static void Finish()
        {
            Log("Finished.");
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException("assembly");

            ModuleDefinition module = assembly.MainModule;
            TypeDefinition gameScript = FindType(module, "GameScript");
            if (gameScript == null)
                throw new InvalidOperationException("Could not find GameScript in Assembly-CSharp.dll.");

            TypeDefinition mon = FindType(module, "Mon");
            TypeDefinition monScriptableObject = FindType(module, "MonScriptableObject");
            if (mon == null)
                throw new InvalidOperationException("Could not find Mon in Assembly-CSharp.dll.");
            if (monScriptableObject == null)
                throw new InvalidOperationException("Could not find MonScriptableObject in Assembly-CSharp.dll.");

            RequiredGameRefs refs = ResolveGameRefs(module, gameScript, mon, monScriptableObject);
            TypeDefinition helper = InjectRuntimeHelper(module, refs);
            MethodDefinition applyHouseBonus = FindMethod(helper, "ApplyHouseBonusToChance");
            MethodDefinition refreshPreview = FindMethod(helper, "RefreshPreview");

            int patchedMethods = 0;
            if (PatchCatchCoroutine(gameScript, applyHouseBonus))
                patchedMethods++;

            if (InjectPreviewPostfix(gameScript, "RefreshMonNamePreview", refreshPreview))
                patchedMethods++;

            if (InjectPreviewPostfix(gameScript, "RefreshBottlePanel", refreshPreview))
                patchedMethods++;

            // ChangeBottle already calls RefreshBottlePanel in the current decompile, but this extra postfix keeps
            // the mod safe if another patcher/game update changes that call order.
            if (InjectPreviewPostfix(gameScript, "ChangeBottle", refreshPreview))
                patchedMethods++;

            if (patchedMethods < 3)
                throw new InvalidOperationException("Catch bottle patch did not patch enough methods. Patched count: " + patchedMethods);

            Log("Patched Assembly-CSharp.dll. Final house-affinity catch bonuses and immediate preview refresh are active.");
        }

        private static RequiredGameRefs ResolveGameRefs(ModuleDefinition module, TypeDefinition gameScript, TypeDefinition mon, TypeDefinition monScriptableObject)
        {
            RequiredGameRefs refs = new RequiredGameRefs();
            refs.GameScriptType = gameScript;
            refs.MonType = mon;
            refs.MonScriptableObjectType = monScriptableObject;

            refs.CurBottleIdField = FindField(gameScript, "curBottleId");
            refs.CurInteractingMonField = FindField(gameScript, "curInteractingMon");
            refs.TxtPreviewCatchDifficultyField = FindField(gameScript, "txtPreviewCatchDifficulty");
            refs.SetColorMethod = FindMethod(gameScript, "SetColor");
            refs.MonScriptableObjectField = FindField(mon, "monScriptableObject");
            refs.IsShinyField = FindField(mon, "isShiny");
            refs.MonIdField = FindField(monScriptableObject, "id");
            refs.CatchDifficultyField = FindField(monScriptableObject, "catchDifficulty");
            refs.HousesField = FindField(monScriptableObject, "houses");
            refs.HousesShinyField = FindField(monScriptableObject, "housesShiny");
            refs.TmpTextSetTextMethod = FindTmpTextSetTextMethod(gameScript);

            if (refs.CurBottleIdField == null)
                throw new MissingFieldException("GameScript.curBottleId");
            if (refs.CurInteractingMonField == null)
                throw new MissingFieldException("GameScript.curInteractingMon");
            if (refs.TxtPreviewCatchDifficultyField == null)
                throw new MissingFieldException("GameScript.txtPreviewCatchDifficulty");
            if (refs.SetColorMethod == null)
                throw new MissingMethodException("GameScript.SetColor");
            if (refs.MonScriptableObjectField == null)
                throw new MissingFieldException("Mon.monScriptableObject");
            if (refs.IsShinyField == null)
                throw new MissingFieldException("Mon.isShiny");
            if (refs.MonIdField == null)
                throw new MissingFieldException("MonScriptableObject.id");
            if (refs.CatchDifficultyField == null)
                throw new MissingFieldException("MonScriptableObject.catchDifficulty");
            if (refs.HousesField == null)
                throw new MissingFieldException("MonScriptableObject.houses");
            if (refs.HousesShinyField == null)
                throw new MissingFieldException("MonScriptableObject.housesShiny");
            if (refs.TmpTextSetTextMethod == null)
                throw new MissingMethodException("Could not find TMP_Text.set_text(string) reference in GameScript.RefreshMonNamePreview.");

            refs.GameScriptRef = module.ImportReference(gameScript);
            refs.MonRef = module.ImportReference(mon);
            refs.MonScriptableObjectRef = module.ImportReference(monScriptableObject);
            refs.HouseArrayRef = module.ImportReference(refs.HousesField.FieldType);
            refs.TmpTextSetTextRef = module.ImportReference(refs.TmpTextSetTextMethod);
            refs.SetColorRef = module.ImportReference(refs.SetColorMethod);

            return refs;
        }

        private static TypeDefinition InjectRuntimeHelper(ModuleDefinition module, RequiredGameRefs refs)
        {
            TypeDefinition existing = module.Types.FirstOrDefault(t => t.Namespace == HelperNamespace && t.Name == HelperName);
            if (existing != null)
                module.Types.Remove(existing);

            TypeDefinition helper = new TypeDefinition(
                HelperNamespace,
                HelperName,
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);

            module.Types.Add(helper);

            MethodDefinition clamp = AddClampMethod(module, helper);
            MethodDefinition isBottleHouseMatch = AddIsBottleHouseMatchMethod(module, helper);
            MethodDefinition getHouseMatchCount = AddGetHouseAffinityMatchCountMethod(module, helper, refs, isBottleHouseMatch);
            MethodDefinition getBonus = AddGetHouseBonusForMatchCountMethod(module, helper);
            MethodDefinition calculateFinalChance = AddCalculateFinalChanceMethod(module, helper, refs, getHouseMatchCount, getBonus, clamp);
            MethodDefinition getBaseChance = AddGetBaseChanceForDifficultyMethod(module, helper);
            MethodDefinition getDifficultyIndex = AddGetDifficultyIndexForFinalChanceMethod(module, helper);
            MethodDefinition getDifficultyName = AddGetDifficultyNameMethod(module, helper);
            AddApplyHouseBonusToChanceMethod(module, helper, refs, calculateFinalChance, clamp);
            AddRefreshPreviewMethod(module, helper, refs, getBaseChance, calculateFinalChance, getDifficultyIndex, getDifficultyName);

            Log("Injected runtime helper: " + HelperNamespace + "." + HelperName);
            return helper;
        }

        private static MethodDefinition AddClampMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("Clamp", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("min", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("max", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction checkMax = Instruction.Create(OpCodes.Ldarg_0);
            Instruction returnValue = Instruction.Create(OpCodes.Ldarg_0);

            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Bge, checkMax));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ret));

            il.Append(checkMax);
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Ble, returnValue));
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Ret));

            il.Append(returnValue);
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static MethodDefinition AddIsBottleHouseMatchMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("IsBottleHouseMatch", module.TypeSystem.Boolean);
            method.Parameters.Add(new ParameterDefinition("bottleId", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("house", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction returnTrue = Instruction.Create(OpCodes.Ldc_I4_1);

            // House enum:
            // 0 FIREBORN, 1 ATLANTIAN, 2 OVERGROWTH, 3 WHIMSICAL, 4 NIGHTWATCH,
            // 5 BRAWLER, 6 DRAGOON, 7 MYSTIC, 8 IRONCLAD, 9 NONE
            AppendBottleHouseMatchCase(il, 109, 1, 8, returnTrue); // Diamond Bottle: ATLANTIAN, IRONCLAD
            AppendBottleHouseMatchCase(il, 110, 3, 7, returnTrue); // Heart Bottle: WHIMSICAL, MYSTIC
            AppendBottleHouseMatchCase(il, 111, 0, 5, returnTrue); // Guild Bottle: FIREBORN, BRAWLER
            AppendBottleHouseMatchCase(il, 112, 4, 6, returnTrue); // Void Bottle: NIGHTWATCH, DRAGOON
            AppendBottleHouseMatchCase(il, 113, 2, 0, returnTrue); // Spice Bottle: OVERGROWTH, FIREBORN

            il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
            il.Append(Instruction.Create(OpCodes.Ret));
            il.Append(returnTrue);
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static void AppendBottleHouseMatchCase(ILProcessor il, int bottleId, int houseA, int houseB, Instruction returnTrue)
        {
            Instruction next = Instruction.Create(OpCodes.Nop);
            Instruction checkHouseB = Instruction.Create(OpCodes.Ldarg_1);

            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(LoadInt(bottleId));
            il.Append(Instruction.Create(OpCodes.Bne_Un, next));

            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(LoadInt(houseA));
            il.Append(Instruction.Create(OpCodes.Beq, returnTrue));

            il.Append(checkHouseB);
            il.Append(LoadInt(houseB));
            il.Append(Instruction.Create(OpCodes.Beq, returnTrue));

            il.Append(next);
        }

        private static MethodDefinition AddGetHouseAffinityMatchCountMethod(ModuleDefinition module, TypeDefinition helper, RequiredGameRefs refs, MethodDefinition isBottleHouseMatch)
        {
            MethodDefinition method = NewStaticMethod("GetHouseAffinityMatchCount", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("bottleId", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("mon", ParameterAttributes.None, refs.MonRef));
            method.Body.InitLocals = true;
            method.Body.Variables.Add(new VariableDefinition(refs.MonScriptableObjectRef));
            method.Body.Variables.Add(new VariableDefinition(refs.HouseArrayRef));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction retZero = Instruction.Create(OpCodes.Ldc_I4_0);
            Instruction afterBasicBottleCheck = Instruction.Create(OpCodes.Ldarg_1);
            Instruction useNormalHousesAfterShinyCheck = Instruction.Create(OpCodes.Ldloc_1);
            Instruction housesNullCheck = Instruction.Create(OpCodes.Ldloc_1);
            Instruction initLoop = Instruction.Create(OpCodes.Ldc_I4_0);
            Instruction loopCheck = Instruction.Create(OpCodes.Ldloc_3);
            Instruction loopBody = Instruction.Create(OpCodes.Ldloc_1);
            Instruction noMatch = Instruction.Create(OpCodes.Ldloc_3);
            Instruction afterLoop = Instruction.Create(OpCodes.Ldloc_2);
            Instruction returnTwo = Instruction.Create(OpCodes.Ldc_I4_2);

            // Basic bottle has no affinity.
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(LoadInt(108));
            il.Append(Instruction.Create(OpCodes.Bne_Un, afterBasicBottleCheck));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
            il.Append(Instruction.Create(OpCodes.Ret));

            // if (mon == null) return 0;
            il.Append(afterBasicBottleCheck);
            il.Append(Instruction.Create(OpCodes.Brfalse, retZero));

            // mso = mon.monScriptableObject; if (mso == null) return 0;
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.MonScriptableObjectField));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Brfalse, retZero));

            // houses = mso.houses;
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.HousesField));
            il.Append(Instruction.Create(OpCodes.Stloc_1));

            // if (mon.isShiny && mso.housesShiny != null && mso.housesShiny.Length > 0) houses = mso.housesShiny;
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.IsShinyField));
            il.Append(Instruction.Create(OpCodes.Brfalse, useNormalHousesAfterShinyCheck));

            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.HousesShinyField));
            il.Append(Instruction.Create(OpCodes.Brfalse, useNormalHousesAfterShinyCheck));

            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.HousesShinyField));
            il.Append(Instruction.Create(OpCodes.Ldlen));
            il.Append(Instruction.Create(OpCodes.Conv_I4));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
            il.Append(Instruction.Create(OpCodes.Ble, useNormalHousesAfterShinyCheck));

            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.HousesShinyField));
            il.Append(Instruction.Create(OpCodes.Stloc_1));

            // if (houses == null) return 0;
            il.Append(useNormalHousesAfterShinyCheck);
            il.Append(Instruction.Create(OpCodes.Brfalse, retZero));

            // count = 0; i = 0;
            il.Append(initLoop);
            il.Append(Instruction.Create(OpCodes.Stloc_2));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
            il.Append(Instruction.Create(OpCodes.Stloc_3));
            il.Append(Instruction.Create(OpCodes.Br, loopCheck));

            // house = (int)houses[i];
            il.Append(loopBody);
            il.Append(Instruction.Create(OpCodes.Ldloc_3));
            il.Append(Instruction.Create(OpCodes.Ldelem_I4));
            il.Append(Instruction.Create(OpCodes.Stloc, method.Body.Variables[4]));

            // if (house == NONE) skip;
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[4]));
            il.Append(LoadInt(9));
            il.Append(Instruction.Create(OpCodes.Beq, noMatch));

            // if (!IsBottleHouseMatch(bottleId, house)) skip;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[4]));
            il.Append(Instruction.Create(OpCodes.Call, isBottleHouseMatch));
            il.Append(Instruction.Create(OpCodes.Brfalse, noMatch));

            // count++;
            il.Append(Instruction.Create(OpCodes.Ldloc_2));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(Instruction.Create(OpCodes.Add));
            il.Append(Instruction.Create(OpCodes.Stloc_2));

            // if (count >= 2) return 2;
            il.Append(Instruction.Create(OpCodes.Ldloc_2));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
            il.Append(Instruction.Create(OpCodes.Bge, returnTwo));

            // i++;
            il.Append(noMatch);
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(Instruction.Create(OpCodes.Add));
            il.Append(Instruction.Create(OpCodes.Stloc_3));

            // while (i < houses.Length)
            il.Append(loopCheck);
            il.Append(Instruction.Create(OpCodes.Ldloc_1));
            il.Append(Instruction.Create(OpCodes.Ldlen));
            il.Append(Instruction.Create(OpCodes.Conv_I4));
            il.Append(Instruction.Create(OpCodes.Blt, loopBody));

            // return count;
            il.Append(afterLoop);
            il.Append(Instruction.Create(OpCodes.Ret));

            il.Append(returnTwo);
            il.Append(Instruction.Create(OpCodes.Ret));

            il.Append(retZero);
            il.Append(Instruction.Create(OpCodes.Ret));

            method.Body.MaxStackSize = 4;
            return method;
        }

        private static MethodDefinition AddGetHouseBonusForMatchCountMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("GetHouseBonusForMatchCount", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("matchCount", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction checkSingle = Instruction.Create(OpCodes.Ldarg_0);
            Instruction returnZero = Instruction.Create(OpCodes.Ldc_I4_0);

            // if matchCount >= 2 return 25;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
            il.Append(Instruction.Create(OpCodes.Blt, checkSingle));
            il.Append(LoadInt(25));
            il.Append(Instruction.Create(OpCodes.Ret));

            // if matchCount >= 1 return 15;
            il.Append(checkSingle);
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(Instruction.Create(OpCodes.Blt, returnZero));
            il.Append(LoadInt(15));
            il.Append(Instruction.Create(OpCodes.Ret));

            // return 0;
            il.Append(returnZero);
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static MethodDefinition AddCalculateFinalChanceMethod(
            ModuleDefinition module,
            TypeDefinition helper,
            RequiredGameRefs refs,
            MethodDefinition getHouseMatchCount,
            MethodDefinition getBonus,
            MethodDefinition clamp)
        {
            MethodDefinition method = NewStaticMethod("CalculateFinalChance", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("baseChance", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("bottleId", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("mon", ParameterAttributes.None, refs.MonRef));
            method.Body.InitLocals = true;
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            // matchCount = GetHouseAffinityMatchCount(bottleId, mon);
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Call, getHouseMatchCount));
            il.Append(Instruction.Create(OpCodes.Stloc_0));

            // adjusted = baseChance + GetHouseBonusForMatchCount(matchCount);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Call, getBonus));
            il.Append(Instruction.Create(OpCodes.Add));
            il.Append(Instruction.Create(OpCodes.Stloc_1));

            // return Clamp(adjusted, 1, 95)
            il.Append(Instruction.Create(OpCodes.Ldloc_1));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(LoadInt(95));
            il.Append(Instruction.Create(OpCodes.Call, clamp));
            il.Append(Instruction.Create(OpCodes.Ret));
            method.Body.MaxStackSize = 4;
            return method;
        }

        private static MethodDefinition AddGetBaseChanceForDifficultyMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("GetBaseChanceForDifficulty", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("difficulty", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            AppendIntReturnCase(il, 0, 90);
            AppendIntReturnCase(il, 1, 60);
            AppendIntReturnCase(il, 2, 30);
            il.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10));
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static MethodDefinition AddGetDifficultyIndexForFinalChanceMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("GetDifficultyIndexForFinalChance", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("finalChance", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction checkMedium = Instruction.Create(OpCodes.Ldarg_0);
            Instruction checkHard = Instruction.Create(OpCodes.Ldarg_0);
            Instruction returnVeryHard = Instruction.Create(OpCodes.Ldc_I4_3);

            // if finalChance >= 90 return 0;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)90));
            il.Append(Instruction.Create(OpCodes.Blt, checkMedium));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
            il.Append(Instruction.Create(OpCodes.Ret));

            // if finalChance >= 60 return 1;
            il.Append(checkMedium);
            il.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)60));
            il.Append(Instruction.Create(OpCodes.Blt, checkHard));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(Instruction.Create(OpCodes.Ret));

            // if finalChance >= 30 return 2;
            il.Append(checkHard);
            il.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)30));
            il.Append(Instruction.Create(OpCodes.Blt, returnVeryHard));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
            il.Append(Instruction.Create(OpCodes.Ret));

            // return 3;
            il.Append(returnVeryHard);
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static MethodDefinition AddGetDifficultyNameMethod(ModuleDefinition module, TypeDefinition helper)
        {
            MethodDefinition method = NewStaticMethod("GetDifficultyName", module.TypeSystem.String);
            method.Parameters.Add(new ParameterDefinition("difficulty", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            AppendStringReturnCase(il, 0, "EASY");
            AppendStringReturnCase(il, 1, "MEDIUM");
            AppendStringReturnCase(il, 2, "HARD");
            il.Append(Instruction.Create(OpCodes.Ldstr, "VERY HARD"));
            il.Append(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static void AppendIntReturnCase(ILProcessor il, int difficulty, int value)
        {
            Instruction next = Instruction.Create(OpCodes.Nop);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(LoadInt(difficulty));
            il.Append(Instruction.Create(OpCodes.Bne_Un, next));
            il.Append(LoadInt(value));
            il.Append(Instruction.Create(OpCodes.Ret));
            il.Append(next);
        }

        private static void AppendStringReturnCase(ILProcessor il, int difficulty, string value)
        {
            Instruction next = Instruction.Create(OpCodes.Nop);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(LoadInt(difficulty));
            il.Append(Instruction.Create(OpCodes.Bne_Un, next));
            il.Append(Instruction.Create(OpCodes.Ldstr, value));
            il.Append(Instruction.Create(OpCodes.Ret));
            il.Append(next);
        }

        private static MethodDefinition AddApplyHouseBonusToChanceMethod(
            ModuleDefinition module,
            TypeDefinition helper,
            RequiredGameRefs refs,
            MethodDefinition calculateFinalChance,
            MethodDefinition clamp)
        {
            MethodDefinition method = NewStaticMethod("ApplyHouseBonusToChance", module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition("gameScript", ParameterAttributes.None, refs.GameScriptRef));
            method.Parameters.Add(new ParameterDefinition("vanillaBaseChance", ParameterAttributes.None, module.TypeSystem.Int32));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction hasGameScript = Instruction.Create(OpCodes.Ldarg_1);

            // if (gameScript == null) return Clamp(vanillaBaseChance, 1, 100);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Brtrue, hasGameScript));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(LoadInt(100));
            il.Append(Instruction.Create(OpCodes.Call, clamp));
            il.Append(Instruction.Create(OpCodes.Ret));

            // return CalculateFinalChance(vanillaBaseChance, gameScript.curBottleId, gameScript.curInteractingMon);
            il.Append(hasGameScript);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.CurBottleIdField));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.CurInteractingMonField));
            il.Append(Instruction.Create(OpCodes.Call, calculateFinalChance));
            il.Append(Instruction.Create(OpCodes.Ret));
            method.Body.MaxStackSize = 4;
            return method;
        }

        private static MethodDefinition AddRefreshPreviewMethod(
            ModuleDefinition module,
            TypeDefinition helper,
            RequiredGameRefs refs,
            MethodDefinition getBaseChance,
            MethodDefinition calculateFinalChance,
            MethodDefinition getDifficultyIndex,
            MethodDefinition getDifficultyName)
        {
            MethodDefinition method = NewStaticMethod("RefreshPreview", module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("gameScript", ParameterAttributes.None, refs.GameScriptRef));
            method.Body.InitLocals = true;
            method.Body.Variables.Add(new VariableDefinition(refs.MonRef));
            method.Body.Variables.Add(new VariableDefinition(refs.MonScriptableObjectRef));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            helper.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction ret = Instruction.Create(OpCodes.Ret);

            // if (gameScript == null) return;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Brfalse, ret));

            // mon = gameScript.curInteractingMon; if (mon == null) return;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.CurInteractingMonField));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Brfalse, ret));

            // monScriptableObject = mon.monScriptableObject; if (monScriptableObject == null) return;
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.MonScriptableObjectField));
            il.Append(Instruction.Create(OpCodes.Stloc_1));
            il.Append(Instruction.Create(OpCodes.Ldloc_1));
            il.Append(Instruction.Create(OpCodes.Brfalse, ret));

            // if (gameScript.txtPreviewCatchDifficulty == null) return;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.TxtPreviewCatchDifficultyField));
            il.Append(Instruction.Create(OpCodes.Brfalse, ret));

            // difficulty = monScriptableObject.catchDifficulty;
            il.Append(Instruction.Create(OpCodes.Ldloc_1));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.CatchDifficultyField));
            il.Append(Instruction.Create(OpCodes.Stloc_2));

            // baseChance = GetBaseChanceForDifficulty(difficulty);
            il.Append(Instruction.Create(OpCodes.Ldloc_2));
            il.Append(Instruction.Create(OpCodes.Call, getBaseChance));
            il.Append(Instruction.Create(OpCodes.Stloc_3));

            // finalChance = CalculateFinalChance(baseChance, gameScript.curBottleId, mon);
            il.Append(Instruction.Create(OpCodes.Ldloc_3));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.CurBottleIdField));
            il.Append(Instruction.Create(OpCodes.Ldloc_0));
            il.Append(Instruction.Create(OpCodes.Call, calculateFinalChance));
            il.Append(Instruction.Create(OpCodes.Stloc, method.Body.Variables[4]));

            // finalDifficulty = GetDifficultyIndexForFinalChance(finalChance);
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[4]));
            il.Append(Instruction.Create(OpCodes.Call, getDifficultyIndex));
            il.Append(Instruction.Create(OpCodes.Stloc, method.Body.Variables[5]));

            // text = GetDifficultyName(finalDifficulty);
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[5]));
            il.Append(Instruction.Create(OpCodes.Call, getDifficultyName));
            il.Append(Instruction.Create(OpCodes.Stloc, method.Body.Variables[6]));

            // gameScript.txtPreviewCatchDifficulty.text = text;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, refs.TxtPreviewCatchDifficultyField));
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[6]));
            il.Append(Instruction.Create(OpCodes.Callvirt, refs.TmpTextSetTextRef));

            // gameScript.SetColor(finalDifficulty);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldloc, method.Body.Variables[5]));
            il.Append(Instruction.Create(OpCodes.Call, refs.SetColorRef));

            il.Append(ret);
            method.Body.MaxStackSize = 4;
            return method;
        }

        private static MethodDefinition NewStaticMethod(string name, TypeReference returnType)
        {
            return new MethodDefinition(
                name,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                returnType);
        }

        private static bool PatchCatchCoroutine(TypeDefinition gameScript, MethodDefinition applyHouseBonus)
        {
            TypeDefinition catchStateMachine = gameScript.NestedTypes.FirstOrDefault(t => t.Name.IndexOf("TryToCatchWildMon2", StringComparison.OrdinalIgnoreCase) >= 0);
            if (catchStateMachine == null)
                throw new MissingMethodException("Could not find GameScript.<TryToCatchWildMon2> coroutine state machine.");

            MethodDefinition moveNext = catchStateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
            if (moveNext == null || !moveNext.HasBody)
                throw new MissingMethodException("Could not find GameScript.<TryToCatchWildMon2>.MoveNext body.");

            FieldDefinition gameScriptField = catchStateMachine.Fields.FirstOrDefault(f => f.Name == "<>4__this");
            FieldDefinition chanceField = catchStateMachine.Fields.FirstOrDefault(f => f.FieldType.FullName == "System.Int32" && f.Name.IndexOf("<chance>", StringComparison.OrdinalIgnoreCase) >= 0);
            FieldDefinition rollField = catchStateMachine.Fields.FirstOrDefault(f => f.FieldType.FullName == "System.Int32" && f.Name.IndexOf("<num>", StringComparison.OrdinalIgnoreCase) >= 0);

            if (gameScriptField == null)
                throw new MissingFieldException(catchStateMachine.FullName + ".<>4__this");
            if (chanceField == null)
                throw new MissingFieldException(catchStateMachine.FullName + ".<chance>");
            if (rollField == null)
                throw new MissingFieldException(catchStateMachine.FullName + ".<num>");

            IList<Instruction> instructions = moveNext.Body.Instructions;
            for (int i = 0; i <= instructions.Count - 5; i++)
            {
                bool pattern =
                    IsLoadArg0(instructions[i]) &&
                    instructions[i + 1].OpCode == OpCodes.Ldfld && SameField(instructions[i + 1].Operand, rollField) &&
                    IsLoadArg0(instructions[i + 2]) &&
                    instructions[i + 3].OpCode == OpCodes.Ldfld && SameField(instructions[i + 3].Operand, chanceField) &&
                    instructions[i + 4].OpCode == OpCodes.Clt;

                if (!pattern)
                    continue;

                Instruction target = instructions[i];
                ILProcessor il = moveNext.Body.GetILProcessor();

                List<Instruction> patch = new List<Instruction>
                {
                    // this.<chance> = CatchBottleRatesRuntime.ApplyHouseBonusToChance(this.<>4__this, this.<chance>);
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, gameScriptField),
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, chanceField),
                    Instruction.Create(OpCodes.Call, applyHouseBonus),
                    Instruction.Create(OpCodes.Stfld, chanceField)
                };

                RedirectBranches(moveNext.Body, target, patch[0]);

                for (int p = 0; p < patch.Count; p++)
                    il.InsertBefore(target, patch[p]);

                moveNext.Body.MaxStackSize = Math.Max(moveNext.Body.MaxStackSize, 4);
                Log("Patched real catch roll in " + catchStateMachine.Name + ".MoveNext.");
                return true;
            }

            throw new InvalidOperationException("Could not find the catch comparison pattern in TryToCatchWildMon2.MoveNext.");
        }

        private static bool InjectPreviewPostfix(TypeDefinition gameScript, string methodName, MethodDefinition refreshPreview)
        {
            MethodDefinition method = gameScript.Methods.FirstOrDefault(m => m.Name == methodName && !m.HasParameters);
            if (method == null || !method.HasBody)
            {
                Log("Skipped " + methodName + ": method not found.");
                return false;
            }

            if (method.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.FullName == refreshPreview.FullName))
            {
                Log(methodName + " already contains preview postfix.");
                return false;
            }

            List<Instruction> returns = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            if (returns.Count == 0)
                throw new InvalidOperationException("Could not find return instruction in GameScript." + methodName + ".");

            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction ret in returns)
            {
                il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, Instruction.Create(OpCodes.Call, refreshPreview));
            }

            method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 4);
            Log("Patched preview refresh postfix in GameScript." + methodName + ".");
            return true;
        }

        private static void RedirectBranches(MethodBody body, Instruction oldTarget, Instruction newTarget)
        {
            foreach (Instruction instruction in body.Instructions)
            {
                if (instruction.Operand == oldTarget)
                {
                    instruction.Operand = newTarget;
                    continue;
                }

                Instruction[] switchTargets = instruction.Operand as Instruction[];
                if (switchTargets == null)
                    continue;

                for (int i = 0; i < switchTargets.Length; i++)
                {
                    if (switchTargets[i] == oldTarget)
                        switchTargets[i] = newTarget;
                }
            }
        }

        private static bool IsLoadArg0(Instruction instruction)
        {
            if (instruction.OpCode == OpCodes.Ldarg_0)
                return true;

            if (instruction.OpCode == OpCodes.Ldarg && instruction.Operand is ParameterDefinition p)
                return p.Index == 0;

            return false;
        }

        private static bool SameField(object operand, FieldDefinition expected)
        {
            FieldReference field = operand as FieldReference;
            return field != null && field.FullName == expected.FullName;
        }

        private static Instruction LoadInt(int value)
        {
            switch (value)
            {
                case -1:
                    return Instruction.Create(OpCodes.Ldc_I4_M1);
                case 0:
                    return Instruction.Create(OpCodes.Ldc_I4_0);
                case 1:
                    return Instruction.Create(OpCodes.Ldc_I4_1);
                case 2:
                    return Instruction.Create(OpCodes.Ldc_I4_2);
                case 3:
                    return Instruction.Create(OpCodes.Ldc_I4_3);
                case 4:
                    return Instruction.Create(OpCodes.Ldc_I4_4);
                case 5:
                    return Instruction.Create(OpCodes.Ldc_I4_5);
                case 6:
                    return Instruction.Create(OpCodes.Ldc_I4_6);
                case 7:
                    return Instruction.Create(OpCodes.Ldc_I4_7);
                case 8:
                    return Instruction.Create(OpCodes.Ldc_I4_8);
                default:
                    if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                        return Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)value);
                    return Instruction.Create(OpCodes.Ldc_I4, value);
            }
        }

        private static TypeDefinition FindType(ModuleDefinition module, string name)
        {
            return module.Types.FirstOrDefault(t => t.Name == name);
        }

        private static FieldDefinition FindField(TypeDefinition type, string name)
        {
            return type.Fields.FirstOrDefault(f => f.Name == name);
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string name)
        {
            return type.Methods.FirstOrDefault(m => m.Name == name);
        }

        private static MethodReference FindTmpTextSetTextMethod(TypeDefinition gameScript)
        {
            MethodDefinition refresh = FindMethod(gameScript, "RefreshMonNamePreview");
            if (refresh == null || !refresh.HasBody)
                return null;

            foreach (Instruction instruction in refresh.Body.Instructions)
            {
                MethodReference method = instruction.Operand as MethodReference;
                if (method == null)
                    continue;

                if (method.Name == "set_text" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.String")
                    return method;
            }

            return null;
        }

        private static void Log(string message)
        {
            Console.WriteLine(LogPrefix + message);
        }

        private sealed class RequiredGameRefs
        {
            public TypeDefinition GameScriptType;
            public TypeDefinition MonType;
            public TypeDefinition MonScriptableObjectType;

            public TypeReference GameScriptRef;
            public TypeReference MonRef;
            public TypeReference MonScriptableObjectRef;
            public TypeReference HouseArrayRef;

            public FieldDefinition CurBottleIdField;
            public FieldDefinition CurInteractingMonField;
            public FieldDefinition TxtPreviewCatchDifficultyField;
            public FieldDefinition MonScriptableObjectField;
            public FieldDefinition IsShinyField;
            public FieldDefinition MonIdField;
            public FieldDefinition CatchDifficultyField;
            public FieldDefinition HousesField;
            public FieldDefinition HousesShinyField;

            public MethodDefinition SetColorMethod;
            public MethodReference SetColorRef;
            public MethodReference TmpTextSetTextMethod;
            public MethodReference TmpTextSetTextRef;
        }
    }
}
