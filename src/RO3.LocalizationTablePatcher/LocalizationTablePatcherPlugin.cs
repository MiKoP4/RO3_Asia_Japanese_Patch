using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RO3.JapaneseMod
{
    [BepInPlugin("com.ro3.localizationtablepatcher", "RO3 Localization Table Patcher", "2.5.1")]
    public sealed class LocalizationTablePatcherPlugin : BaseUnityPlugin
    {
        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T value)
            {
                return value == null ? 0 : RuntimeHelpers.GetHashCode(value);
            }
        }

        private sealed class Entry
        {
            public string Id;
            public string English;
            public string Japanese;
        }

        private enum KeyMode
        {
            Unknown,
            String,
            Int64,
            Int32,
            Double,
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<long, Entry> _entriesById = new Dictionary<long, Entry>();
        private static LocalizationTablePatcherPlugin _current;
        private bool _done;
        private bool _insideAttempt;
        private string _mapPath;
        private string _lastRetryStatus;
        private SynchronizationContext _unityContext;
        private System.Threading.Timer _retryTimer;
        private bool _localizationLoadObserved;
        private int _postLocalizationLogAttempts;
        private Harmony _harmony;
        private object _directLocalizationTable;
        private object _capturedLuaEnv;
        private bool _loggedLocalizationTableDiagnostics;
        private bool _loggedHarmonyPatchDiagnostics;
        private bool _loggedDelegateBridgeListDiagnostics;
        private bool _loggedLanguageMainTableDiagnostics;
        private bool _loggedLanguageMainFunctionDiagnostics;
        private bool _loggedLanguageMainCacheStable;
        private int _languageMainTimerAttempts;
        private static int _gameLoopPostfixHits;
        private static int _luaUpdatePostfixHits;
        private static int _requirePostfixHits;
        private static int _luaInitializePostfixHits;
        private static int _luaBridgeBindPostfixHits;
        private static int _luaEnvGetterPostfixHits;
        private static int _gameBootstrapPostfixHits;
        private static int _languageLookupOverrideHits;
        private static int _languageLookupPostfixHits;
        private static int _languageCacheResetPostfixHits;
        private int _mainThreadRetryHits;
        private int _timerPostFailures;
        private int _awakeThreadId;
        private int _liveProbeAttempts;
        private readonly HashSet<Assembly> _patchedGameAssemblies =
            new HashSet<Assembly>(ReferenceComparer<Assembly>.Instance);
        private readonly HashSet<MethodBase> _patchedMethods =
            new HashSet<MethodBase>(ReferenceComparer<MethodBase>.Instance);
        private readonly HashSet<object> _knownLuaEnvs =
            new HashSet<object>(ReferenceComparer<object>.Instance);
        private readonly HashSet<object> _loggedLuaEnvsBeforeLocalization =
            new HashSet<object>(ReferenceComparer<object>.Instance);
        private readonly HashSet<object> _loggedLuaEnvsAfterLocalization =
            new HashSet<object>(ReferenceComparer<object>.Instance);
        private readonly HashSet<object> _patchedLocalizationTables =
            new HashSet<object>(ReferenceComparer<object>.Instance);

        private void Awake()
        {
            _mapPath = Path.Combine(Paths.ConfigPath, "RO3.LocalizationOverrides.tsv");
            LoadEntries();
            _current = this;
            _unityContext = SynchronizationContext.Current;
            _awakeThreadId = Thread.CurrentThread.ManagedThreadId;

            InstallRequireHook();
            SeedLanguageMainCache("Awake");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.logMessageReceived += OnLogMessageReceived;

            if (_unityContext != null)
            {
                _retryTimer = new System.Threading.Timer(
                    QueueMainThreadRetry,
                    null,
                    1000,
                    1000);
            }

            Logger.LogInfo(
                "[LocalizationTablePatcher] Ready. Entries=" + _entries.Count +
                ", map=" + _mapPath +
                ", syncContext=" +
                (_unityContext == null ? "<null>" : _unityContext.GetType().FullName) +
                ", awakeThread=" + _awakeThreadId);
            // Do NOT touch LA_LuaManager.Instance here.  RO3's singleton getter
            // creates a GameObject when no instance exists, so resolving it from
            // BepInEx.Awake races the game's own Lua bootstrap and can leave us
            // holding an uninitialized manager.  Retry events below only inspect
            // an already-created Unity object.
        }

        private void OnDestroy()
        {
            UnsubscribeRetryEvents();
            if (_harmony != null)
            {
                try
                {
                    _harmony.UnpatchSelf();
                }
                catch
                {
                }
                _harmony = null;
            }
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }

        private void InstallRequireHook()
        {
            try
            {
                _harmony = new Harmony("com.ro3.localizationtablepatcher.lifecycle");

                Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
                int candidates = 0;
                foreach (Assembly assembly in loaded)
                {
                    if (!IsRelevantRuntimeAssembly(assembly))
                    {
                        continue;
                    }
                    candidates++;
                    PatchGameAssembly(assembly, "Awake-enumeration");
                }
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Relevant runtime assembly copies visible at Awake=" +
                    candidates + ". AppDomain.AssemblyLoad monitoring is intentionally disabled because " +
                    "RO3's Mono profile throws MissingMethodException for AssemblyLoadEventArgs.LoadedAssembly.");
                LogHarmonyPatchDiagnostics();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[LocalizationTablePatcher] Lifecycle hook setup failed: " + ex);
                _harmony = null;
            }
        }

        private static bool IsRelevantRuntimeAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                return false;
            }
            try
            {
                if (string.Equals(
                        assembly.GetName().Name,
                        "Assembly-CSharp",
                        StringComparison.Ordinal))
                {
                    return true;
                }

                string[] relevantTypes =
                {
                    "LA_LuaManager",
                    "LanguageMain",
                    "Obf_Zb",
                    "Obf_o",
                    "LA_LuaComponent",
                    "XLua.DelegateBridge",
                };
                foreach (string typeName in relevantTypes)
                {
                    if (assembly.GetType(typeName, false) != null)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void PatchGameAssembly(Assembly assembly, string source)
        {
            if (assembly == null || _harmony == null)
            {
                return;
            }

            lock (_patchedGameAssemblies)
            {
                if (_patchedGameAssemblies.Contains(assembly))
                {
                    return;
                }
                _patchedGameAssemblies.Add(assembly);
            }

            Logger.LogInfo(
                "[LocalizationTablePatcher][diag] Patching game assembly copy, source=" + source +
                ", " + DescribeAssembly(assembly) +
                ", relevantTypes=" + DescribeRelevantTypes(assembly) + ".");

            Type managerType = assembly.GetType("LA_LuaManager", false);
            Type gameLoopType = assembly.GetType("Obf_Zb", false);
            Type luaUpdateType = assembly.GetType("Obf_o", false);
            Type languageMainType = assembly.GetType("LanguageMain", false);

            MethodInfo require = managerType == null ? null : managerType.GetMethod(
                "Obf_MD",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            MethodInfo initializeLua = FindMethodByNameAndArity(managerType, "Obf_iD", 2);
            MethodInfo luaEnvGetter = managerType == null ? null : managerType.GetMethod(
                "Obf_jD",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo gameBootstrap = FindMethodByNameAndArity(gameLoopType, "Obf_au", 3);
            MethodInfo gameLoop = gameLoopType == null ? null : gameLoopType.GetMethod(
                "Obf_Cu",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo luaBridgeBind = FindMethodByNameAndArity(luaUpdateType, "Obf_he", 1);
            MethodInfo luaUpdate = luaUpdateType == null ? null : luaUpdateType.GetMethod(
                "Obf_ie",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo languageLookup = languageMainType == null ? null : languageMainType.GetMethod(
                "Obf_gO",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(long) },
                null);
            MethodInfo languageCacheReset = languageMainType == null ? null : languageMainType.GetMethod(
                "Obf_FO",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            string suffix = " [asm=" + GetAssemblyToken(assembly) + "]";
            if (managerType != null)
            {
                TryInstallPostfix(require, "RequirePostfix", "LA_LuaManager.Obf_MD" + suffix);
                TryInstallPostfix(initializeLua, "LuaInitializePostfix", "LA_LuaManager.Obf_iD" + suffix);
                TryInstallPostfix(luaEnvGetter, "LuaEnvGetterPostfix", "LA_LuaManager.Obf_jD" + suffix);
            }
            if (gameLoopType != null)
            {
                TryInstallPostfix(gameBootstrap, "GameBootstrapPostfix", "Obf_Zb.Obf_au" + suffix);
                TryInstallPostfix(gameLoop, "GameLoopPostfix", "Obf_Zb.Obf_Cu" + suffix);
            }
            if (luaUpdateType != null)
            {
                TryInstallPostfix(luaBridgeBind, "LuaBridgeBindPostfix", "Obf_o.Obf_he" + suffix);
                TryInstallPostfix(luaUpdate, "LuaUpdatePostfix", "Obf_o.Obf_ie" + suffix);
            }
            if (languageMainType != null)
            {
                TryInstallPostfix(languageLookup, "LanguageLookupPostfix", "LanguageMain.Obf_gO" + suffix);
                TryInstallPostfix(languageCacheReset, "LanguageCacheResetPostfix", "LanguageMain.Obf_FO" + suffix);
            }
        }

        private static MethodInfo FindMethodByNameAndArity(Type type, string name, int arity)
        {
            if (type == null)
            {
                return null;
            }
            foreach (MethodInfo candidate in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (candidate.Name == name && candidate.GetParameters().Length == arity)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string DescribeAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                return "<null assembly>";
            }
            string location;
            try
            {
                location = assembly.Location;
            }
            catch
            {
                location = "<no-location>";
            }
            string mvid;
            try
            {
                mvid = assembly.ManifestModule.ModuleVersionId.ToString("D");
            }
            catch
            {
                mvid = "<no-mvid>";
            }
            return
                "name=" + assembly.FullName +
                ", token=" + GetAssemblyToken(assembly) +
                ", mvid=" + mvid +
                ", location=" + location;
        }

        private static string GetAssemblyToken(Assembly assembly)
        {
            return assembly == null
                ? "null"
                : RuntimeHelpers.GetHashCode(assembly).ToString("X8", CultureInfo.InvariantCulture);
        }

        private static string DescribeRelevantTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                return "<none>";
            }
            string[] names =
            {
                "LA_LuaManager",
                "LanguageMain",
                "Obf_Zb",
                "Obf_o",
                "LA_LuaComponent",
                "XLua.DelegateBridge",
            };
            List<string> found = new List<string>();
            foreach (string name in names)
            {
                try
                {
                    if (assembly.GetType(name, false) != null)
                    {
                        found.Add(name);
                    }
                }
                catch
                {
                }
            }
            return found.Count == 0 ? "<none>" : string.Join(",", found.ToArray());
        }

        private static string DescribeMethod(MethodBase method)
        {
            if (method == null)
            {
                return "<null method>";
            }
            string mvid;
            try
            {
                mvid = method.Module.ModuleVersionId.ToString("D");
            }
            catch
            {
                mvid = "<no-mvid>";
            }
            string metadataToken;
            try
            {
                metadataToken = method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture);
            }
            catch
            {
                metadataToken = "<no-token>";
            }
            return
                (method.DeclaringType == null ? "<no-type>" : method.DeclaringType.FullName) + "." +
                method.Name +
                " methodRef=" + RuntimeHelpers.GetHashCode(method).ToString("X8", CultureInfo.InvariantCulture) +
                ", metadataToken=" + metadataToken +
                ", mvid=" + mvid +
                ", asm=" + GetAssemblyToken(method.Module.Assembly);
        }

        private void TryInstallPostfix(MethodInfo target, string callbackName, string label)
        {
            if (target == null)
            {
                Logger.LogWarning("[LocalizationTablePatcher][diag] Missing hook target: " + label + ".");
                return;
            }

            MethodInfo callback = typeof(LocalizationTablePatcherPlugin).GetMethod(
                callbackName,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (callback == null)
            {
                Logger.LogWarning("[LocalizationTablePatcher][diag] Missing callback: " + callbackName + ".");
                return;
            }

            try
            {
                lock (_patchedMethods)
                {
                    if (_patchedMethods.Contains(target))
                    {
                        Logger.LogInfo(
                            "[LocalizationTablePatcher][diag] Hook already registered by MethodBase identity: " +
                            label + " -> " + DescribeMethod(target) + ".");
                        return;
                    }
                    _patchedMethods.Add(target);
                }
                _harmony.Patch(target, postfix: new HarmonyMethod(callback));
                Patches patchInfo = Harmony.GetPatchInfo(target);
                bool ownerPresent = false;
                int postfixCount = 0;
                if (patchInfo != null && patchInfo.Postfixes != null)
                {
                    postfixCount = patchInfo.Postfixes.Count;
                    foreach (Patch patch in patchInfo.Postfixes)
                    {
                        if (string.Equals(patch.owner, _harmony.Id, StringComparison.Ordinal))
                        {
                            ownerPresent = true;
                            break;
                        }
                    }
                }
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Hook installed: " + label +
                    " -> " + callbackName +
                    ", ownerPresent=" + ownerPresent +
                    ", postfixCount=" + postfixCount +
                    ", target=" + DescribeMethod(target) + ".");
            }
            catch (Exception ex)
            {
                lock (_patchedMethods)
                {
                    _patchedMethods.Remove(target);
                }
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] Hook install failed: " + label +
                    " -> " + callbackName + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void RequirePostfix(object __instance, string __0, object[] __result, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            int hit = System.Threading.Interlocked.Increment(ref _requirePostfixHits);
            bool isNameplateModule = IsNameplateDiagnosticModule(__0);
            if (plugin != null && (hit <= 12 || isNameplateModule))
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Obf_MD postfix hit=" + hit +
                    " module=" + (__0 ?? "<null>") +
                    " result=" + DescribeObjectArray(__result) +
                    " target=" + DescribeMethod(__originalMethod) + ".");
            }

            if (plugin == null)
            {
                return;
            }

            bool isLocalizationModule = string.Equals(__0, "Localization_en", StringComparison.Ordinal);
            if (isLocalizationModule)
            {
                // Even a previously known LuaEnv can reload Localization_en.
                // Re-open the all-copy probe for that concrete module load.
                plugin._done = false;
            }

            object managerLuaEnv = GetInstanceField(__instance, __instance == null ? null : __instance.GetType(), "Obf_Dc");
            if (managerLuaEnv != null)
            {
                plugin.CaptureLuaEnv(managerLuaEnv, "Obf_MD-postfix:" + (__0 ?? "<null>"));
            }

            if (isNameplateModule)
            {
                plugin._done = false;
                plugin.LogNameplateModuleDiagnostics(__0, __instance, __result, __originalMethod);
                plugin.ProbeAndPatchAll("nameplate-require:" + __0);
            }

            if (plugin._done || plugin._insideAttempt || !isLocalizationModule)
            {
                return;
            }

            plugin.TryPatch("Obf_MD(Localization_en)-postfix", __instance, __result);
            plugin.ProbeAndPatchAll("Obf_MD(Localization_en)-postfix-all-copy");
        }

        private static bool IsNameplateDiagnosticModule(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName))
            {
                return false;
            }
            return string.Equals(
                       moduleName,
                       "Logic/MeshUI/LC_MeshUI_NamePlate",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       moduleName,
                       "Logic/MeshUI/LC_MeshUI_NamePlateTop",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       moduleName,
                       "Utility/Lua_MeshUI_Utils",
                       StringComparison.Ordinal) ||
                   moduleName.EndsWith("/LC_MeshUI_NamePlate", StringComparison.Ordinal) ||
                   moduleName.EndsWith("/LC_MeshUI_NamePlateTop", StringComparison.Ordinal) ||
                   moduleName.EndsWith("/Lua_MeshUI_Utils", StringComparison.Ordinal);
        }

        private void LogNameplateModuleDiagnostics(
            string moduleName,
            object manager,
            object[] requireResult,
            MethodBase originalMethod)
        {
            try
            {
                object table = FindLuaTable(requireResult);
                string lookupDetail = "require result";
                if (table == null && manager != null)
                {
                    table = FindLoadedModuleTable(
                        manager,
                        manager.GetType(),
                        moduleName,
                        out lookupDetail);
                }

                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] MeshUI module loaded: module=" + moduleName +
                    ", table=" + TypeName(table) +
                    (table == null
                        ? string.Empty
                        : ", tableRef=" + RuntimeHelpers.GetHashCode(table).ToString("X8", CultureInfo.InvariantCulture) +
                          ", tableAsm=" + GetAssemblyToken(table.GetType().Assembly)) +
                    ", lookup=" + lookupDetail +
                    ", requireTarget=" + DescribeMethod(originalMethod) + ".");

                if (table == null)
                {
                    return;
                }

                LogLuaTableKeySummary(table, "MeshUI module " + moduleName, 192);

                int nestedLogged = 0;
                foreach (object key in EnumerateLuaTableKeys(table, 192))
                {
                    object value = GetLuaTableObjectValue(table, key);
                    if (!IsLuaTable(value))
                    {
                        continue;
                    }
                    LogLuaTableKeySummary(
                        value,
                        "MeshUI module " + moduleName + " nested[" + DescribeValue(key) + "]",
                        64);
                    nestedLogged++;
                    if (nestedLogged >= 12)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] MeshUI module diagnostics failed for " +
                    (moduleName ?? "<null>") + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void LuaInitializePostfix(object __instance, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            int hit = System.Threading.Interlocked.Increment(ref _luaInitializePostfixHits);
            if (plugin != null && hit == 1)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Obf_iD postfix entered. instance=" +
                    TypeName(__instance) +
                    ", target=" + DescribeMethod(__originalMethod) + ".");
            }
            if (plugin == null)
            {
                return;
            }

            object luaEnv = GetInstanceField(__instance, __instance == null ? null : __instance.GetType(), "Obf_Dc");
            plugin.CaptureLuaEnv(luaEnv, "Obf_iD-postfix");
            if (plugin._done || plugin._insideAttempt)
            {
                return;
            }

            // The original Obf_iD has just created the Lua environment and
            // registered RO3's custom loader.  At this point calling require is
            // safe and ensures Localization_en is patched before game Lua starts
            // consuming it for MeshUI/world-space nameplates.
            plugin.TryPatch("Obf_iD-LuaEnv-ready");
            plugin.ProbeAndPatchAll("Obf_iD-LuaEnv-ready-all-copy");
        }

        private static void LuaEnvGetterPostfix(object __result, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            int hit = System.Threading.Interlocked.Increment(ref _luaEnvGetterPostfixHits);
            if (plugin != null && hit <= 3)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Obf_jD postfix hit=" + hit +
                    " result=" + TypeName(__result) +
                    ", target=" + DescribeMethod(__originalMethod) + ".");
            }
            if (plugin != null)
            {
                plugin.CaptureLuaEnv(__result, "Obf_jD-postfix");
                plugin.ProbeAndPatchAll("Obf_jD-postfix-all-copy");
            }
        }

        private static void GameBootstrapPostfix(object __instance, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            int hit = System.Threading.Interlocked.Increment(ref _gameBootstrapPostfixHits);
            if (plugin != null && hit == 1)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Obf_Zb.Obf_au postfix entered. instance=" +
                    TypeName(__instance) +
                    ", target=" + DescribeMethod(__originalMethod) + ".");
            }
            if (plugin == null || plugin._done || plugin._insideAttempt)
            {
                return;
            }

            string detail;
            object table = FindLocalizationTableFromGameLoop(__instance, out detail);
            if (table != null)
            {
                plugin._directLocalizationTable = table;
                plugin.TryPatch("Obf_Zb.Obf_au-postfix-live-bridge");
                plugin.ProbeAndPatchAll("Obf_Zb.Obf_au-postfix-all-copy");
                return;
            }

            plugin.LogRetryStatus(detail, "Obf_Zb.Obf_au-postfix-live-bridge");
        }

        private static void LuaBridgeBindPostfix(object __instance, object __0, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            int hit = System.Threading.Interlocked.Increment(ref _luaBridgeBindPostfixHits);
            if (plugin != null && hit <= 3)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Obf_o.Obf_he postfix hit=" + hit +
                    " luaUpdate=" + TypeName(__instance) +
                    " luaEnvArg=" + TypeName(__0) +
                    ", target=" + DescribeMethod(__originalMethod) + ".");
            }
            if (plugin != null)
            {
                plugin.CaptureLuaEnv(__0, "Obf_o.Obf_he-postfix");
                plugin.ProbeAndPatchAll("Obf_o.Obf_he-postfix-all-copy");
            }
        }

        private static void LanguageLookupPostfix(long __0, ref string __result, MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            if (plugin == null)
            {
                return;
            }

            int invocationHit = System.Threading.Interlocked.Increment(ref _languageLookupPostfixHits);
            if (invocationHit <= 6)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] LanguageMain.Obf_gO postfix entered hit=" +
                    invocationHit +
                    ", id=" + __0.ToString(CultureInfo.InvariantCulture) +
                    ", target=" + DescribeMethod(__originalMethod) + ".");
            }

            Entry entry;
            if (!plugin._entriesById.TryGetValue(__0, out entry))
            {
                return;
            }

            string before = __result;
            __result = entry.Japanese;
            int hit = System.Threading.Interlocked.Increment(ref _languageLookupOverrideHits);
            if (hit <= 12)
            {
                plugin.Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] LanguageMain.Obf_gO override hit=" + hit +
                    " id=" + __0.ToString(CultureInfo.InvariantCulture) +
                    " before=" + DescribeValue(before) +
                    " after=" + DescribeValue(__result) + ".");
            }
        }

        private static void LanguageCacheResetPostfix(MethodBase __originalMethod)
        {
            LocalizationTablePatcherPlugin plugin = _current;
            if (plugin == null)
            {
                return;
            }

            int hit = System.Threading.Interlocked.Increment(ref _languageCacheResetPostfixHits);
            plugin.Logger.LogInfo(
                "[LocalizationTablePatcher][diag] LanguageMain.Obf_FO postfix hit=" + hit +
                "; reseeding ID cache; target=" + DescribeMethod(__originalMethod) + ".");
            plugin.SeedLanguageMainCache("LanguageMain.Obf_FO-postfix");
            plugin.ProbeAndPatchAll("LanguageMain.Obf_FO-postfix-all-copy");
        }

        private static void GameLoopPostfix(object __instance, MethodBase __originalMethod)
        {
            int hit = System.Threading.Interlocked.Increment(ref _gameLoopPostfixHits);
            if (hit == 1)
            {
                UnityEngine.Debug.Log(
                    "[LocalizationTablePatcher] GameLoopPostfix entered. target=" +
                    DescribeMethod(__originalMethod) + ".");
            }
            LocalizationTablePatcherPlugin plugin = _current;
            if (plugin == null || plugin._done || plugin._insideAttempt)
            {
                return;
            }

            string detail;
            object table = FindLocalizationTableFromGameLoop(__instance, out detail);
            if (table == null)
            {
                plugin.LogRetryStatus(detail, "Obf_Zb.Obf_Cu-live-bridge");
                return;
            }

            plugin._directLocalizationTable = table;
            plugin.TryPatch("Obf_Zb.Obf_Cu-live-bridge");
            plugin.ProbeAndPatchAll("Obf_Zb.Obf_Cu-all-copy");
        }

        private static void LuaUpdatePostfix(object __instance, MethodBase __originalMethod)
        {
            int hit = System.Threading.Interlocked.Increment(ref _luaUpdatePostfixHits);
            if (hit == 1)
            {
                UnityEngine.Debug.Log(
                    "[LocalizationTablePatcher] LuaUpdatePostfix entered. target=" +
                    DescribeMethod(__originalMethod) + ".");
            }
            LocalizationTablePatcherPlugin plugin = _current;
            if (plugin == null || plugin._done || plugin._insideAttempt)
            {
                return;
            }

            string detail;
            object table = FindLocalizationTableFromLuaUpdate(__instance, out detail);
            if (table == null)
            {
                plugin.LogRetryStatus(detail, "Obf_o.Obf_ie-live-bridge");
                return;
            }

            plugin._directLocalizationTable = table;
            plugin.TryPatch("Obf_o.Obf_ie-live-bridge");
            plugin.ProbeAndPatchAll("Obf_o.Obf_ie-all-copy");
        }

        private void QueueMainThreadRetry(object state)
        {
            if (_done)
            {
                return;
            }
            try
            {
                if (ThreadingHelper.Instance != null)
                {
                    ThreadingHelper.Instance.StartSyncInvoke(MainThreadRetry);
                    return;
                }
                if (_unityContext != null)
                {
                    _unityContext.Post(_ => MainThreadRetry(), null);
                    return;
                }
                throw new InvalidOperationException("no main-thread dispatcher is available");
            }
            catch (Exception ex)
            {
                _timerPostFailures++;
                if (_timerPostFailures <= 3)
                {
                    Logger.LogWarning(
                        "[LocalizationTablePatcher][diag] UnitySynchronizationContext.Post failed #" +
                        _timerPostFailures + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private void MainThreadRetry()
        {
            if (_done)
            {
                return;
            }

            _mainThreadRetryHits++;
            if (_mainThreadRetryHits <= 3)
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] MainThreadRetry entered hit=" +
                    _mainThreadRetryHits +
                    ", thread=" + Thread.CurrentThread.ManagedThreadId +
                    ", awakeThread=" + _awakeThreadId + ".");
            }

            _languageMainTimerAttempts++;
            ProbeAndPatchAll("timer#" + _languageMainTimerAttempts);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ProbeAndPatchAll("sceneLoaded:" + scene.name);
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            ProbeAndPatchAll("activeSceneChanged:" + next.name);
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return;
            }

            // Recovery logs Localization_en while its require() call is still
            // loading the bytecode.  Do not re-enter require from that callback.
            // The next recovered Lua module is loaded only after Localization_en
            // has returned, so it is a reliable main-thread initialization edge.
            if (condition.IndexOf(
                    "[LuaRecovery] module=Localization_en ",
                    StringComparison.Ordinal) >= 0)
            {
                _done = false;
                _localizationLoadObserved = true;
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Observed Recovery load for Localization_en; " +
                    "waiting for the next Lua module before reading package.loaded.");
                return;
            }


            if (_done)
            {
                return;
            }

            if (!_localizationLoadObserved || _insideAttempt)
            {
                return;
            }

            if (condition.IndexOf("[LuaRecovery] module=", StringComparison.Ordinal) < 0)
            {
                return;
            }

            _postLocalizationLogAttempts++;
            if (_postLocalizationLogAttempts <= 32)
            {
                int attempt = _postLocalizationLogAttempts;
                if (_capturedLuaEnv != null)
                {
                    LogLuaEnvSnapshot(
                        _capturedLuaEnv,
                        "post-Localization_en-log#" + attempt,
                        true);
                }
                string source = "post-Localization_en-log#" + attempt;
                if (ThreadingHelper.Instance != null)
                {
                    // Recovery logging occurs inside Lua's module loader. Defer
                    // LuaTable reads until BepInEx's next main-thread Update so
                    // we never re-enter Lua while require() is still on the stack.
                    ThreadingHelper.Instance.StartSyncInvoke(() => ProbeAndPatchAll(source));
                }
                else
                {
                    ProbeAndPatchAll(source);
                }
            }
        }

        private void ProbeAndPatchAll(string source)
        {
            if (_done || _insideAttempt || _entries.Count == 0)
            {
                return;
            }

            _liveProbeAttempts++;
            EnsureLanguageMainCacheSeeded(source);

            List<object> envs = new List<object>();
            HashSet<object> envSeen = new HashSet<object>(ReferenceComparer<object>.Instance);
            if (_capturedLuaEnv != null && envSeen.Add(_capturedLuaEnv))
            {
                envs.Add(_capturedLuaEnv);
            }

            string languageEnvDetail;
            List<object> languageEnvs = FindLuaEnvsFromLanguageMain(out languageEnvDetail);
            foreach (object env in languageEnvs)
            {
                if (env != null && envSeen.Add(env))
                {
                    envs.Add(env);
                }
            }
            if (languageEnvs.Count > 0 && !_loggedLanguageMainFunctionDiagnostics)
            {
                _loggedLanguageMainFunctionDiagnostics = true;
                LogCurrentCSharpTransProbes("pre-all-copy-table-patch");
            }

            string componentDetail;
            foreach (object env in FindLuaEnvsFromLuaComponents(out componentDetail))
            {
                if (env != null && envSeen.Add(env))
                {
                    envs.Add(env);
                }
            }

            string bridgeDetail;
            foreach (object env in FindLuaEnvsFromDelegateBridgeLists(out bridgeDetail))
            {
                if (env != null && envSeen.Add(env))
                {
                    envs.Add(env);
                }
            }

            List<string> managerDetails = new List<string>();
            foreach (Type managerType in FindTypes("LA_LuaManager"))
            {
                string token = GetAssemblyToken(managerType.Assembly);
                try
                {
                    object manager = FindExistingLuaManager(managerType);
                    if (manager == null)
                    {
                        managerDetails.Add("asm=" + token + ": manager=<null>");
                        continue;
                    }
                    object env = GetInstanceField(manager, manager.GetType(), "Obf_Dc");
                    managerDetails.Add(
                        "asm=" + token +
                        ": manager=" + TypeName(manager) +
                        ", env=" + TypeName(env) +
                        (env == null
                            ? string.Empty
                            : ", envRef=" + RuntimeHelpers.GetHashCode(env).ToString("X8", CultureInfo.InvariantCulture)));
                    if (env != null && envSeen.Add(env))
                    {
                        envs.Add(env);
                    }
                }
                catch (Exception ex)
                {
                    managerDetails.Add(
                        "asm=" + token + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            int unresolvedEnvs = 0;
            HashSet<object> tableSeen = new HashSet<object>(ReferenceComparer<object>.Instance);
            List<string> envResults = new List<string>();
            for (int i = 0; i < envs.Count; i++)
            {
                object env = envs[i];
                CaptureLuaEnv(env, "all-copy-probe:" + source + "#" + i);

                string envDetail;
                object table = FindLoadedModuleTableFromLuaEnv(
                    env,
                    "Localization_en",
                    out envDetail);
                if (table == null)
                {
                    unresolvedEnvs++;
                    envResults.Add(
                        "envRef=" + RuntimeHelpers.GetHashCode(env).ToString("X8", CultureInfo.InvariantCulture) +
                        ": unresolved: " + envDetail);
                    continue;
                }

                envResults.Add(
                    "envRef=" + RuntimeHelpers.GetHashCode(env).ToString("X8", CultureInfo.InvariantCulture) +
                    ": tableRef=" + RuntimeHelpers.GetHashCode(table).ToString("X8", CultureInfo.InvariantCulture) +
                    ", " + envDetail);
                if (!tableSeen.Add(table))
                {
                    continue;
                }

                _directLocalizationTable = table;
                TryPatch("all-copy-LuaEnv-" + source + "#" + i);
            }

            if (envs.Count == 0)
            {
                _directLocalizationTable = null;
                TryPatch("fallback-manager-" + source);
            }

            int patchedTableCount;
            lock (_patchedLocalizationTables)
            {
                patchedTableCount = _patchedLocalizationTables.Count;
            }

            bool shouldLog = _liveProbeAttempts <= 12 || _liveProbeAttempts % 30 == 0;
            if (shouldLog)
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Combined all-copy probe attempt=" +
                    _liveProbeAttempts +
                    ", source=" + source +
                    ", envs=" + envs.Count +
                    ", uniqueTables=" + tableSeen.Count +
                    ", patchedTables=" + patchedTableCount +
                    ", unresolvedEnvs=" + unresolvedEnvs +
                    "; LanguageMain={" + languageEnvDetail +
                    "}; LA_LuaComponent={" + componentDetail +
                    "}; DelegateBridge={" + bridgeDetail +
                    "}; Managers={" + string.Join(" | ", managerDetails.ToArray()) +
                    "}; EnvResults={" + string.Join(" | ", envResults.ToArray()) + "}.");
            }

            // Once the currently live LuaEnv set has been enumerated a few times
            // and at least one localization table is patched, frame-level hooks no
            // longer need to scan every update. A newly observed LuaEnv or a
            // Localization_en reload re-opens _done above.
            if (_liveProbeAttempts >= 3 && patchedTableCount > 0 && tableSeen.Count > 0)
            {
                _done = true;
                _lastRetryStatus = null;
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] All-copy probe stabilized. " +
                    "envs=" + envs.Count +
                    ", uniqueTables=" + tableSeen.Count +
                    ", patchedTables=" + patchedTableCount +
                    ", unresolvedEnvs=" + unresolvedEnvs +
                    ", knownLuaEnvs=" + _knownLuaEnvs.Count +
                    ", source=" + source + ".");
            }
        }

        private void UnsubscribeRetryEvents()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Application.logMessageReceived -= OnLogMessageReceived;
            if (_retryTimer != null)
            {
                _retryTimer.Dispose();
                _retryTimer = null;
            }
        }

        private void LoadEntries()
        {
            _entries.Clear();
            _entriesById.Clear();
            if (!File.Exists(_mapPath))
            {
                Logger.LogError("[LocalizationTablePatcher] Map not found: " + _mapPath);
                return;
            }

            foreach (string rawLine in File.ReadAllLines(_mapPath))
            {
                string line = rawLine.TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length != 3 ||
                    string.IsNullOrEmpty(parts[0]) ||
                    string.IsNullOrEmpty(parts[1]) ||
                    string.IsNullOrEmpty(parts[2]) ||
                    parts[1] == parts[2])
                {
                    continue;
                }

                Entry entry = new Entry
                {
                    Id = parts[0],
                    English = parts[1],
                    Japanese = parts[2],
                };
                _entries.Add(entry);

                long id;
                if (long.TryParse(entry.Id, NumberStyles.None, CultureInfo.InvariantCulture, out id))
                {
                    _entriesById[id] = entry;
                }
            }
        }

        private void SeedLanguageMainCache(string source)
        {
            try
            {
                List<Type> languageMainTypes = FindTypes("LanguageMain");
                if (languageMainTypes.Count == 0)
                {
                    Logger.LogInfo(
                        "[LocalizationTablePatcher][diag] LanguageMain cache seed deferred; type unavailable, source=" +
                        source + ".");
                    return;
                }

                int copies = 0;
                int changed = 0;
                int already = 0;
                int totalEntries = 0;
                foreach (Type languageMainType in languageMainTypes)
                {
                    FieldInfo cacheField = languageMainType.GetField(
                        "Obf_Pk",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    Dictionary<long, string> cache = cacheField == null
                        ? null
                        : cacheField.GetValue(null) as Dictionary<long, string>;
                    if (cache == null)
                    {
                        continue;
                    }

                    copies++;
                    foreach (KeyValuePair<long, Entry> pair in _entriesById)
                    {
                        string current;
                        if (cache.TryGetValue(pair.Key, out current) &&
                            string.Equals(current, pair.Value.Japanese, StringComparison.Ordinal))
                        {
                            already++;
                            continue;
                        }
                        cache[pair.Key] = pair.Value.Japanese;
                        changed++;
                    }
                    totalEntries += cache.Count;
                }

                Logger.LogInfo(
                    "[LocalizationTablePatcher] Seeded LanguageMain ID cache copies=" + copies +
                    ", changed=" + changed +
                    ", already=" + already +
                    ", totalEntries=" + totalEntries +
                    ", source=" + source + ".");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] LanguageMain cache seed failed at " + source +
                    ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void EnsureLanguageMainCacheSeeded(string source)
        {
            try
            {
                List<Type> languageMainTypes = FindTypes("LanguageMain");
                if (languageMainTypes.Count == 0)
                {
                    return;
                }
                Entry probeEntry;
                const long probeId = 10530000045L; // Piere
                bool probeMapped = _entriesById.TryGetValue(probeId, out probeEntry);
                bool needsSeed = false;
                int cacheCopies = 0;
                int totalCount = 0;
                string firstProbe = null;
                foreach (Type languageMainType in languageMainTypes)
                {
                    FieldInfo cacheField = languageMainType.GetField(
                        "Obf_Pk",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    Dictionary<long, string> cache = cacheField == null
                        ? null
                        : cacheField.GetValue(null) as Dictionary<long, string>;
                    if (cache == null)
                    {
                        continue;
                    }

                    cacheCopies++;
                    totalCount += cache.Count;
                    string current = null;
                    bool probeCorrect = probeMapped &&
                        cache.TryGetValue(probeId, out current) &&
                        string.Equals(current, probeEntry.Japanese, StringComparison.Ordinal);
                    if (firstProbe == null)
                    {
                        firstProbe = DescribeValue(current);
                    }
                    if (!probeCorrect || cache.Count < _entriesById.Count)
                    {
                        needsSeed = true;
                    }
                }

                if (cacheCopies == 0)
                {
                    return;
                }

                // LanguageMain.Obf_FO() clears this dictionary during the real
                // language bootstrap. Refill only when that actually happened
                // (or when our probe was replaced), rather than rewriting 8k
                // entries every second.
                if (needsSeed)
                {
                    SeedLanguageMainCache(source);
                    _loggedLanguageMainCacheStable = false;
                    return;
                }

                if (!_loggedLanguageMainCacheStable)
                {
                    _loggedLanguageMainCacheStable = true;
                    Logger.LogInfo(
                        "[LocalizationTablePatcher][diag] LanguageMain cache is stable after bootstrap. " +
                        "copies=" + cacheCopies +
                        ", totalCount=" + totalCount +
                        ", probe=" + probeId + "=" + firstProbe +
                        ", source=" + source + ".");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] LanguageMain cache stability check failed at " + source +
                    ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private List<object> FindLuaEnvsFromLanguageMain(out string detail)
        {
            List<Type> languageMainTypes = FindTypes("LanguageMain");
            List<object> envs = new List<object>();
            HashSet<object> seen = new HashSet<object>(ReferenceComparer<object>.Instance);
            List<string> copyDetails = new List<string>();
            if (languageMainTypes.Count == 0)
            {
                detail = "LanguageMain type unavailable";
                return envs;
            }

            foreach (Type languageMainType in languageMainTypes)
            {
                string token = GetAssemblyToken(languageMainType.Assembly);
                try
                {
                    FieldInfo csharpTransField = languageMainType.GetField(
                        "Obf_pk",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object csharpTrans = csharpTransField == null ? null : csharpTransField.GetValue(null);
                    if (csharpTrans == null)
                    {
                        copyDetails.Add("asm=" + token + ": Obf_pk=<null>");
                        continue;
                    }

                    object luaEnv = GetInstanceField(csharpTrans, csharpTrans.GetType(), "Obf_blA");
                    copyDetails.Add(
                        "asm=" + token +
                        ": function=" + TypeName(csharpTrans) +
                        ", functionRef=" + RuntimeHelpers.GetHashCode(csharpTrans).ToString("X8", CultureInfo.InvariantCulture) +
                        ", luaEnv=" + TypeName(luaEnv) +
                        (luaEnv == null
                            ? string.Empty
                            : ", envRef=" + RuntimeHelpers.GetHashCode(luaEnv).ToString("X8", CultureInfo.InvariantCulture)));
                    if (luaEnv != null && seen.Add(luaEnv))
                    {
                        envs.Add(luaEnv);
                    }
                }
                catch (Exception ex)
                {
                    copyDetails.Add(
                        "asm=" + token + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            detail =
                "copies=" + languageMainTypes.Count +
                ", uniqueEnvs=" + envs.Count +
                ", " + string.Join(" | ", copyDetails.ToArray());
            return envs;
        }

        private object FindLocalizationTableFromLanguageMain(out string detail)
        {
            List<Type> languageMainTypes = FindTypes("LanguageMain");
            if (languageMainTypes.Count == 0)
            {
                detail = "LanguageMain type unavailable";
                return null;
            }

            List<string> failures = new List<string>();
            foreach (Type languageMainType in languageMainTypes)
            {
                string token = GetAssemblyToken(languageMainType.Assembly);
                FieldInfo csharpTransField = languageMainType.GetField(
                    "Obf_pk",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object csharpTrans = csharpTransField == null ? null : csharpTransField.GetValue(null);
                if (csharpTrans == null)
                {
                    failures.Add("asm=" + token + ": Obf_pk=<null>");
                    continue;
                }

                object luaEnv = GetInstanceField(csharpTrans, csharpTrans.GetType(), "Obf_blA");
                if (!_loggedLanguageMainFunctionDiagnostics)
                {
                    _loggedLanguageMainFunctionDiagnostics = true;
                    Logger.LogInfo(
                        "[LocalizationTablePatcher][diag] LanguageMain.Obf_pk(CSharpTrans) found in asm=" +
                        token + ": type=" + TypeName(csharpTrans) +
                        ", ref=" + DescribeValue(GetInstanceField(csharpTrans, csharpTrans.GetType(), "Obf_AlA")) +
                        ", luaEnv=" + TypeName(luaEnv) + ".");
                    LogCSharpTransProbes(csharpTrans, "pre-patch");
                }

                if (luaEnv == null)
                {
                    failures.Add("asm=" + token + ": CSharpTrans LuaEnv=<null>");
                    continue;
                }

                CaptureLuaEnv(luaEnv, "LanguageMain.Obf_pk(CSharpTrans),asm=" + token);
                object languageTable = GetGlobalTableFromLuaEnv(luaEnv, "Language");
                if (!_loggedLanguageMainTableDiagnostics && languageTable != null)
                {
                    _loggedLanguageMainTableDiagnostics = true;
                    LogLuaTableKeySummary(languageTable, "Lua globals.Language asm=" + token, 96);
                }

                string envDetail;
                object module = FindLoadedModuleTableFromLuaEnv(
                    luaEnv,
                    "Localization_en",
                    out envDetail);
                if (module != null)
                {
                    detail = "asm=" + token + " via CSharpTrans LuaEnv -> " + envDetail;
                    return module;
                }

                string mode;
                if (TableLooksLikeLocalization(languageTable, out mode))
                {
                    detail = "asm=" + token + " globals.Language direct table, keyMode=" + mode;
                    return languageTable;
                }

                string nestedDetail;
                object nested = FindLocalizationTableRecursively(
                    languageTable,
                    "globals.Language asm=" + token,
                    4,
                    256,
                    out nestedDetail);
                if (nested != null)
                {
                    detail = nestedDetail;
                    return nested;
                }
                failures.Add(
                    "asm=" + token + ": env found, Localization_en unresolved: " +
                    envDetail + "; Language scan: " + nestedDetail);
            }

            detail = string.Join(" | ", failures.ToArray());
            return null;
        }

        private void TryPatch(string trigger)
        {
            TryPatch(trigger, null, null);
        }

        private void TryPatch(string trigger, object knownManager, object[] knownRequireResult)
        {
            if (_insideAttempt || _entries.Count == 0)
            {
                return;
            }

            _insideAttempt = true;
            try
            {
                object table = _directLocalizationTable;
                if (_capturedLuaEnv == null)
                {
                    string bridgeDetail;
                    object bridgeLuaEnv = FindLuaEnvFromDelegateBridgeList(out bridgeDetail);
                    if (bridgeLuaEnv != null)
                    {
                        CaptureLuaEnv(bridgeLuaEnv, "XLua.DelegateBridgeList");
                    }
                    else if (!_loggedDelegateBridgeListDiagnostics)
                    {
                        _loggedDelegateBridgeListDiagnostics = true;
                        Logger.LogInfo(
                            "[LocalizationTablePatcher][diag] DelegateBridgeList LuaEnv probe: " +
                            bridgeDetail + ".");
                    }
                }
                if (table == null && _capturedLuaEnv != null)
                {
                    string capturedDetail;
                    table = FindLoadedModuleTableFromLuaEnv(
                        _capturedLuaEnv,
                        "Localization_en",
                        out capturedDetail);
                    if (table != null)
                    {
                        Logger.LogInfo(
                            "[LocalizationTablePatcher][diag] Localization_en resolved from captured LuaEnv via " +
                            capturedDetail + ", trigger=" + trigger + ".");
                    }
                    else if (_localizationLoadObserved)
                    {
                        LogRetryStatus(
                            "captured LuaEnv could not resolve Localization_en: " + capturedDetail,
                            trigger);
                    }
                }
                if (table == null)
                {
                    List<Type> managerTypes = knownManager != null
                        ? new List<Type> { knownManager.GetType() }
                        : FindTypes("LA_LuaManager");
                    if (managerTypes.Count == 0)
                    {
                        LogRetryStatus("LA_LuaManager type is not loaded yet", trigger);
                        return;
                    }

                    List<string> managerFailures = new List<string>();
                    foreach (Type managerType in managerTypes)
                    {
                        string token = GetAssemblyToken(managerType.Assembly);
                        object manager = knownManager ?? FindExistingLuaManager(managerType);
                        if (manager == null)
                        {
                            managerFailures.Add("asm=" + token + ": existing manager not found");
                            continue;
                        }

                        MethodInfo require = managerType.GetMethod(
                            "Obf_MD",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new[] { typeof(string) },
                            null);
                        if (require == null)
                        {
                            managerFailures.Add("asm=" + token + ": Obf_MD(string) missing");
                            continue;
                        }

                        object[] required = knownManager != null
                            ? knownRequireResult
                            : require.Invoke(manager, new object[] { "Localization_en" }) as object[];
                        table = FindLuaTable(required);
                        string lookupDetail = "require result";
                        if (table == null)
                        {
                            table = FindLoadedModuleTable(
                                manager,
                                managerType,
                                "Localization_en",
                                out lookupDetail);
                        }
                        if (table != null)
                        {
                            Logger.LogInfo(
                                "[LocalizationTablePatcher][diag] Manager fallback resolved Localization_en from " +
                                "asm=" + token + ", trigger=" + trigger + ", via=" + lookupDetail + ".");
                            break;
                        }

                        string requireStatus = required == null
                            ? "null"
                            : required.Length + " value(s)";
                        managerFailures.Add(
                            "asm=" + token + ": require=" + requireStatus + ", " + lookupDetail);
                    }

                    if (table == null)
                    {
                        LogRetryStatus(
                            "Localization_en table was not available across LA_LuaManager copies: " +
                            string.Join(" | ", managerFailures.ToArray()),
                            trigger);
                        return;
                    }
                }

                lock (_patchedLocalizationTables)
                {
                    if (_patchedLocalizationTables.Contains(table))
                    {
                        return;
                    }
                }

                Type tableType = table.GetType();
                MethodInfo stringGetter = tableType.GetMethod(
                    "Obf_MuA",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);
                MethodInfo stringSetter = tableType.GetMethod(
                    "Obf_nuA",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(object) },
                    null);
                MethodInfo objectGetter = tableType.GetMethod(
                    "Obf_NuA",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(object) },
                    null);
                MethodInfo objectSetter = tableType.GetMethod(
                    "Obf_ouA",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(object), typeof(object) },
                    null);

                if (stringGetter == null || stringSetter == null || objectGetter == null || objectSetter == null)
                {
                    Logger.LogError(
                        "[LocalizationTablePatcher] Required LuaTable getter/setter methods are missing on " +
                        tableType.FullName + ".");
                    return;
                }

                LogLocalizationTableDiagnostics(
                    table,
                    stringGetter,
                    objectGetter,
                    trigger);

                KeyMode mode = DetectKeyMode(table, stringGetter, objectGetter);
                if (mode == KeyMode.Unknown)
                {
                    Logger.LogWarning(
                        "[LocalizationTablePatcher] Localization_en loaded but no map ID matched its " +
                        "expected English/Japanese value yet; retrying.");
                    return;
                }

                int patched = 0;
                int already = 0;
                int missing = 0;
                int mismatch = 0;
                int errors = 0;

                foreach (Entry entry in _entries)
                {
                    try
                    {
                        object key = ConvertKey(entry.Id, mode);
                        if (key == null)
                        {
                            missing++;
                            continue;
                        }

                        object currentObject = ReadValue(
                            table,
                            key,
                            mode,
                            stringGetter,
                            objectGetter);
                        if (currentObject == null)
                        {
                            missing++;
                            continue;
                        }

                        string current = currentObject as string ?? currentObject.ToString();
                        if (string.Equals(current, entry.Japanese, StringComparison.Ordinal))
                        {
                            already++;
                            continue;
                        }
                        if (!string.Equals(current, entry.English, StringComparison.Ordinal))
                        {
                            mismatch++;
                            continue;
                        }

                        WriteValue(
                            table,
                            key,
                            entry.Japanese,
                            mode,
                            stringSetter,
                            objectSetter);

                        object verifiedObject = ReadValue(
                            table,
                            key,
                            mode,
                            stringGetter,
                            objectGetter);
                        string verified = verifiedObject as string ??
                            (verifiedObject == null ? null : verifiedObject.ToString());
                        if (string.Equals(verified, entry.Japanese, StringComparison.Ordinal))
                        {
                            patched++;
                        }
                        else
                        {
                            errors++;
                        }
                    }
                    catch
                    {
                        errors++;
                    }
                }

                if (patched + already == 0)
                {
                    Logger.LogWarning(
                        "[LocalizationTablePatcher] Key mode " + mode +
                        " was detected, but no entries were patched; retrying.");
                    return;
                }

                LogCurrentCSharpTransProbes("post-table-patch");

                _lastRetryStatus = null;
                lock (_patchedLocalizationTables)
                {
                    _patchedLocalizationTables.Add(table);
                }
                Logger.LogInfo(
                    "[LocalizationTablePatcher] Patched Localization_en via " + mode +
                    " keys. patched=" + patched +
                    ", already=" + already +
                    ", missing=" + missing +
                    ", mismatch=" + mismatch +
                    ", errors=" + errors +
                    ", tableRef=" + RuntimeHelpers.GetHashCode(table).ToString("X8", CultureInfo.InvariantCulture) +
                    ", tableAsm=" + GetAssemblyToken(table.GetType().Assembly) +
                    ", trigger=" + trigger + ".");
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Logger.LogWarning(
                    "[LocalizationTablePatcher] Runtime patch attempt failed: " + inner.Message);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher] Runtime patch attempt failed: " + ex.Message);
            }
            finally
            {
                _insideAttempt = false;
            }
        }

        private static object FindExistingLuaManager(Type managerType)
        {
            // Read SingletonMonoBehaviour<T>._instance directly.  Unlike the
            // Instance property this has no create-on-read side effect.
            for (Type cursor = managerType.BaseType; cursor != null; cursor = cursor.BaseType)
            {
                FieldInfo instanceField = cursor.GetField(
                    "_instance",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                if (instanceField != null)
                {
                    object existing = instanceField.GetValue(null);
                    if (existing != null)
                    {
                        return existing;
                    }
                    break;
                }
            }

            // Fallback for a singleton whose static field has not yet been
            // populated.  Resources.FindObjectsOfTypeAll includes inactive and
            // DontDestroyOnLoad objects but never creates a new component.
            UnityEngine.Object[] candidates = Resources.FindObjectsOfTypeAll(managerType);
            if (candidates != null)
            {
                foreach (UnityEngine.Object candidate in candidates)
                {
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private void LogRetryStatus(string status, string trigger)
        {
            if (string.Equals(_lastRetryStatus, status, StringComparison.Ordinal))
            {
                return;
            }
            _lastRetryStatus = status;
            Logger.LogInfo(
                "[LocalizationTablePatcher] Waiting: " + status + ", trigger=" + trigger + ".");
        }

        private void CaptureLuaEnv(object luaEnv, string source)
        {
            if (luaEnv == null)
            {
                return;
            }

            bool newlyDiscovered;
            lock (_knownLuaEnvs)
            {
                newlyDiscovered = _knownLuaEnvs.Add(luaEnv);
            }
            if (newlyDiscovered)
            {
                // A later-loaded Assembly-CSharp/XLua copy may initialize after a
                // previous copy was already patched. Re-open the probe state so
                // this newly observed LuaEnv cannot be skipped by _done.
                _done = false;
            }

            bool changed = !ReferenceEquals(_capturedLuaEnv, luaEnv);
            _capturedLuaEnv = luaEnv;
            if (changed || newlyDiscovered)
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] Captured live LuaEnv from " + source +
                    ": " + TypeName(luaEnv) +
                    ", envRef=" + RuntimeHelpers.GetHashCode(luaEnv).ToString("X8", CultureInfo.InvariantCulture) +
                    ", asm=" + GetAssemblyToken(luaEnv.GetType().Assembly) +
                    ", newlyDiscovered=" + newlyDiscovered + ".");
            }

            LogLuaEnvSnapshot(luaEnv, source, _localizationLoadObserved);
        }

        private void LogLuaEnvSnapshot(object luaEnv, string source, bool afterLocalization)
        {
            if (luaEnv == null)
            {
                return;
            }

            HashSet<object> loggedSet = afterLocalization
                ? _loggedLuaEnvsAfterLocalization
                : _loggedLuaEnvsBeforeLocalization;
            lock (loggedSet)
            {
                if (!loggedSet.Add(luaEnv))
                {
                    return;
                }
            }

            try
            {
                MethodInfo getGlobals = luaEnv.GetType().GetMethod(
                    "Obf_PTA",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                object globals = getGlobals == null ? null : getGlobals.Invoke(luaEnv, null);
                object direct = GetLuaTableStringValue(globals, "Localization_en");
                object package = GetLuaTableStringValue(globals, "package");
                object loaded = GetLuaTableStringValue(package, "loaded");
                object module = GetLuaTableStringValue(loaded, "Localization_en");

                string[] candidateNames =
                {
                    "LanguageKV",
                    "Localization",
                    "Language",
                    "LanguageData",
                    "Lang",
                };
                List<string> candidates = new List<string>();
                foreach (string name in candidateNames)
                {
                    candidates.Add(name + "=" + TypeName(GetLuaTableStringValue(globals, name)));
                }

                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] LuaEnv snapshot stage=" +
                    (afterLocalization ? "post-Localization_en" : "pre-Localization_en") +
                    " source=" + source +
                    " env=" + TypeName(luaEnv) +
                    " globals=" + TypeName(globals) +
                    " global.Localization_en=" + TypeName(direct) +
                    " package=" + TypeName(package) +
                    " package.loaded=" + TypeName(loaded) +
                    " package.loaded.Localization_en=" + TypeName(module) +
                    " candidateGlobals={" + string.Join(", ", candidates.ToArray()) + "}.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] LuaEnv snapshot failed at " + source +
                    ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void LogLocalizationTableDiagnostics(
            object table,
            MethodInfo stringGetter,
            MethodInfo objectGetter,
            string source)
        {
            if (_loggedLocalizationTableDiagnostics || table == null)
            {
                return;
            }
            _loggedLocalizationTableDiagnostics = true;

            Logger.LogInfo(
                "[LocalizationTablePatcher][diag] Localization table acquired from " + source +
                ": type=" + TypeName(table) + ".");

            string[] probeIds =
            {
                "10530000045", // Piere
                "10530000067", // Isis
                "10470000759", // Ahn Gu-ho
                "10470000828", // Alphonse
                "10470000996", // Caravan Member
                "36001",       // Floor ${1}
                "13150100573", // dynamic main-quest title
            };

            foreach (string id in probeIds)
            {
                object byString = SafeInvoke(stringGetter, table, new object[] { id });
                object byLong = null;
                object byInt = null;
                object byDouble = null;
                long numeric;
                if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out numeric))
                {
                    byLong = SafeInvoke(objectGetter, table, new object[] { (object)numeric });
                    if (numeric >= int.MinValue && numeric <= int.MaxValue)
                    {
                        byInt = SafeInvoke(objectGetter, table, new object[] { (object)(int)numeric });
                    }
                    byDouble = SafeInvoke(objectGetter, table, new object[] { (object)(double)numeric });
                }

                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] key-probe id=" + id +
                    " string=" + DescribeValue(byString) +
                    " int64=" + DescribeValue(byLong) +
                    " int32=" + DescribeValue(byInt) +
                    " double=" + DescribeValue(byDouble) + ".");
            }

            try
            {
                List<string> interfaces = new List<string>();
                foreach (Type iface in table.GetType().GetInterfaces())
                {
                    interfaces.Add(iface.FullName);
                }
                List<string> interestingMethods = new List<string>();
                foreach (MethodInfo method in table.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    string lower = method.Name.ToLowerInvariant();
                    if (lower.Contains("enumer") || lower.Contains("key") || lower.Contains("dict") ||
                        lower.Contains("table") || lower.Contains("array"))
                    {
                        interestingMethods.Add(method.ToString());
                    }
                    if (interestingMethods.Count >= 20)
                    {
                        break;
                    }
                }
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] table interfaces={" +
                    string.Join(", ", interfaces.ToArray()) + "}; interestingMethods={" +
                    string.Join(" | ", interestingMethods.ToArray()) + "}.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] table reflection diagnostics failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string DescribeValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }
            string text;
            try
            {
                text = value as string ?? value.ToString();
            }
            catch
            {
                text = "<ToString failed>";
            }
            if (text != null)
            {
                text = text.Replace("\r", "\\r").Replace("\n", "\\n");
                if (text.Length > 100)
                {
                    text = text.Substring(0, 100) + "...";
                }
            }
            return TypeName(value) + ":\"" + (text ?? "<null>") + "\"";
        }

        private static string DescribeObjectArray(object[] values)
        {
            if (values == null)
            {
                return "<null>";
            }
            List<string> parts = new List<string>();
            int limit = Math.Min(values.Length, 4);
            for (int i = 0; i < limit; i++)
            {
                parts.Add(i + "=" + TypeName(values[i]));
            }
            if (values.Length > limit)
            {
                parts.Add("...");
            }
            return "len=" + values.Length + " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        private void LogHarmonyPatchDiagnostics()
        {
            if (_loggedHarmonyPatchDiagnostics)
            {
                return;
            }
            _loggedHarmonyPatchDiagnostics = true;
            Logger.LogInfo(
                "[LocalizationTablePatcher][diag] Lifecycle hook registration complete; " +
                "each target above was patched independently so one failed hook cannot suppress the others.");
        }

        private static object FindLuaEnvFromDelegateBridgeList(out string detail)
        {
            List<object> envs = FindLuaEnvsFromDelegateBridgeLists(out detail);
            return envs.Count == 0 ? null : envs[0];
        }

        private static List<object> FindLuaEnvsFromDelegateBridgeLists(out string detail)
        {
            List<Type> bridgeTypes = FindTypes("XLua.DelegateBridge");
            List<object> envs = new List<object>();
            HashSet<object> seen = new HashSet<object>(ReferenceComparer<object>.Instance);
            List<string> copyDetails = new List<string>();
            if (bridgeTypes.Count == 0)
            {
                detail = "XLua.DelegateBridge type not loaded";
                return envs;
            }

            foreach (Type bridgeType in bridgeTypes)
            {
                string token = GetAssemblyToken(bridgeType.Assembly);
                try
                {
                    FieldInfo listField = bridgeType.GetField(
                        "DelegateBridgeList",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (listField == null)
                    {
                        copyDetails.Add("asm=" + token + ": field missing");
                        continue;
                    }

                    Array bridges = listField.GetValue(null) as Array;
                    if (bridges == null)
                    {
                        copyDetails.Add("asm=" + token + ": list=<null>");
                        continue;
                    }

                    int nonNull = 0;
                    int withEnv = 0;
                    for (int i = 0; i < bridges.Length; i++)
                    {
                        object bridge = bridges.GetValue(i);
                        if (bridge == null)
                        {
                            continue;
                        }
                        nonNull++;
                        object luaEnv = GetInstanceField(bridge, bridge.GetType(), "Obf_blA");
                        if (luaEnv == null)
                        {
                            continue;
                        }
                        withEnv++;
                        if (seen.Add(luaEnv))
                        {
                            envs.Add(luaEnv);
                        }
                    }
                    copyDetails.Add(
                        "asm=" + token +
                        ": length=" + bridges.Length +
                        ", nonNull=" + nonNull +
                        ", withEnv=" + withEnv);
                }
                catch (Exception ex)
                {
                    copyDetails.Add(
                        "asm=" + token + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            detail =
                "copies=" + bridgeTypes.Count +
                ", uniqueEnvs=" + envs.Count +
                ", " + string.Join(" | ", copyDetails.ToArray());
            return envs;
        }

        private static object FindLuaEnvFromLuaComponents(out string detail)
        {
            List<object> envs = FindLuaEnvsFromLuaComponents(out detail);
            return envs.Count == 0 ? null : envs[0];
        }

        private static List<object> FindLuaEnvsFromLuaComponents(out string detail)
        {
            List<Type> componentTypes = FindTypes("LA_LuaComponent");
            List<object> envs = new List<object>();
            HashSet<object> seen = new HashSet<object>(ReferenceComparer<object>.Instance);
            List<string> copyDetails = new List<string>();
            if (componentTypes.Count == 0)
            {
                detail = "LA_LuaComponent type not loaded";
                return envs;
            }

            foreach (Type componentType in componentTypes)
            {
                string token = GetAssemblyToken(componentType.Assembly);
                try
                {
                    UnityEngine.Object[] components = Resources.FindObjectsOfTypeAll(componentType);
                    if (components == null || components.Length == 0)
                    {
                        copyDetails.Add("asm=" + token + ": count=0");
                        continue;
                    }

                    int directEnvs = 0;
                    int tableEnvs = 0;
                    for (int i = 0; i < components.Length; i++)
                    {
                        UnityEngine.Object component = components[i];
                        if (component == null)
                        {
                            continue;
                        }

                        object luaEnv = GetInstanceField(component, component.GetType(), "Obf_ac");
                        if (luaEnv != null)
                        {
                            directEnvs++;
                            if (seen.Add(luaEnv))
                            {
                                envs.Add(luaEnv);
                            }
                        }

                        object luaTable = GetInstanceField(component, component.GetType(), "Obf_Ac");
                        if (luaTable != null)
                        {
                            luaEnv = GetInstanceField(luaTable, luaTable.GetType(), "Obf_blA");
                            if (luaEnv != null)
                            {
                                tableEnvs++;
                                if (seen.Add(luaEnv))
                                {
                                    envs.Add(luaEnv);
                                }
                            }
                        }
                    }
                    copyDetails.Add(
                        "asm=" + token +
                        ": count=" + components.Length +
                        ", directEnvs=" + directEnvs +
                        ", tableEnvs=" + tableEnvs);
                }
                catch (Exception ex)
                {
                    copyDetails.Add(
                        "asm=" + token + ": FindObjectsOfTypeAll failed: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }

            detail =
                "copies=" + componentTypes.Count +
                ", uniqueEnvs=" + envs.Count +
                ", " + string.Join(" | ", copyDetails.ToArray());
            return envs;
        }

        private static string SafeUnityObjectName(UnityEngine.Object value)
        {
            if (value == null)
            {
                return "<null>";
            }
            try
            {
                return value.name ?? value.GetType().FullName;
            }
            catch
            {
                return value.GetType().FullName;
            }
        }

        private KeyMode DetectKeyMode(object table, MethodInfo stringGetter, MethodInfo objectGetter)
        {
            int probeCount = Math.Min(_entries.Count, 128);
            for (int i = 0; i < probeCount; i++)
            {
                Entry entry = _entries[i];

                object value = SafeInvoke(stringGetter, table, new object[] { entry.Id });
                if (MatchesExpected(value, entry))
                {
                    return KeyMode.String;
                }

                long longKey;
                if (long.TryParse(entry.Id, NumberStyles.None, CultureInfo.InvariantCulture, out longKey))
                {
                    value = SafeInvoke(objectGetter, table, new object[] { (object)longKey });
                    if (MatchesExpected(value, entry))
                    {
                        return KeyMode.Int64;
                    }

                    if (longKey >= int.MinValue && longKey <= int.MaxValue)
                    {
                        value = SafeInvoke(objectGetter, table, new object[] { (object)(int)longKey });
                        if (MatchesExpected(value, entry))
                        {
                            return KeyMode.Int32;
                        }
                    }

                    value = SafeInvoke(objectGetter, table, new object[] { (object)(double)longKey });
                    if (MatchesExpected(value, entry))
                    {
                        return KeyMode.Double;
                    }
                }
            }
            return KeyMode.Unknown;
        }

        private static bool MatchesExpected(object value, Entry entry)
        {
            if (value == null)
            {
                return false;
            }
            string text = value as string ?? value.ToString();
            return string.Equals(text, entry.English, StringComparison.Ordinal) ||
                   string.Equals(text, entry.Japanese, StringComparison.Ordinal);
        }

        private static object SafeInvoke(MethodInfo method, object instance, object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch
            {
                return null;
            }
        }

        private static object ConvertKey(string id, KeyMode mode)
        {
            if (mode == KeyMode.String)
            {
                return id;
            }

            long value;
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                return null;
            }

            switch (mode)
            {
                case KeyMode.Int64:
                    return value;
                case KeyMode.Int32:
                    if (value < int.MinValue || value > int.MaxValue)
                    {
                        return null;
                    }
                    return (int)value;
                case KeyMode.Double:
                    return (double)value;
                default:
                    return null;
            }
        }

        private static object ReadValue(
            object table,
            object key,
            KeyMode mode,
            MethodInfo stringGetter,
            MethodInfo objectGetter)
        {
            if (mode == KeyMode.String)
            {
                return stringGetter.Invoke(table, new object[] { key });
            }
            return objectGetter.Invoke(table, new object[] { key });
        }

        private static void WriteValue(
            object table,
            object key,
            string value,
            KeyMode mode,
            MethodInfo stringSetter,
            MethodInfo objectSetter)
        {
            if (mode == KeyMode.String)
            {
                stringSetter.Invoke(table, new object[] { key, value });
                return;
            }
            objectSetter.Invoke(table, new object[] { key, value });
        }

        private static Type FindType(string fullName)
        {
            List<Type> matches = FindTypes(fullName);
            return matches.Count == 0 ? null : matches[0];
        }

        private static List<Type> FindTypes(string fullName)
        {
            List<Type> matches = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        matches.Add(type);
                    }
                }
                catch
                {
                }
            }
            return matches;
        }

        private static PropertyInfo FindStaticProperty(Type type, string name)
        {
            for (Type cursor = type; cursor != null; cursor = cursor.BaseType)
            {
                PropertyInfo property = cursor.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null)
                {
                    return property;
                }
            }
            return null;
        }

        private static object FindLuaTable(object[] values)
        {
            if (values == null)
            {
                return null;
            }

            foreach (object value in values)
            {
                if (value == null)
                {
                    continue;
                }
                Type type = value.GetType();
                if (type.GetMethod("Obf_MuA", new[] { typeof(string) }) != null &&
                    type.GetMethod("Obf_NuA", new[] { typeof(object) }) != null)
                {
                    return value;
                }
            }
            return null;
        }

        private static object FindLocalizationTableFromGameLoop(object gameLoop, out string detail)
        {
            if (gameLoop == null)
            {
                detail = "game-loop instance is null";
                return null;
            }

            object luaUpdate = GetInstanceField(gameLoop, gameLoop.GetType(), "Obf_JP");
            if (luaUpdate == null)
            {
                detail = "Obf_Zb.Obf_JP is null";
                return null;
            }

            return FindLocalizationTableFromLuaUpdate(luaUpdate, out detail);
        }

        private static object FindLocalizationTableFromLuaUpdate(object luaUpdate, out string detail)
        {
            if (luaUpdate == null)
            {
                detail = "Obf_o Lua update object is null";
                return null;
            }

            object updateDelegateObject = GetInstanceField(luaUpdate, luaUpdate.GetType(), "Obf_Ic");
            Delegate updateDelegate = updateDelegateObject as Delegate;
            if (updateDelegate == null)
            {
                detail = "Obf_o.Obf_Ic Update delegate is null";
                return null;
            }

            object bridge = updateDelegate.Target;
            if (bridge == null)
            {
                detail = "XLua Update delegate target is null";
                return null;
            }

            // XLua.DelegateBridge -> DelegateBridgeBase -> Obf_R.Obf_cf.  The
            // Obf_blA field on that base class is the live Obf_R.Obf_Cf LuaEnv
            // used by this exact delegate, so it remains usable even after the
            // LA_LuaManager singleton is no longer discoverable as a Unity object.
            object luaEnv = GetInstanceField(bridge, bridge.GetType(), "Obf_blA");
            if (luaEnv == null)
            {
                detail = "XLua DelegateBridge.Obf_blA LuaEnv is null (bridge=" +
                    bridge.GetType().FullName + ")";
                return null;
            }

            return FindLoadedModuleTableFromLuaEnv(luaEnv, "Localization_en", out detail);
        }

        private static object FindLoadedModuleTable(
            object manager,
            Type managerType,
            string moduleName,
            out string detail)
        {
            // A Lua module can be successfully loaded even when the wrapper
            // around require() exposes no return values. Read Lua's standard
            // package.loaded cache directly through the existing LuaTable C#
            // API instead of executing arbitrary Lua code.
            object luaEnv = GetInstanceField(manager, managerType, "Obf_Dc");
            if (luaEnv == null)
            {
                detail = "LA_LuaManager.Obf_Dc is null";
                return null;
            }

            return FindLoadedModuleTableFromLuaEnv(luaEnv, moduleName, out detail);
        }

        private static object FindLoadedModuleTableFromLuaEnv(
            object luaEnv,
            string moduleName,
            out string detail)
        {

            MethodInfo getGlobals = luaEnv.GetType().GetMethod(
                "Obf_PTA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (getGlobals == null)
            {
                detail = luaEnv.GetType().FullName + ".Obf_PTA() missing";
                return null;
            }

            object globals = getGlobals.Invoke(luaEnv, null);
            if (!IsLuaTable(globals))
            {
                detail = "globals=" + TypeName(globals);
                return null;
            }

            // Some generated localization modules also assign themselves to a
            // global. Prefer that simple path when present.
            object direct = GetLuaTableStringValue(globals, moduleName);
            if (IsLuaTable(direct))
            {
                string directMode;
                if (TableLooksLikeLocalization(direct, out directMode))
                {
                    detail = "global " + moduleName + " direct table, keyMode=" + directMode;
                    return direct;
                }

                string directNestedDetail;
                object directNested = FindLocalizationTableRecursively(
                    direct,
                    "global." + moduleName,
                    4,
                    512,
                    out directNestedDetail);
                if (directNested != null)
                {
                    detail = directNestedDetail;
                    return directNested;
                }
            }

            object package = GetLuaTableStringValue(globals, "package");
            object loaded = IsLuaTable(package)
                ? GetLuaTableStringValue(package, "loaded")
                : null;
            object module = IsLuaTable(loaded)
                ? GetLuaTableStringValue(loaded, moduleName)
                : null;
            if (IsLuaTable(module))
            {
                string moduleMode;
                if (TableLooksLikeLocalization(module, out moduleMode))
                {
                    detail = "package.loaded." + moduleName + " direct table, keyMode=" + moduleMode;
                    return module;
                }

                string moduleNestedDetail;
                object moduleNested = FindLocalizationTableRecursively(
                    module,
                    "package.loaded." + moduleName,
                    4,
                    512,
                    out moduleNestedDetail);
                if (moduleNested != null)
                {
                    detail = moduleNestedDetail;
                    return moduleNested;
                }
            }

            string scanDetail;
            object scanned = FindLocalizationTableByScanningGlobals(globals, out scanDetail);
            if (scanned != null)
            {
                detail = scanDetail;
                return scanned;
            }
            detail =
                "global=" + TypeName(direct) +
                ", package=" + TypeName(package) +
                ", package.loaded=" + TypeName(loaded) +
                ", package.loaded." + moduleName + "=" + TypeName(module) +
                ", scan=" + scanDetail;
            return null;
        }

        private static object GetGlobalTableFromLuaEnv(object luaEnv, string globalName)
        {
            if (luaEnv == null || string.IsNullOrEmpty(globalName))
            {
                return null;
            }

            MethodInfo getGlobals = luaEnv.GetType().GetMethod(
                "Obf_PTA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (getGlobals == null)
            {
                return null;
            }
            object globals = SafeInvoke(getGlobals, luaEnv, null);
            return GetLuaTableStringValue(globals, globalName);
        }

        private void LogCurrentCSharpTransProbes(string stage)
        {
            List<Type> languageMainTypes = FindTypes("LanguageMain");
            if (languageMainTypes.Count == 0)
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                    " probe skipped: no LanguageMain copies loaded.");
                return;
            }

            foreach (Type languageMainType in languageMainTypes)
            {
                string token = GetAssemblyToken(languageMainType.Assembly);
                try
                {
                    FieldInfo field = languageMainType.GetField(
                        "Obf_pk",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object function = field == null ? null : field.GetValue(null);
                    if (function == null)
                    {
                        Logger.LogInfo(
                            "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                            " asm=" + token + " function=<null>.");
                        continue;
                    }
                    LogCSharpTransProbes(function, stage + ",asm=" + token);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                        " asm=" + token + " probe lookup failed: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private void LogCSharpTransProbes(object function, string stage)
        {
            if (function == null)
            {
                return;
            }

            MethodInfo call = function.GetType().GetMethod(
                "Obf_HuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(object[]) },
                null);
            if (call == null)
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                    " probe skipped: Obf_HuA(object[]) missing on " + TypeName(function) + ".");
                return;
            }

            long[] ids =
            {
                10530000045L, // Piere
                10530000067L, // Isis
                10470000759L, // Ahn Gu-ho
                10470000996L, // Caravan Member
                36001L,
            };

            foreach (long id in ids)
            {
                try
                {
                    object resultObject = call.Invoke(
                        function,
                        new object[] { new object[] { id } });
                    object[] results = resultObject as object[];
                    Logger.LogInfo(
                        "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                        " id=" + id.ToString(CultureInfo.InvariantCulture) +
                        " -> " + DescribeObjectArrayWithValues(results) + ".");
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    Logger.LogWarning(
                        "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                        " id=" + id.ToString(CultureInfo.InvariantCulture) +
                        " threw " + inner.GetType().Name + ": " + inner.Message);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        "[LocalizationTablePatcher][diag] CSharpTrans " + stage +
                        " id=" + id.ToString(CultureInfo.InvariantCulture) +
                        " failed " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static string DescribeObjectArrayWithValues(object[] values)
        {
            if (values == null)
            {
                return "<null>";
            }
            List<string> parts = new List<string>();
            int limit = Math.Min(values.Length, 4);
            for (int i = 0; i < limit; i++)
            {
                parts.Add(i + "=" + DescribeValue(values[i]));
            }
            if (values.Length > limit)
            {
                parts.Add("...");
            }
            return "len=" + values.Length + " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        private void LogLuaTableKeySummary(object table, string label, int maxKeys)
        {
            if (!IsLuaTable(table))
            {
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] " + label + " is " + TypeName(table) + ".");
                return;
            }

            try
            {
                List<string> items = new List<string>();
                int count = 0;
                foreach (object key in EnumerateLuaTableKeys(table, maxKeys))
                {
                    object value = GetLuaTableObjectValue(table, key);
                    items.Add(DescribeValue(key) + "=>" + TypeName(value));
                    count++;
                }
                Logger.LogInfo(
                    "[LocalizationTablePatcher][diag] " + label +
                    " keys(sample=" + count + ")={" + string.Join(" | ", items.ToArray()) + "}.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[LocalizationTablePatcher][diag] " + label + " key summary failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static object FindLocalizationTableRecursively(
            object root,
            string rootName,
            int maxDepth,
            int maxTables,
            out string detail)
        {
            detail = rootName + " unavailable";
            if (!IsLuaTable(root) || maxDepth < 0 || maxTables <= 0)
            {
                return null;
            }

            Queue<object> tables = new Queue<object>();
            Queue<string> names = new Queue<string>();
            Queue<int> depths = new Queue<int>();
            HashSet<int> seen = new HashSet<int>();

            tables.Enqueue(root);
            names.Enqueue(rootName);
            depths.Enqueue(0);
            int inspected = 0;

            while (tables.Count > 0 && inspected < maxTables)
            {
                object current = tables.Dequeue();
                string currentName = names.Dequeue();
                int depth = depths.Dequeue();
                if (current == null)
                {
                    continue;
                }

                int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(current);
                if (!seen.Add(identity))
                {
                    continue;
                }
                inspected++;

                string mode;
                if (TableLooksLikeLocalization(current, out mode))
                {
                    detail =
                        currentName + " localization table, keyMode=" + mode +
                        ", depth=" + depth +
                        ", inspectedTables=" + inspected;
                    return current;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                int childCount = 0;
                foreach (object key in EnumerateLuaTableKeys(current, 256))
                {
                    object value = GetLuaTableObjectValue(current, key);
                    if (!IsLuaTable(value))
                    {
                        continue;
                    }
                    childCount++;
                    tables.Enqueue(value);
                    names.Enqueue(currentName + "[" + DescribeValue(key) + "]");
                    depths.Enqueue(depth + 1);
                    if (childCount >= 128 || tables.Count + inspected >= maxTables)
                    {
                        break;
                    }
                }
            }

            detail =
                rootName + " recursive scan found no localization-shaped table; inspectedTables=" +
                inspected + ", maxDepth=" + maxDepth + ", maxTables=" + maxTables;
            return null;
        }

        private static object FindLocalizationTableByScanningGlobals(object globals, out string detail)
        {
            detail = "not scanned";
            if (!IsLuaTable(globals))
            {
                detail = "globals not a LuaTable";
                return null;
            }

            object language = GetLuaTableStringValue(globals, "Language");
            string mode;
            if (IsLuaTable(language) && TableLooksLikeLocalization(language, out mode))
            {
                detail = "global.Language direct localization table, keyMode=" + mode;
                return language;
            }

            object nested = FindLocalizationTableInContainer(language, "global.Language", 256, out detail);
            if (nested != null)
            {
                return nested;
            }

            int inspectedKeys = 0;
            int inspectedTables = 0;
            foreach (object key in EnumerateLuaTableKeys(globals, 1024))
            {
                inspectedKeys++;
                object value = GetLuaTableObjectValue(globals, key);
                if (!IsLuaTable(value))
                {
                    continue;
                }
                inspectedTables++;
                if (TableLooksLikeLocalization(value, out mode))
                {
                    detail =
                        "global[" + DescribeValue(key) + "] localization table, keyMode=" + mode +
                        ", inspectedKeys=" + inspectedKeys +
                        ", inspectedTables=" + inspectedTables;
                    return value;
                }

                if (inspectedTables <= 128)
                {
                    string childDetail;
                    object child = FindLocalizationTableInContainer(
                        value,
                        "global[" + DescribeValue(key) + "]",
                        128,
                        out childDetail);
                    if (child != null)
                    {
                        detail = childDetail;
                        return child;
                    }
                }
            }

            detail =
                "no localization-shaped table found; inspectedKeys=" + inspectedKeys +
                ", inspectedTables=" + inspectedTables +
                ", Language=" + TypeName(language);
            return null;
        }

        private static object FindLocalizationTableInContainer(
            object container,
            string containerName,
            int maxKeys,
            out string detail)
        {
            detail = containerName + " unavailable";
            if (!IsLuaTable(container))
            {
                return null;
            }

            int inspected = 0;
            string mode;
            foreach (object key in EnumerateLuaTableKeys(container, maxKeys))
            {
                inspected++;
                object value = GetLuaTableObjectValue(container, key);
                if (!IsLuaTable(value))
                {
                    continue;
                }
                if (TableLooksLikeLocalization(value, out mode))
                {
                    detail =
                        containerName + "[" + DescribeValue(key) +
                        "] localization table, keyMode=" + mode +
                        ", inspected=" + inspected;
                    return value;
                }
            }

            detail = containerName + " scanned " + inspected + " keys without a localization-shaped child table";
            return null;
        }

        private static bool TableLooksLikeLocalization(object table, out string mode)
        {
            mode = "unknown";
            if (!IsLuaTable(table))
            {
                return false;
            }

            Type type = table.GetType();
            MethodInfo stringGetter = type.GetMethod(
                "Obf_MuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            MethodInfo objectGetter = type.GetMethod(
                "Obf_NuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(object) },
                null);
            if (stringGetter == null || objectGetter == null)
            {
                return false;
            }

            long first = 10530000000L; // Poring
            long second = 10530000045L; // Piere

            if (IsStringValue(SafeInvoke(stringGetter, table, new object[] { first.ToString(CultureInfo.InvariantCulture) })) &&
                IsStringValue(SafeInvoke(stringGetter, table, new object[] { second.ToString(CultureInfo.InvariantCulture) })))
            {
                mode = "String";
                return true;
            }
            if (IsStringValue(SafeInvoke(objectGetter, table, new object[] { (object)first })) &&
                IsStringValue(SafeInvoke(objectGetter, table, new object[] { (object)second })))
            {
                mode = "Int64";
                return true;
            }
            if (IsStringValue(SafeInvoke(objectGetter, table, new object[] { (object)(double)first })) &&
                IsStringValue(SafeInvoke(objectGetter, table, new object[] { (object)(double)second })))
            {
                mode = "Double";
                return true;
            }
            return false;
        }

        private static bool IsStringValue(object value)
        {
            return value is string;
        }

        private static IEnumerable<object> EnumerateLuaTableKeys(object table, int maxKeys)
        {
            if (!IsLuaTable(table) || maxKeys <= 0)
            {
                yield break;
            }

            MethodInfo enumerate = table.GetType().GetMethod(
                "Obf_PuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (enumerate == null)
            {
                yield break;
            }

            System.Collections.IEnumerable values = SafeInvoke(enumerate, table, null) as System.Collections.IEnumerable;
            if (values == null)
            {
                yield break;
            }

            int count = 0;
            foreach (object key in values)
            {
                yield return key;
                count++;
                if (count >= maxKeys)
                {
                    yield break;
                }
            }
        }

        private static object GetLuaTableObjectValue(object table, object key)
        {
            if (!IsLuaTable(table))
            {
                return null;
            }
            MethodInfo getter = table.GetType().GetMethod(
                "Obf_NuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(object) },
                null);
            return getter == null ? null : SafeInvoke(getter, table, new object[] { key });
        }

        private static string TypeName(object value)
        {
            return value == null ? "<null>" : value.GetType().FullName;
        }

        private static object GetInstanceField(object instance, Type type, string name)
        {
            for (Type cursor = type; cursor != null; cursor = cursor.BaseType)
            {
                FieldInfo field = cursor.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
            }
            return null;
        }

        private static object GetLuaTableStringValue(object table, string key)
        {
            if (!IsLuaTable(table))
            {
                return null;
            }
            MethodInfo getter = table.GetType().GetMethod(
                "Obf_MuA",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            if (getter == null)
            {
                return null;
            }
            return SafeInvoke(getter, table, new object[] { key });
        }

        private static bool IsLuaTable(object value)
        {
            if (value == null)
            {
                return false;
            }
            Type type = value.GetType();
            return type.GetMethod("Obf_MuA", new[] { typeof(string) }) != null &&
                   type.GetMethod("Obf_NuA", new[] { typeof(object) }) != null;
        }
    }
}
