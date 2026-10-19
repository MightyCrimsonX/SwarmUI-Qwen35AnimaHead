using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace Qwen35AnimaHead;

/// <summary>SwarmUI extension providing seamless compatibility for the Qwen3.5-0.8B + Anima head text encoder via nawka12/comfyui-qwen35-anima-head.</summary>
public class Qwen35AnimaHeadExtension : Extension
{
    /// <summary>Git repository URL for the underlying ComfyUI custom node.</summary>
    public const string ComfyRepoUrl = "https://github.com/nawka12/comfyui-qwen35-anima-head";

    /// <summary>Feature ID used in ComfyUI backend feature detection.</summary>
    public const string FeatureId = "qwen35_anima_head";

    /// <summary>ComfyUI custom node class name.</summary>
    public const string NodeClassType = "Qwen35AnimaCLIPLoader";

    /// <summary>Parameter group for Qwen 3.5 Anima Text Encoder settings.</summary>
    public static T2IParamGroup AnimaGroup;

    /// <summary>Optional explicit model selector for the Qwen 3.5 Anima text encoder.</summary>
    public static T2IRegisteredParam<T2IModel> Qwen35AnimaClipModelParam;

    /// <summary>Whether to force using the Qwen 3.5 Anima head loader.</summary>
    public static T2IRegisteredParam<bool> ForceQwen35AnimaLoaderParam;

    /// <summary>Execution device for the text encoder loader ('default' or 'cpu').</summary>
    public static T2IRegisteredParam<string> DeviceParam;

    /// <summary>Lock object to ensure concurrent generation calls don't duplicate git clone operations.</summary>
    private static readonly object InstallLock = new();

    /// <summary>Discovers potential custom_nodes directory paths across configured and local ComfyUI backends.</summary>
    public static List<string> GetComfyCustomNodesDirectories()
    {
        HashSet<string> directories = [];

        // 1. Check all configured backends in SwarmUI
        foreach (BackendHandler.BackendData data in Program.Backends.AllBackends.Values)
        {
            if (data.AbstractBackend is ComfyUISelfStartBackend selfStart && selfStart.Settings is not null)
            {
                if (!string.IsNullOrWhiteSpace(selfStart.Settings.StartScript))
                {
                    string scriptPath = Path.GetFullPath(selfStart.Settings.StartScript);
                    string comfyDir = Path.GetDirectoryName(scriptPath);
                    if (Directory.Exists(comfyDir))
                    {
                        directories.Add(Path.Combine(comfyDir, "custom_nodes"));
                    }
                }
            }
        }

        // 2. Check standard portable paths for Windows and Linux
        string winDefault = Path.GetFullPath("dlbackend/comfy/ComfyUI/custom_nodes");
        if (Directory.Exists(Path.GetDirectoryName(winDefault)))
        {
            directories.Add(winDefault);
        }
        string linuxDefault = Path.GetFullPath("dlbackend/ComfyUI/custom_nodes");
        if (Directory.Exists(Path.GetDirectoryName(linuxDefault)))
        {
            directories.Add(linuxDefault);
        }

        // 3. Check Swarm's DLNodes folder
        if (!string.IsNullOrWhiteSpace(ComfyUIBackendExtension.Folder))
        {
            string dlNodes = Path.GetFullPath(ComfyUIBackendExtension.Folder + "/DLNodes");
            if (Directory.Exists(dlNodes))
            {
                directories.Add(dlNodes);
            }
        }

        if (directories.Count == 0)
        {
            directories.Add(winDefault);
        }

        return [.. directories];
    }

    /// <summary>Ensures the comfyui-qwen35-anima-head extension is cloned into the ComfyUI backend's custom_nodes folder.</summary>
    public static async Task<bool> EnsureCustomNodeInstalled()
    {
        lock (InstallLock)
        {
            List<string> customNodesDirs = GetComfyCustomNodesDirectories();
            if (customNodesDirs.Any(dir => Directory.Exists(Path.Combine(dir, "comfyui-qwen35-anima-head"))))
            {
                return false;
            }

            string targetCustomNodes = customNodesDirs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(targetCustomNodes))
            {
                return false;
            }

            bool anyCloned = false;
            try
            {
                if (!Directory.Exists(targetCustomNodes))
                {
                    Directory.CreateDirectory(targetCustomNodes);
                }
                Logs.Info($"[Qwen35Anima] Extension not found in ComfyUI custom_nodes. Cloning {ComfyRepoUrl} into '{targetCustomNodes}'...");
                string cloneOutput = Utilities.RunGitProcess($"clone {ComfyRepoUrl}", targetCustomNodes).GetAwaiter().GetResult();
                Logs.Info($"[Qwen35Anima] Git clone output: {cloneOutput.Trim()}");
                anyCloned = true;
            }
            catch (Exception ex)
            {
                Logs.Error($"[Qwen35Anima] Failed to clone comfyui-qwen35-anima-head into '{targetCustomNodes}': {ex.ReadableString()}");
            }

            if (anyCloned)
            {
                ComfyUISelfStartBackend[] running = [.. Program.Backends.RunningBackendsOfType<ComfyUISelfStartBackend>()];
                if (running.Length > 0)
                {
                    Logs.Info("[Qwen35Anima] Restarting running ComfyUI backend(s) to load the new custom node...");
                    Task[] shutdownTasks = [.. running.Select(b => Program.Backends.ShutdownBackendCleanly(b.BackendData))];
                    Task.WhenAll(shutdownTasks).GetAwaiter().GetResult();
                    foreach (ComfyUISelfStartBackend backend in running)
                    {
                        Program.Backends.DoInitBackend(backend.BackendData);
                    }
                }
            }

            return anyCloned;
        }
    }

    /// <summary>Checks whether a given text encoder filename or identifier matches Qwen 3.5 Anima Head text encoder patterns.</summary>
    public static bool IsQwen35AnimaHeadModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        string clean = name.ToLowerFast().Replace('\\', '/').AfterLast('/').Replace('-', '_');
        return clean.Contains("anima_head")
            || clean.Contains("imgloss")
            || (clean.Contains("qwen_3_5") && clean.Contains("anima"))
            || (clean.Contains("qwen35") && clean.Contains("anima"))
            || (clean.Contains("qwen3_5") && clean.Contains("anima"))
            || clean.Contains("qwen_3_5_08b")
            || clean.Contains("qwen35_08b");
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        Logs.Init("Loading Qwen 3.5 Anima Head Text Encoder extension...");

        // Map custom node class to Swarm feature
        ComfyUIBackendExtension.NodeToFeatureMap[NodeClassType] = FeatureId;

        // Register as an installable feature in SwarmUI
        InstallableFeatures.RegisterInstallableFeature(new(
            "Qwen3.5 Anima Text Encoder",
            FeatureId,
            ComfyRepoUrl,
            "nawka12",
            "Loads the Qwen3.5-0.8B + head text encoder (qwen_3_5_08b_anima_head_L20 / imgloss files) for Anima models."
        ));

        // Ensure custom node is ready at server startup rather than waiting for generation time
        Program.ReadyEvent += () =>
        {
            Task.Run(EnsureCustomNodeInstalled);
        };

        AnimaGroup = new(
            "Anima Qwen 3.5",
            Toggles: false,
            Open: false,
            IsAdvanced: true,
            OrderPriority: 25,
            Description: "Settings for Qwen 3.5 0.8B + Anima Head text encoder compatibility (comfyui-qwen35-anima-head)."
        );

        ForceQwen35AnimaLoaderParam = T2IParamTypes.Register<bool>(new(
            Name: "[Anima] Force Qwen 3.5 Anima Loader",
            Description: "Force using the custom Qwen35AnimaCLIPLoader node for the active Qwen text encoder in Anima.",
            Default: "false",
            Toggleable: true,
            IsAdvanced: true,
            Group: AnimaGroup,
            OrderPriority: 1
        ));

        Qwen35AnimaClipModelParam = T2IParamTypes.Register<T2IModel>(new(
            Name: "[Anima] Qwen 3.5 Text Encoder",
            Description: "Explicitly select the Qwen 3.5 0.8B + head text encoder model file (e.g. qwen_3_5_08b_anima_head_L20.safetensors). When set or selected in 'Qwen Model', Qwen35AnimaCLIPLoader is automatically used.",
            Default: "",
            IgnoreIf: "",
            Subtype: "Clip",
            Permission: Permissions.ModelParams,
            Toggleable: true,
            IsAdvanced: true,
            Group: AnimaGroup,
            OrderPriority: 2
        ));

        DeviceParam = T2IParamTypes.Register<string>(new(
            Name: "[Anima] Qwen 3.5 Loader Device",
            Description: "Device on which to load the Qwen 3.5 text encoder ('default' or 'cpu').",
            Default: "default",
            GetValues: _ => ["default", "cpu"],
            IsAdvanced: true,
            Group: AnimaGroup,
            OrderPriority: 3
        ));

        // Hook into WorkflowGenerator model loading at priority -15 (runs before LoRAs at -14)
        WorkflowGenerator.AddModelGenStep(g =>
        {
            bool hasExplicit = g.UserInput.TryGet(Qwen35AnimaClipModelParam, out T2IModel explicitClip) && explicitClip is not null;
            bool isForced = g.UserInput.Get(ForceQwen35AnimaLoaderParam, false);
            bool hasQwenParam = g.UserInput.TryGet(T2IParamTypes.QwenModel, out T2IModel qwenModel) && qwenModel is not null;

            string modelName = null;
            bool shouldUseCustomLoader = false;

            if (hasExplicit)
            {
                modelName = explicitClip.ToString(g.ModelFolderFormat);
                shouldUseCustomLoader = true;
            }
            else if (hasQwenParam && IsQwen35AnimaHeadModel(qwenModel.Name))
            {
                modelName = qwenModel.ToString(g.ModelFolderFormat);
                shouldUseCustomLoader = true;
            }
            else if (isForced)
            {
                shouldUseCustomLoader = true;
                if (hasQwenParam)
                {
                    modelName = qwenModel.ToString(g.ModelFolderFormat);
                }
            }

            string existingClipName = null;
            if (g.LoadingClip is not null)
            {
                string currentClipNodeId = g.LoadingClip[0].ToString();
                if (g.Workflow.TryGetValue(currentClipNodeId, out JToken existingClipToken) && existingClipToken is JObject existingClipObj)
                {
                    existingClipName = existingClipObj["inputs"]?["clip_name"]?.ToString() ?? "";
                }
            }

            // Also inspect existing LoadingClip node in case Anima model loader resolved the text encoder
            if (!shouldUseCustomLoader && !string.IsNullOrWhiteSpace(existingClipName) && IsQwen35AnimaHeadModel(existingClipName))
            {
                shouldUseCustomLoader = true;
                modelName = existingClipName;
            }

            if (!shouldUseCustomLoader)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = existingClipName;
            }
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "qwen_3_5_08b_anima_head_L20.safetensors";
            }

            // Automatically clone if not installed in custom_nodes
            EnsureCustomNodeInstalled().GetAwaiter().GetResult();

            // Replace generic CLIPLoader node with Qwen35AnimaCLIPLoader
            if (g.LoadingClip is not null)
            {
                string oldClipNodeId = g.LoadingClip[0].ToString();
                g.Workflow.Remove(oldClipNodeId);
            }

            string device = g.UserInput.Get(DeviceParam, "default");
            string customClipLoaderNode = g.CreateNode(NodeClassType, new JObject()
            {
                ["clip_name"] = modelName,
                ["device"] = device
            });
            g.LoadingClip = [customClipLoaderNode, 0];

            Logs.Info($"[Qwen35Anima] Using {NodeClassType} for text encoder '{modelName}' (device: {device}).");
        }, -15);
    }
}
