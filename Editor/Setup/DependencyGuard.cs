using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace LookAnimation.Setup
{
    /// <summary>
    /// Keeps Look Animator from ever breaking a project that doesn't have its dependencies yet.
    /// </summary>
    /// <remarks>
    /// This assembly references nothing, so it always compiles. Whenever scripts reload or an assembly
    /// definition appears or disappears, it looks for each dependency's assembly definition and sets or
    /// clears that dependency's scripting define symbol (e.g. <c>HAS_UNITASK</c>). Look Animator's own
    /// assemblies list those symbols as Define Constraints, so while a dependency is missing they are
    /// simply left out of compilation - no errors - and this guard offers to install what's missing:
    /// <list type="bullet">
    /// <item>Unity and third-party packages (UniTask) through the Package Manager.</item>
    /// <item>The author's own systems (Veneer) by downloading their repository into
    /// <c>Assets/Scripts/...</c> - exactly as if it had been copied there by hand, so every file stays
    /// visible and editable.</item>
    /// </list>
    /// It also keeps <c>HAS_LOOK_ANIMATOR</c> set while Look Animator is in the project, so code that
    /// uses it from outside (the Combat System bridge, say) can be left out of compilation once it's removed.
    /// When Look Animator is deleted, the guard clears its own symbol and sets the shared ones to what is still installed,
    /// so other systems' assemblies that need them keep compiling.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class DependencyGuard : AssetPostprocessor, IActiveBuildTargetChanged
    {
        internal const string SystemName = "Look Animator";
        private const string DeclinedKey = "LookAnimator.Setup.DeclinedDependencies";
        private const string SetupAsmdefFile = "LookAnimator.Setup.asmdef";
        private const string OwnDefine = "HAS_LOOK_ANIMATOR";
        private const string Branch = "main";

        /// <summary>Everything Look Animator uses. UniTask is required; the Veneer is optional and only gives the Inspector its studio look.</summary>
        internal static readonly Dependency[] Dependencies =
        {
            Dependency.Package("UniTask", "UniTask", "HAS_UNITASK", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask", "com.cysharp.unitask"),
            Dependency.Repository("Veneer", "Veneer.Editor", "HAS_VENEER", "https://github.com/fatihgezerx/Veneer", "Assets/Scripts/Veneer", "for the studio-style Inspector (without it Unity's default Inspector is used)"),
        };

        private static AddAndRemoveRequest _packageRequest;
        private static readonly List<Download> Downloads = new();

        private sealed class Download
        {
            public Dependency Dependency;
            public UnityWebRequest Request;
        }

        static DependencyGuard()
        {
            Events.registeringPackages += OnRegisteringPackages;
            EditorApplication.delayCall += () => Refresh(true);
        }

        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => Refresh(false);

        // An assembly definition appeared or disappeared (a package or folder added / deleted): update the
        // symbols right away, during this import, so the compilation that follows already uses them.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Look Animator itself is being deleted: clear every symbol it manages, or a leftover one
            // (e.g. HAS_EVENT_SYSTEM once Event System is gone too) would let a later copy compile against
            // a missing dependency. Also forget an earlier "Not now", so a fresh copy asks again.
            if (ContainsFile(deleted, SetupAsmdefFile))
            {
                SessionState.EraseString(DeclinedKey);
                // Dependency symbols are shared with other systems' assemblies (their Define Constraints), so they
                // are set to what is actually installed now - never cleared blindly, or those assemblies would be
                // left out of compilation while everything they need is still there.
                var symbols = new Dictionary<string, bool> { [OwnDefine] = false };
                var assemblies = FindAssemblyDefinitions();
                foreach (var dependency in Dependencies)
                {
                    symbols[dependency.Define] = assemblies.ContainsKey(dependency.Assembly);
                }

                ApplyDefines(symbols);
                return;
            }

            // Imported again (e.g. a newer copy): ask again too.
            if (ContainsFile(imported, SetupAsmdefFile))
            {
                SessionState.EraseString(DeclinedKey);
            }

            if (ContainsAsmdef(imported) || ContainsAsmdef(deleted) || ContainsAsmdef(moved))
            {
                Refresh(false);
            }
        }

        // A package is about to be removed: clear its symbol before its code disappears, so nothing
        // tries to compile against it in between.
        private static void OnRegisteringPackages(PackageRegistrationEventArgs args)
        {
            var symbols = new Dictionary<string, bool>();
            foreach (var removed in args.removed)
            {
                foreach (var dependency in Dependencies)
                {
                    if (dependency.PackageName == removed.name)
                    {
                        symbols[dependency.Define] = false;
                    }
                }
            }

            ApplyDefines(symbols);
        }

        /// <summary>Updates every symbol; returns true if anything is missing. Offers to install it when <paramref name="prompt"/>.</summary>
        internal static bool Refresh(bool prompt)
        {
            var assemblies = FindAssemblyDefinitions();

            // Look Animator was deleted, but this code is still loaded: Unity keeps the old scripts
            // while the project has compile errors (e.g. from code that used the deleted system). Its
            // symbols were cleared when it was deleted, so don't set any of them again.
            if (!assemblies.ContainsKey(Path.GetFileNameWithoutExtension(SetupAsmdefFile)))
            {
                return false;
            }

            var symbols = new Dictionary<string, bool> { [OwnDefine] = true };
            var missing = new List<Dependency>();

            foreach (var dependency in Dependencies)
            {
                var present = assemblies.ContainsKey(dependency.Assembly);
                symbols[dependency.Define] = present;
                if (!present)
                {
                    missing.Add(dependency);
                }
            }

            ApplyDefines(symbols);

            if (missing.Count == 0)
            {
                return false;
            }

            // Asked whenever something is missing, unless "Not now" was already picked for all of it in
            // this editor session (a dependency that goes missing later is asked about again).
            if (prompt && !IsInstalling && !Application.isBatchMode && !WasDeclined(missing))
            {
                Prompt(missing);
            }

            return true;
        }

        /// <summary>Whether every dependency, optional ones included, is installed.</summary>
        internal static bool AllPresent()
        {
            var assemblies = FindAssemblyDefinitions();
            foreach (var dependency in Dependencies)
            {
                if (!assemblies.ContainsKey(dependency.Assembly))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Every assembly definition in Assets and Packages: file name (the assembly name, by convention) -> path.</summary>
        internal static Dictionary<string, string> FindAssemblyDefinitions()
        {
            var result = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                result[Path.GetFileNameWithoutExtension(path)] = path;
            }

            return result;
        }

        private static bool IsInstalling => _packageRequest != null || Downloads.Count > 0;

        private static void Prompt(List<Dependency> missing)
        {
            var required = new StringBuilder();
            var optional = new StringBuilder();
            foreach (var dependency in missing)
            {
                var line = dependency.IsOptional ? optional : required;
                line.Append("• ").Append(dependency.Name);
                if (dependency.IsRepository)
                {
                    line.Append(" (into ").Append(dependency.TargetFolder).Append(')');
                }

                if (dependency.IsOptional)
                {
                    line.Append(" - ").Append(dependency.Purpose);
                }

                line.AppendLine();
            }

            var message = new StringBuilder();
            if (required.Length > 0)
            {
                message.Append(SystemName).AppendLine(" needs these packages:").AppendLine().Append(required).AppendLine();
            }

            if (optional.Length > 0)
            {
                message.AppendLine("Optional:").AppendLine().Append(optional).AppendLine();
            }

            message.Append("Until they are installed, the parts of ").Append(SystemName)
                .Append(" that use them are left out of compilation, so the project keeps compiling.");

            // Only one kind missing: a plain Install / Not now.
            if (required.Length == 0 || optional.Length == 0)
            {
                if (EditorUtility.DisplayDialog(SystemName, message.ToString(), "Install", "Not now"))
                {
                    Install(missing);
                }
                else
                {
                    Decline(missing);
                }

                return;
            }

            // Both: the required ones can be installed on their own. Closing the dialog counts as "Not now".
            switch (EditorUtility.DisplayDialogComplex(SystemName, message.ToString(), "Install All", "Not now", "Install Required Only"))
            {
                case 0:
                    Install(missing);
                    break;
                case 2:
                    var requiredOnly = missing.FindAll(dependency => !dependency.IsOptional);
                    Decline(missing.FindAll(dependency => dependency.IsOptional));
                    Install(requiredOnly);
                    break;
                default:
                    Decline(missing);
                    break;
            }
        }

        // Not asked about these again in this editor session (see WasDeclined).
        private static void Decline(List<Dependency> dependencies)
        {
            var names = new List<string>();
            foreach (var dependency in dependencies)
            {
                names.Add(dependency.Name);
            }

            SessionState.SetString(DeclinedKey, string.Join("\n", names));
        }

        private static bool WasDeclined(List<Dependency> missing)
        {
            var declined = new HashSet<string>(SessionState.GetString(DeclinedKey, string.Empty).Split('\n'));
            foreach (var dependency in missing)
            {
                if (!declined.Contains(dependency.Name))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Install(List<Dependency> dependencies)
        {
            var packages = new List<string>();
            foreach (var dependency in dependencies)
            {
                if (dependency.IsRepository)
                {
                    StartDownload(dependency);
                }
                else
                {
                    packages.Add(dependency.InstallId);
                }
            }

            if (packages.Count > 0)
            {
                Debug.Log($"[{SystemName}] Installing: {string.Join(", ", packages)}");
                _packageRequest = Client.AddAndRemove(packages.ToArray());
            }

            EditorApplication.update -= WaitForInstall;
            EditorApplication.update += WaitForInstall;
        }

        // A repository is downloaded as a zip of its main branch and unpacked into its target folder -
        // .meta files included, so the result is identical to copying the repository by hand.
        private static void StartDownload(Dependency dependency)
        {
            var url = dependency.InstallId.TrimEnd('/') + "/archive/refs/heads/" + Branch + ".zip";
            Debug.Log($"[{SystemName}] Downloading {dependency.Name} into {dependency.TargetFolder}");
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            Downloads.Add(new Download { Dependency = dependency, Request = request });
        }

        private static void WaitForInstall()
        {
            var extracted = false;
            for (var i = Downloads.Count - 1; i >= 0; i--)
            {
                var download = Downloads[i];
                if (!download.Request.isDone)
                {
                    continue;
                }

                if (download.Request.result == UnityWebRequest.Result.Success)
                {
                    extracted |= Extract(download.Request.downloadHandler.data, download.Dependency);
                }
                else
                {
                    Debug.LogError($"[{SystemName}] Couldn't download {download.Dependency.Name}: {download.Request.error}\n" +
                                   $"Download {download.Dependency.InstallId} yourself and copy it into {download.Dependency.TargetFolder}.");
                }

                download.Request.Dispose();
                Downloads.RemoveAt(i);
            }

            if (extracted)
            {
                AssetDatabase.Refresh();
            }

            if (_packageRequest != null && _packageRequest.IsCompleted)
            {
                if (_packageRequest.Status == StatusCode.Failure)
                {
                    Debug.LogError($"[{SystemName}] Couldn't install the packages: {_packageRequest.Error?.message}\n" +
                                   "Git URLs need Git installed. You can also add them in Window > Package Manager > + > Add package from git URL.");
                }

                _packageRequest = null;
            }

            if (!IsInstalling)
            {
                EditorApplication.update -= WaitForInstall;
            }
        }

        // Unpacks the zip's top folder (e.g. "UniMVC-main/") into the target folder. Files that already
        // exist are kept, never overwritten; Git's own files (.gitignore, .github/...) are skipped.
        private static bool Extract(byte[] zip, Dependency dependency)
        {
            var written = 0;
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var slash = entry.FullName.IndexOf('/');
                    var relative = slash >= 0 ? entry.FullName.Substring(slash + 1) : string.Empty;
                    if (relative.Length == 0 || relative.EndsWith("/") || IsGitFile(relative))
                    {
                        continue;
                    }

                    var path = Path.Combine(dependency.TargetFolder, relative);
                    if (File.Exists(path))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var input = entry.Open())
                    using (var output = File.Create(path))
                    {
                        input.CopyTo(output);
                    }

                    written++;
                }
            }

            Debug.Log($"[{SystemName}] {dependency.Name}: {written} file(s) added to {dependency.TargetFolder}.");
            return written > 0;
        }

        // Only Git's own files: other hidden files are part of the system, e.g. the MVC/.pending marker
        // that makes MvcIntegration add its MVC scripts once the downloaded system is loaded.
        private static bool IsGitFile(string relativePath)
        {
            foreach (var part in relativePath.Split('/'))
            {
                if (part.StartsWith(".git"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyDefines(Dictionary<string, bool> symbols)
        {
            if (symbols.Count == 0)
            {
                return;
            }

            var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            PlayerSettings.GetScriptingDefineSymbols(target, out var current);

            var defines = new List<string>(current);
            var changed = false;
            foreach (var pair in symbols)
            {
                var has = defines.Contains(pair.Key);
                if (pair.Value && !has)
                {
                    defines.Add(pair.Key);
                    changed = true;
                }
                else if (!pair.Value && has)
                {
                    defines.Remove(pair.Key);
                    changed = true;
                }
            }

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
            }
        }

        private static bool ContainsFile(string[] paths, string fileName)
        {
            foreach (var path in paths)
            {
                if (Path.GetFileName(path) == fileName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAsmdef(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".asmdef"))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One package Look Animator uses.</summary>
    internal readonly struct Dependency
    {
        /// <summary>Shown to the user.</summary>
        public readonly string Name;

        /// <summary>Its assembly definition's name; the dependency counts as installed when it exists.</summary>
        public readonly string Assembly;

        /// <summary>Scripting define symbol set while it is installed.</summary>
        public readonly string Define;

        /// <summary>A Package Manager id (package name or git URL), or a GitHub repository URL for repositories.</summary>
        public readonly string InstallId;

        /// <summary>Packages only: the package name, to notice it being removed.</summary>
        public readonly string PackageName;

        /// <summary>Repositories only: the folder the repository is unpacked into.</summary>
        public readonly string TargetFolder;

        /// <summary>Null for required dependencies; for optional ones, what they enable.</summary>
        public readonly string Purpose;

        private Dependency(string name, string assembly, string define, string installId, string packageName,
            string targetFolder, string purpose)
        {
            Name = name;
            Assembly = assembly;
            Define = define;
            InstallId = installId;
            PackageName = packageName;
            TargetFolder = targetFolder;
            Purpose = purpose;
        }

        /// <summary>A Unity or third-party package, installed through the Package Manager.</summary>
        public static Dependency Package(string name, string assembly, string define, string installId, string packageName,
            string purpose = null) =>
            new(name, assembly, define, installId, packageName, null, purpose);

        /// <summary>A GitHub repository, downloaded into <paramref name="targetFolder"/> like a manual copy.</summary>
        public static Dependency Repository(string name, string assembly, string define, string repositoryUrl,
            string targetFolder, string purpose = null) =>
            new(name, assembly, define, repositoryUrl, null, targetFolder, purpose);

        public bool IsOptional => Purpose != null;

        public bool IsRepository => TargetFolder != null;
    }
}
