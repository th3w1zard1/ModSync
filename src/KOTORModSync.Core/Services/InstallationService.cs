// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core;
using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services.Checkpoints;
using KOTORModSync.Core.Utility;

using Python.Included;
using Python.Runtime;

namespace KOTORModSync.Core.Services
{

    public class InstallationService
    {
        private static bool _pythonInitialized = false;
        private static readonly SemaphoreSlim _pythonSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Initializes the embedded Python environment if not already initialized.
        /// </summary>
        private static async Task EnsurePythonInitializedAsync()
        {
            if (_pythonInitialized)
            {
                return;
            }

            await _pythonSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_pythonInitialized)
                {
                    return;
                }

                // Disable keyring to prevent pip from hanging waiting for authentication
                // This is a known issue with pip - it tries to use keyring for authentication
                // which can hang indefinitely waiting for user input
                Environment.SetEnvironmentVariable("PYTHON_KEYRING_BACKEND", "keyring.backends.null.Keyring");


                await Logger.LogVerboseAsync("Initializing embedded Python environment...")
.ConfigureAwait(false);
                DateTime startTime = DateTime.Now;

                await Installer.SetupPython().ConfigureAwait(false);
                PythonEngine.Initialize();

                // Check if dependencies are already installed (from build time)
                bool dependenciesInstalled = await CheckPythonDependenciesAsync().ConfigureAwait(false);

                if (!dependenciesInstalled)

                {
                    await Logger.LogVerboseAsync("Python dependencies not found, installing HoloPatcher Python dependencies...").ConfigureAwait(false);
                    await Logger.LogVerboseAsync("This may take several minutes on first run...").ConfigureAwait(false);

                    // Use Python.NET to call pip._internal.main() directly to avoid Python.Included's buggy RunCommand
                    using (Py.GIL())
                    {
                        try
                        {
                            // Set up stdout/stderr to prevent NoneType write errors
                            dynamic sys = Py.Import("sys");
                            dynamic io = Py.Import("io");
                            dynamic StringIO = io.StringIO;
                            sys.stdout = StringIO();
                            sys.stderr = StringIO();


                            await Logger.LogVerboseAsync("Set up StringIO for stdout/stderr to prevent NoneType write errors")
.ConfigureAwait(false);

                            // Install loggerplus
                            await Logger.LogVerboseAsync("Installing loggerplus...")
.ConfigureAwait(false);
                            dynamic pip =





Py.Import("pip._internal");
                            dynamic pipMain = pip.main;

                            var args = new Python.Runtime.PyList();
                            args.Append(new Python.Runtime.PyString("install"));




































































                            args.Append(new Python.Runtime.PyString("loggerplus"));
                            pipMain(args);
                            await Logger.LogVerboseAsync("✓ loggerplus installed").ConfigureAwait(false);

                            // Install ply
                            await Logger.LogVerboseAsync("Installing ply...").ConfigureAwait(false);
                            var argsply = new Python.Runtime.PyList();
                            argsply.Append(new Python.Runtime.PyString("install"));
                            argsply.Append(new Python.Runtime.PyString("ply"));
                            pipMain(argsply);
                            await Logger.LogVerboseAsync("✓ ply installed").ConfigureAwait(false);
                        }
                        catch (PythonException ex)
                        {
                            await Logger.LogExceptionAsync(ex, "Failed to install Python dependencies via pip._internal").ConfigureAwait(false);
                            throw;
                        }
                    }

                    await Logger.LogVerboseAsync("Python dependencies installation completed.").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogVerboseAsync("Python dependencies already installed (from build time).").ConfigureAwait(false);
                }

                TimeSpan elapsed = DateTime.Now - startTime;
                await Logger.LogVerboseAsync($"Python environment initialization completed in {elapsed.TotalSeconds:F1} seconds.").ConfigureAwait(false);

                _pythonInitialized = true;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Failed to initialize Python environment").ConfigureAwait(false);
                throw;
            }
            finally
            {
                _pythonSemaphore.Release();
            }
        }

        /// <summary>
        /// Checks if the required Python dependencies are already installed.
        /// </summary>
        private static async Task<bool> CheckPythonDependenciesAsync()
        {
            try
            {
                using (Py.GIL())
                {
                    // Try to import the required modules
                    try
                    {
                        Py.Import("loggerplus");
                        Py.Import("ply");
                        return true;
                    }
                    catch (PythonException ex)
                    {
                        await Logger.LogVerboseAsync($"Python dependency check failed: {ex.Message}").ConfigureAwait(false);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"Error checking Python dependencies: {ex.Message}").ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Finds the platform-specific holopatcher executable (exe/binary version).
        /// </summary>
        /// <param name="resourcesDir">Resources directory path</param>
        /// <param name="baseDir">Base directory path</param>
        /// <returns>The path to the executable if found, null otherwise</returns>
        private static async Task<string> FindHolopatcherExecutableAsync(string resourcesDir, string baseDir)
        {
            FileSystemInfo patcherCliPath = null;

            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                patcherCliPath = new FileInfo(Path.Combine(resourcesDir, "holopatcher.exe"));
                if (patcherCliPath.Exists)
                {
                    await Logger.LogVerboseAsync($"Found holopatcher executable at '{patcherCliPath.FullName}'...").ConfigureAwait(false);
                    return patcherCliPath.FullName;
                }
                await Logger.LogVerboseAsync($"Holopatcher executable not found at '{patcherCliPath.FullName}'...").ConfigureAwait(false);
            }
            else
            {
                string[] possibleOsxPaths = {
                    Path.Combine(resourcesDir, "HoloPatcher.app", "Contents", "MacOS", "holopatcher"),
                    Path.Combine(resourcesDir, "holopatcher"),
                    Path.Combine(baseDir, "Resources", "HoloPatcher.app", "Contents", "MacOS", "holopatcher"),
                    Path.Combine(baseDir, "Resources", "holopatcher"),
                };
                OSPlatform thisOperatingSystem = UtilityHelper.GetOperatingSystem();
                foreach (string path in possibleOsxPaths)
                {
                    patcherCliPath = thisOperatingSystem == OSPlatform.OSX && path.ToLowerInvariant().EndsWith(".app", StringComparison.Ordinal)
                        ? (FileSystemInfo)PathHelper.GetCaseSensitivePath(new DirectoryInfo(path))
                        : (FileSystemInfo)PathHelper.GetCaseSensitivePath(new FileInfo(path));
                    if (patcherCliPath.Exists)
                    {
                        await Logger.LogVerboseAsync($"Found holopatcher executable at '{patcherCliPath.FullName}'...").ConfigureAwait(false);
                        return patcherCliPath.FullName;
                    }
                    await Logger.LogVerboseAsync($"Holopatcher executable not found at '{patcherCliPath.FullName}'...").ConfigureAwait(false);
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the Python version of holopatcher.
        /// </summary>
        /// <param name="resourcesDir">Resources directory path</param>
        /// <returns>The path to the Python source directory if found, null otherwise</returns>
        private static async Task<string> FindHolopatcherPythonAsync(string resourcesDir)
        {
            string holopatcherPyPath = Path.Combine(resourcesDir, "PyKotor", "Tools", "HoloPatcher", "src", "holopatcher");

            if (Directory.Exists(holopatcherPyPath))
            {
                await Logger.LogVerboseAsync($"Found holopatcher Python source at '{holopatcherPyPath}'").ConfigureAwait(false);
                return holopatcherPyPath;
            }

            await Logger.LogVerboseAsync($"Holopatcher Python source not found at '{holopatcherPyPath}'...").ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// Locates holopatcher, checking for platform-specific executable and Python source.
        /// </summary>
        /// <param name="resourcesDir">Resources directory path</param>
        /// <param name="baseDir">Base directory path</param>
        /// <param name="preferPythonVersion">If true, prefer Python version over executable; otherwise prefer executable</param>
        /// <returns>Tuple of (holopatcherPath, usePythonVersion, found)</returns>
        public static async Task<(string holopatcherPath, bool usePythonVersion, bool found)> FindHolopatcherAsync(
            string resourcesDir,
            string baseDir,
            bool preferPythonVersion = false)
        {
            string executablePath = await FindHolopatcherExecutableAsync(resourcesDir, baseDir).ConfigureAwait(false);
            string pythonPath = await FindHolopatcherPythonAsync(resourcesDir).ConfigureAwait(false);

            // If preference is for Python, check Python first
            if (preferPythonVersion)
            {
                if (pythonPath != null)
                {
                    return (pythonPath, true, true);
                }
                if (executablePath != null)
                {
                    await Logger.LogVerboseAsync("Python version not found, falling back to executable version...").ConfigureAwait(false);
                    return (executablePath, false, true);
                }
            }
            else
            {
                // Default behavior: prefer executable
                if (executablePath != null)
                {
                    return (executablePath, false, true);
                }
                if (pythonPath != null)
                {
                    await Logger.LogVerboseAsync("Platform-specific holopatcher not found, using embedded Python version...").ConfigureAwait(false);
                    return (pythonPath, true, true);
                }
            }

            // Not found anywhere
            return (null, false, false);
        }

        /// <summary>
        /// Sets up Python IO streams (stdout/stderr) to capture output.
        /// </summary>
        /// <param name="sys">Python sys module</param>
        private static void SetupPythonIO(dynamic sys)
        {
            dynamic io = Py.Import("io");
            dynamic StringIO = io.StringIO;
            dynamic stdout = StringIO();
            dynamic stderr = StringIO();
            sys.stdout = stdout;
            sys.stderr = stderr;
            Logger.LogVerbose($"[RunHolopatcherPyAsync] Set up StringIO for stdout/stderr");
        }

        /// <summary>
        /// Parses command line arguments and sets up sys.argv for Python execution.
        /// </summary>
        /// <param name="sys">Python sys module</param>
        /// <param name="args">Arguments string to parse</param>
        private static void ParseHolopatcherArgs(dynamic sys, string args)
        {
            dynamic sysArgv = new PyList();
            sysArgv.append("holopatcher");
            if (!string.IsNullOrEmpty(args))
            {
                foreach (string arg in args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    sysArgv.append(arg.Trim('"'));
                }
            }
            sys.argv = sysArgv;
            Logger.LogVerbose($"[RunHolopatcherPyAsync] sys.argv set to: holopatcher {args}");
        }

        /// <summary>
        /// Executes holopatcher's __main__.py file with the proper Python environment setup.
        /// </summary>
        /// <param name="holopatcherPath">Path to the holopatcher Python source directory</param>
        /// <param name="sys">Python sys module</param>
        /// <returns>Tuple of (success, stdout, stderr) or (false, "", error message)</returns>
        private static (bool success, string stdout, string stderr) ExecuteHolopatcherPythonCode(string holopatcherPath, dynamic sys)
        {
            // Find the __main__.py file path
            string mainPyFile = Path.Combine(holopatcherPath, "__main__.py");
            Logger.LogVerbose($"[RunHolopatcherPyAsync] Looking for __main__.py at: {mainPyFile}");

            if (!File.Exists(mainPyFile))
            {
                string errorMsg = $"__main__.py not found at: {mainPyFile}";
                Logger.LogError(errorMsg);
                return (false, "", errorMsg);
            }

            Logger.LogVerbose($"[RunHolopatcherPyAsync] Found __main__.py, reading file...");
            string pythonCode = File.ReadAllText(mainPyFile);

            // Set __file__ so HoloPatcher's path detection works
            Logger.LogVerbose($"[RunHolopatcherPyAsync] Executing __main__.py with __file__ = {mainPyFile}");

            // Create a module-like namespace with __file__ set
            dynamic builtins = Py.Import("builtins");
            dynamic globals = new PyDict();
            globals["__file__"] = mainPyFile.ToPython();
            globals["__name__"] = "__main__".ToPython();
            globals["__builtins__"] = builtins;

            // Execute the Python code in this namespace
            // This is equivalent to running: python __main__.py
            PythonEngine.Exec(pythonCode, globals.Handle, globals.Handle);

            // Capture any output from stdout/stderr
            dynamic stdoutValue = sys.stdout.getvalue();
            dynamic stderrValue = sys.stderr.getvalue();
            string stdout = stdoutValue?.ToString() ?? string.Empty;
            string stderr = stderrValue?.ToString() ?? string.Empty;

            Logger.LogVerbose($"[RunHolopatcherPyAsync] HoloPatcher launched successfully");
            Logger.LogVerbose($"[RunHolopatcherPyAsync] stdout: {stdout}");
            Logger.LogVerbose($"[RunHolopatcherPyAsync] stderr: {stderr}");

            return (true, stdout, stderr);
        }

        /// <summary>
        /// Formats exception information for logging and error reporting.
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="errorType">Type description for the error header</param>
        /// <returns>Formatted error message</returns>
        private static string FormatHolopatcherError(Exception ex, string errorType)
        {
            string fullStackTrace = $@"=== {errorType} ===
Error: {ex.Message}

Full Stack Trace:
{ex.StackTrace}

Exception Type: {ex.GetType().FullName}";
            return fullStackTrace;
        }

        /// <summary>
        /// Runs holopatcher directly using Python.NET with the embedded Python interpreter.
        /// </summary>
        /// <param name="holopatcherPath">Path to the holopatcher Python source directory</param>
        /// <param name="args">Arguments to pass to holopatcher</param>
        /// <returns>Tuple of (exit code, stdout, stderr)</returns>
        public static async Task<(int exitCode, string stdout, string stderr)> RunHolopatcherPyAsync(string holopatcherPath, string args)
        {
            await EnsurePythonInitializedAsync().ConfigureAwait(false);

            return await Task.Run(() =>
            {
                try
                {
                    Logger.LogVerbose($"[RunHolopatcherPyAsync] Starting HoloPatcher from path: {holopatcherPath}");

                    // Run Python code to execute holopatcher's __main__.py file directly
                    // This lets HoloPatcher's own code handle all the path setup
                    using (Py.GIL())
                    {
                        dynamic sys = Py.Import("sys");

                        // Set up stdout/stderr to prevent NoneType write errors
                        SetupPythonIO(sys);

                        // Set sys.argv with the arguments
                        ParseHolopatcherArgs(sys, args);

                        // Execute the holopatcher Python code
                        (bool success, string stdout, string stderr) result = ExecuteHolopatcherPythonCode(holopatcherPath, sys);

                        if (!result.success)
                        {
                            return (1, "", result.stderr);
                        }

                        return (0, result.stdout, result.stderr);
                    }
                }
                catch (PythonException ex)
                {
                    string fullStackTrace = FormatHolopatcherError(ex, "PYTHON ERROR RUNNING HOLOPATCHER");
                    Logger.LogError($"Python error running HoloPatcher: {ex.Message}");
                    Logger.LogVerbose(fullStackTrace);
                    return (1, "", fullStackTrace);
                }
                catch (Exception ex)
                {
                    string fullStackTrace = FormatHolopatcherError(ex, "ERROR RUNNING HOLOPATCHER");
                    Logger.LogError($"Error running HoloPatcher: {ex.Message}");
                    Logger.LogVerbose(fullStackTrace);
                    return (1, "", fullStackTrace);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs holopatcher using whichever version is available (executable or Python).
        /// Automatically finds and determines which version to use.
        /// </summary>
        /// <param name="args">Arguments to pass to holopatcher</param>
        /// <param name="preferPythonVersion">If true, prefer Python version over executable; otherwise prefer executable</param>
        /// <param name="resourcesDir">Resources directory path. If null, will be determined automatically.</param>
        /// <param name="baseDir">Base directory path. If null, will be determined automatically.</param>
        /// <returns>Tuple of (exit code, stdout, stderr)</returns>
        /// <exception cref="FileNotFoundException">Thrown when holopatcher cannot be found</exception>
        public static async Task<(int exitCode, string stdout, string stderr)> RunHolopatcherAsync(
            string args = "",
            bool preferPythonVersion = false,
            string resourcesDir = null,
            string baseDir = null)
        {
            // Auto-determine paths if not provided
            if (baseDir == null)
            {
                baseDir = UtilityHelper.GetBaseDirectory();
            }
            if (resourcesDir == null)
            {
                resourcesDir = UtilityHelper.GetResourcesDirectory(baseDir);
            }

            // Find holopatcher
            (string holopatcherPath, bool usePythonVersion, bool found) = await FindHolopatcherAsync(
                resourcesDir,
                baseDir,
                preferPythonVersion
            ).ConfigureAwait(false);

            if (!found)
            {
                throw new FileNotFoundException(
                    $"HoloPatcher could not be found in the Resources directory: '{resourcesDir}'"
                );
            }

            // Execute the appropriate version
            if (usePythonVersion)
            {
                return await RunHolopatcherPyAsync(holopatcherPath, args).ConfigureAwait(false);
            }

            return await PlatformAgnosticMethods.ExecuteProcessAsync(holopatcherPath, args).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the KPatcher CLI executable: optional user path, then <c>PATH</c>, then common names next to the app / in Resources.
        /// </summary>
        public static async Task<(string path, bool found)> FindKPatcherExecutableAsync(string baseDir = null, string resourcesDir = null)
        {
            if (!string.IsNullOrWhiteSpace(MainConfig.KPatcherExecutablePath))
            {
                string configured = MainConfig.KPatcherExecutablePath.Trim();
                if (File.Exists(configured))
                {
                    await Logger.LogVerboseAsync($"[KPatcher] Using configured executable: {configured}").ConfigureAwait(false);
                    return (configured, true);
                }

                await Logger.LogWarningAsync($"[KPatcher] Configured path not found: {configured}").ConfigureAwait(false);
            }

            baseDir = baseDir ?? UtilityHelper.GetBaseDirectory();
            resourcesDir = resourcesDir ?? UtilityHelper.GetResourcesDirectory(baseDir);

            string[] names =
            {
                "KPatcher",
                "KPatcher.exe",
                "kpatcher",
                "kpatcher.exe",
            };

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            char sep = Path.PathSeparator;
            foreach (string dir in pathEnv.Split(sep, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                try
                {
                    foreach (string name in names)
                    {
                        string candidate = Path.Combine(dir.Trim(), name);
                        if (File.Exists(candidate))
                        {
                            await Logger.LogVerboseAsync($"[KPatcher] Found on PATH: {candidate}").ConfigureAwait(false);
                            return (candidate, true);
                        }
                    }
                }
                catch
                {
                    // ignore invalid PATH segments
                }
            }

            foreach (string name in names)
            {
                string nextToApp = Path.Combine(baseDir, name);
                if (File.Exists(nextToApp))
                {
                    return (nextToApp, true);
                }

                string inResources = Path.Combine(resourcesDir, name);
                if (File.Exists(inResources))
                {
                    return (inResources, true);
                }
            }

            return (null, false);
        }

        /// <summary>Runs TSLPatcher/HoloPatcher/KPatcher with the same CLI shape HoloPatcher expects for <c>--install</c>.</summary>
        public static async Task<(int exitCode, string stdout, string stderr)> RunTslPatcherCliAsync(
            string args,
            Services.FileSystem.IFileSystemProvider fileSystemProvider = null)
        {
            string engine = MainConfig.PatcherEngine ?? PatcherEngines.Holopatcher;
            if (string.Equals(engine, PatcherEngines.KPatcher, StringComparison.OrdinalIgnoreCase))
            {
                (string kPath, bool kFound) = await FindKPatcherExecutableAsync().ConfigureAwait(false);
                if (!kFound)
                {
                    return (1, string.Empty, "KPatcher executable not found. Set the path in Settings or install KPatcher on PATH.");
                }

                string prefix = OperatingSystem.IsWindows() ? string.Empty : "--console ";
                string fullArgs = prefix + args.TrimStart();
                await Logger.LogVerboseAsync($"[KPatcher] {kPath} {fullArgs}").ConfigureAwait(false);
                if (fileSystemProvider != null)
                {
                    return await fileSystemProvider.ExecuteProcessAsync(kPath, fullArgs).ConfigureAwait(false);
                }

                return await PlatformAgnosticMethods.ExecuteProcessAsync(kPath, fullArgs).ConfigureAwait(false);
            }

            return await RunHolopatcherAsync(args).ConfigureAwait(false);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static async Task<(bool success, string informationMessage)> ValidateInstallationEnvironmentAsync(
            [NotNull] MainConfig mainConfig,
            [CanBeNull] Func<string, Task<bool?>> confirmationCallback = null)
        {
            if (mainConfig is null)
            {
                throw new ArgumentNullException(nameof(mainConfig));
            }

            try
            {
                if (MainConfig.DestinationPath is null || MainConfig.SourcePath is null)
                {
                    return (false, "Please set your directories first");
                }

                bool patcherIsExecutable = true;
                bool patcherTestExecute = false;
                string baseDir = UtilityHelper.GetBaseDirectory();
                string resourcesDir = UtilityHelper.GetResourcesDirectory(baseDir);
                string engine = MainConfig.PatcherEngine ?? PatcherEngines.Holopatcher;

                if (string.Equals(engine, PatcherEngines.KPatcher, StringComparison.OrdinalIgnoreCase))
                {
                    (string kPath, bool kFound) = await FindKPatcherExecutableAsync(baseDir, resourcesDir).ConfigureAwait(false);
                    if (!kFound)
                    {
                        return (false,
                            "KPatcher was selected in Settings but no executable was found. Set the KPatcher path in Settings or add it to PATH.");
                    }

                    try
                    {
                        await PlatformAgnosticMethods.MakeExecutableAsync(new FileInfo(kPath)).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        await Logger.LogExceptionAsync(e).ConfigureAwait(false);
                        patcherIsExecutable = false;
                    }

                    string prefix = OperatingSystem.IsWindows() ? string.Empty : "--console ";
                    // --install without paths exits 2; --help exits 0 and proves the CLI runs.
                    (int, string, string) kResult = await PlatformAgnosticMethods.ExecuteProcessAsync(
                        kPath,
                        prefix + "--help"
                    ).ConfigureAwait(false);
                    if (kResult.Item1 == 0)
                    {
                        patcherTestExecute = true;
                    }
                }
                else
                {
                    // HoloPatcher (Resources binary or embedded Python)
                    (string holopatcherPath, bool usePythonVersion, bool found) = await FindHolopatcherAsync(resourcesDir, baseDir).ConfigureAwait(false);

                    if (!found)
                    {
                        return (false,
                            "HoloPatcher could not be found in the Resources directory. Please ensure your AV isn't quarantining it and the files exist.");
                    }

                    if (usePythonVersion)
                    {
                        await Logger.LogVerboseAsync("Initializing embedded Python environment...").ConfigureAwait(false);
                        try
                        {
                            await EnsurePythonInitializedAsync().ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            await Logger.LogExceptionAsync(e).ConfigureAwait(false);
                            patcherIsExecutable = false;
                        }

                        (int, string, string) result = await RunHolopatcherPyAsync(holopatcherPath, "--install").ConfigureAwait(false);
                        if (result.Item1 == 2)
                        {
                            patcherTestExecute = true;
                        }
                    }
                    else
                    {
                        await Logger.LogVerboseAsync("Ensuring the holopatcher binary has executable permissions...").ConfigureAwait(false);
                        try
                        {
                            await PlatformAgnosticMethods.MakeExecutableAsync(new FileInfo(holopatcherPath)).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            await Logger.LogExceptionAsync(e).ConfigureAwait(false);
                            patcherIsExecutable = false;
                        }

                        (int, string, string) result = await PlatformAgnosticMethods.ExecuteProcessAsync(
                            holopatcherPath,
                            args: "--install"
                        ).ConfigureAwait(false);
                        if (result.Item1 == 2)
                        {
                            patcherTestExecute = true;
                        }
                    }
                }

                if (MainConfig.AllComponents.IsNullOrEmptyCollection())
                {
                    return (false, "No instructions loaded! Press 'Load Instructions File' or create some instructions first.");
                }

                if (!MainConfig.AllComponents.Exists(component => component.IsSelected))
                {
                    return (false, "Select at least one mod in the left list to be installed first.");
                }

                await Logger.LogAsync("Finding duplicate case-insensitive folders/files in the install destination...").ConfigureAwait(false);
                IEnumerable<FileSystemInfo> duplicates = PathHelper.FindCaseInsensitiveDuplicates(MainConfig.DestinationPath.FullName);
                var fileSystemInfos = duplicates.ToList();
                foreach (FileSystemInfo duplicate in fileSystemInfos)
                {
                    await Logger.LogErrorAsync(duplicate?.FullName + " has a duplicate, please resolve before attempting an install.").ConfigureAwait(false);
                }

                await Logger.LogAsync("Checking for duplicate components...").ConfigureAwait(false);
                bool noDuplicateComponents = await ComponentManagerService.FindDuplicateComponentsAsync(MainConfig.AllComponents).ConfigureAwait(false);

                await Logger.LogAsync("Ensuring both the mod directory and the install directory are writable...").ConfigureAwait(false);
                bool isInstallDirectoryWritable = UtilityHelper.IsDirectoryWritable(MainConfig.DestinationPath);
                bool isModDirectoryWritable = UtilityHelper.IsDirectoryWritable(MainConfig.SourcePath);
                if (
                    !isInstallDirectoryWritable
                    && confirmationCallback != null
                    && await confirmationCallback("The Install directory is not writable! Would you like to attempt to gain access now?").ConfigureAwait(false) == true
                )
                {
                    await FilePermissionHelper.FixPermissionsAsync(MainConfig.DestinationPath).ConfigureAwait(false);
                    isInstallDirectoryWritable = UtilityHelper.IsDirectoryWritable(MainConfig.DestinationPath);
                }
                if (
                    !isModDirectoryWritable
                    && confirmationCallback != null
                    && await confirmationCallback("Your mod directory is not writable! Would you like to attempt to gain access now?").ConfigureAwait(false) == true
                )
                {
                    await FilePermissionHelper.FixPermissionsAsync(MainConfig.SourcePath).ConfigureAwait(false);
                    isModDirectoryWritable = UtilityHelper.IsDirectoryWritable(MainConfig.SourcePath);
                }
                await Logger.LogAsync("Validating individual components, this might take a while...").ConfigureAwait(false);
                bool individuallyValidated = true;
                foreach (ModComponent component in MainConfig.AllComponents)
                {
                    if (!component.IsSelected)
                    {
                        continue;
                    }

                    if (component.Restrictions.Count > 0 && component.IsSelected)
                    {
                        List<ModComponent> restrictedComponentsList = ModComponent.FindComponentsFromGuidList(
                            component.Restrictions,
                            MainConfig.AllComponents
                        );
                        foreach (ModComponent restrictedComponent in restrictedComponentsList)
                        {

                            if (restrictedComponent?.IsSelected == true)
                            {
                                await Logger.LogErrorAsync($"Cannot install '{component.Name}' due to '{restrictedComponent.Name}' being selected for install.").ConfigureAwait(false);
                                individuallyValidated = false;
                            }
                        }
                    }

                    if (component.Dependencies.Count > 0 && component.IsSelected)
                    {
                        List<ModComponent> dependencyComponentsList = ModComponent.FindComponentsFromGuidList(component.Dependencies, MainConfig.AllComponents);
                        foreach (ModComponent dependencyComponent in dependencyComponentsList)
                        {

                            if (dependencyComponent?.IsSelected != true)
                            {
                                await Logger.LogErrorAsync($"Cannot install '{component.Name}' due to '{dependencyComponent?.Name}' not being selected for install.").ConfigureAwait(false);
                                individuallyValidated = false;
                            }
                        }
                    }

                    var validator = new ComponentValidation(component, MainConfig.AllComponents);
                    await Logger.LogVerboseAsync($" == Validating '{component.Name}' == ").ConfigureAwait(false);
                    individuallyValidated &= validator.Run();
                }

                await Logger.LogVerboseAsync("Finished validating all components.").ConfigureAwait(false);

                string informationMessage = string.Empty;

                if (!patcherIsExecutable)
                {
                    informationMessage = "The patcher binary does not seem to be executable, please see the logs in the output window for more information.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (!patcherTestExecute)
                {
                    informationMessage = "The patcher test execution did not pass, this may mean the"
                    + " binary is corrupted or has unresolved dependency problems.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (!isInstallDirectoryWritable)
                {
                    informationMessage = "The Install directory is not writable!"
                        + " Please ensure administrative privileges or reinstall KOTOR"
                        + " to a directory with write access.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (!isModDirectoryWritable)
                {
                    informationMessage = "The Mod directory is not writable!"
                        + " Please ensure administrative privileges or choose a new mod directory.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (!noDuplicateComponents)
                {
                    informationMessage = "There were several duplicate components found."
                        + " Please ensure all components are unique and none have conflicting GUIDs.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (!individuallyValidated)
                {
                    informationMessage =
                        $"Some components failed to validate. Check the output/console window for details.{Environment.NewLine}If you are seeing this as an end user you most"
                        + " likely need to whitelist KOTORModSync and HoloPatcher in your antivirus, or download the missing mods.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                if (fileSystemInfos.Count != 0)
                {
                    informationMessage =
                        "You have duplicate files/folders in your installation directory in a case-insensitive environment."
                        + "Please resolve these before continuing. Check the output window for the specific files to resolve.";
                    await Logger.LogErrorAsync(informationMessage).ConfigureAwait(false);
                }

                return string.IsNullOrWhiteSpace(informationMessage)
                    ? (success: true, informationMessage: "No issues found. If you encounter any problems during the installation, please submit a bug report.")
                    : (success: false, informationMessage: informationMessage);
            }
            catch (Exception e)
            {
                await Logger.LogExceptionAsync(e).ConfigureAwait(false);
                return (success: false, informationMessage: "Unknown error, check the output window for more information.");
            }
        }


        public static async Task<ModComponent.InstallExitCode> InstallSingleComponentAsync(
            [NotNull] ModComponent component,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> allComponents,
            CancellationToken cancellationToken = default)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (allComponents is null)
            {
                throw new ArgumentNullException(nameof(allComponents));
            }

            var validator = new ComponentValidation(component, allComponents.ToList());
            await Logger.LogVerboseAsync($" == Validating '{component.Name}' == ").ConfigureAwait(false);
            if (!validator.Run())
            {
                return ModComponent.InstallExitCode.InvalidOperation;
            }

            return await component.InstallAsync(allComponents.ToList(), cancellationToken).ConfigureAwait(false);
        }


        public static async Task<ModComponent.InstallExitCode> InstallAllSelectedComponentsAsync(
            [NotNull][ItemNotNull] List<ModComponent> allComponents,
            [CanBeNull] Action<int, int, string> progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (allComponents is null)
            {
                throw new ArgumentNullException(nameof(allComponents));
            }

            var coordinator = new InstallCoordinator();
            DirectoryInfo destination = MainConfig.DestinationPath
                                        ?? throw new InvalidOperationException("DestinationPath must be set before installing.");
            ResumeResult resume = await coordinator.InitializeAsync(allComponents, destination, cancellationToken).ConfigureAwait(false);
            var orderedComponents = resume.OrderedComponents.Where(component => component.IsSelected).ToList();
            int total = orderedComponents.Count;
            ModComponent.InstallExitCode exitCode = ModComponent.InstallExitCode.Success;

            for (int index = 0; index < orderedComponents.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModComponent component = orderedComponents[index];

                progressCallback?.Invoke(index, total, component.Name);

                switch (component.InstallState)
                {
                    case ModComponent.ComponentInstallState.Completed:
                        await Logger.LogAsync($"Skipping '{component.Name}' (already completed).").ConfigureAwait(false);
                        coordinator.CheckpointManager.UpdateComponentState(component);
                        await coordinator.CheckpointManager.SaveAsync().ConfigureAwait(false);
                        continue;
                    case ModComponent.ComponentInstallState.Skipped:
                    case ModComponent.ComponentInstallState.Blocked:
                        await Logger.LogAsync($"Skipping '{component.Name}' (blocked by dependency).").ConfigureAwait(false);
                        coordinator.CheckpointManager.UpdateComponentState(component);
                        await coordinator.CheckpointManager.SaveAsync().ConfigureAwait(false);
                        continue;
                }

                await Logger.LogAsync($"Start install of '{component.Name}'...").ConfigureAwait(false);
                exitCode = await component.InstallAsync(allComponents, cancellationToken).ConfigureAwait(false);
                coordinator.CheckpointManager.UpdateComponentState(component);

                if (exitCode == ModComponent.InstallExitCode.Success)
                {
                    await Logger.LogAsync($"Install of '{component.Name}' succeeded.").ConfigureAwait(false);

                    // Create checkpoint after successful installation
                    try
                    {
                        CheckpointInfo checkpoint = await coordinator.CheckpointService.CreateCheckpointAsync(
                            component,
                            index + 1,
                            total,
                            cancellationToken
                        ).ConfigureAwait(false);

                        coordinator.CheckpointManager.State.ComponentCheckpoints[component.Guid] = checkpoint.CommitId;
                        await Logger.LogAsync($"✓ Checkpoint created: {checkpoint.ShortCommitId}").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogWarningAsync($"Failed to create checkpoint for '{component.Name}': {ex.Message}").ConfigureAwait(false);
                    }

                    await coordinator.CheckpointManager.PromoteSnapshotAsync(destination, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogErrorAsync($"Install of '{component.Name}' failed with exit code {exitCode}").ConfigureAwait(false);
                    InstallCoordinator.MarkBlockedDescendants(orderedComponents, component.Guid);
                    foreach (ModComponent blocked in orderedComponents.Where(c => c.InstallState == ModComponent.ComponentInstallState.Blocked))
                    {
                        coordinator.CheckpointManager.UpdateComponentState(blocked);
                    }
                    await coordinator.CheckpointManager.SaveAsync().ConfigureAwait(false);
                    break;
                }

                await coordinator.CheckpointManager.SaveAsync().ConfigureAwait(false);
            }

            return exitCode;
        }

        public static Task<ModComponent.InstallExitCode> InstallAllSelectedComponentsAsync(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> allComponents,
            [CanBeNull] Action<int, int, string> progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (allComponents is null)
            {
                throw new ArgumentNullException(nameof(allComponents));
            }

            return InstallAllSelectedComponentsAsync(allComponents.ToList(), progressCallback, cancellationToken);
        }

    }
}
