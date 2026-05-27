using AssetsTools.NET;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using UABEANext4.AssetWorkspace;
using UABEANext4.Interfaces;
using UABEANext4.Logic.ImportExport;
using UABEANext4.Plugins;
using UABEANext4.ViewModels;
using ValueReplacePlugin.Config;
using ValueReplacePlugin.Logic;

namespace ValueReplacePlugin.ViewModels;

public partial class FieldEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _field = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    public IRelayCommand<FieldEntryViewModel> RemoveCommand { get; init; } = null!;
}

public partial class RuleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _matchName = string.Empty;

    [ObservableProperty]
    private string _matchPathId = string.Empty;

    public ObservableCollection<FieldEntryViewModel> Fields { get; } = [];

    [RelayCommand]
    private void AddField()
    {
        Fields.Add(new FieldEntryViewModel
        {
            RemoveCommand = new RelayCommand<FieldEntryViewModel>(entry =>
            {
                if (entry is not null)
                    Fields.Remove(entry);
            })
        });
    }

    public ReplaceRule ToRule()
    {
        return new ReplaceRule
        {
            MatchName = string.IsNullOrWhiteSpace(MatchName) ? null : MatchName.Trim(),
            MatchPathId = long.TryParse(MatchPathId, out var pid) ? pid : null,
            Fields = Fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Field))
                .Select(f => new FieldEntry { Field = f.Field.Trim(), Value = f.Value })
                .ToList()
        };
    }

    public static RuleViewModel FromRule(ReplaceRule rule)
    {
        var vm = new RuleViewModel
        {
            MatchName = rule.MatchName ?? string.Empty,
            MatchPathId = rule.MatchPathId?.ToString() ?? string.Empty,
        };
        foreach (var f in rule.Fields)
        {
            vm.Fields.Add(new FieldEntryViewModel
            {
                Field = f.Field,
                Value = f.Value,
                RemoveCommand = new RelayCommand<FieldEntryViewModel>(entry =>
                {
                    if (entry is not null)
                        vm.Fields.Remove(entry);
                })
            });
        }
        return vm;
    }
}

public partial class ValueReplaceViewModel : ViewModelBase, IDialogAware<bool>
{
    public string Title => "Replace Field Values";
    public int Width => 760;
    public int Height => 600;
    public bool IsModal => true;

    public event Action<bool>? RequestClose;

    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    [ObservableProperty]
    private RuleViewModel? _selectedRule;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    private readonly Workspace _workspace;
    private readonly IList<AssetInst> _assets;
    private readonly IUavPluginFunctions _funcs;

    public ValueReplaceViewModel(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> assets)
    {
        _workspace = workspace;
        _funcs = funcs;
        _assets = assets;
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new RuleViewModel();
        rule.AddFieldCommand.Execute(null);
        Rules.Add(rule);
        SelectedRule = rule;
    }

    [RelayCommand]
    private void RemoveSelectedRule()
    {
        if (SelectedRule is null)
            return;
        var idx = Rules.IndexOf(SelectedRule);
        Rules.Remove(SelectedRule);
        SelectedRule = Rules.Count > 0 ? Rules[Math.Max(0, idx - 1)] : null;
    }

    public async void LoadConfig()
    {
        var paths = await _funcs.ShowOpenFileDialog(new FilePickerOpenOptions
        {
            Title = "Load replace config",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON config (*.json)") { Patterns = ["*.json"] }]
        });

        if (paths.Length == 0)
            return;

        try
        {
            var json = await File.ReadAllTextAsync(paths[0]);
            var config = ReplaceConfig.FromJson(json);
            Rules.Clear();
            foreach (var rule in config.Rules)
                Rules.Add(RuleViewModel.FromRule(rule));
            SelectedRule = Rules.FirstOrDefault();
            SetStatus("Config loaded.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load: {ex.Message}", true);
        }
    }

    public async void SaveConfig()
    {
        var path = await _funcs.ShowSaveFileDialog(new FilePickerSaveOptions
        {
            Title = "Save replace config",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON config (*.json)") { Patterns = ["*.json"] }]
        });

        if (path is null)
            return;

        try
        {
            var config = BuildConfig();
            await File.WriteAllTextAsync(path, config.ToJson());
            SetStatus("Config saved.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to save: {ex.Message}", true);
        }
    }

    public async void Apply()
    {
        try
        {
            var config = BuildConfig();

            if (config.Rules.Count == 0)
            {
                SetStatus("No rules defined.", true);
                return;
            }

            int applied = 0;
            int skipped = 0;
            var errors = new List<string>();

            var allAssets = WorkspaceItem
                .GetAssetsFileWorkspaceItems(_workspace.RootItems)
                .Where(item => item.Object is AssetsTools.NET.Extra.AssetsFileInstance)
                .SelectMany(item =>
                {
                    var fileInst = (AssetsTools.NET.Extra.AssetsFileInstance)item.Object!;
                    return fileInst.file.AssetInfos.OfType<AssetInst>();
                })
                .ToList();

            foreach (var asset in allAssets)
            {
                var assetName = asset.DisplayName;

                var matchingRules = config.Rules
                    .Where(r =>
                        (r.MatchName is null || r.MatchName == assetName) &&
                        (r.MatchPathId is null || r.MatchPathId == asset.PathId))
                    .ToList();

                if (matchingRules.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var baseField = _workspace.GetBaseField(asset);
                if (baseField is null)
                {
                    errors.Add($"{assetName}: could not read base field");
                    continue;
                }

                JObject? assetJson;
                try
                {
                    assetJson = ExportToJObject(baseField);
                }
                catch (Exception ex)
                {
                    errors.Add($"{assetName}: export failed — {ex.Message}");
                    continue;
                }

                if (assetJson is null)
                {
                    errors.Add($"{assetName}: could not export to JSON");
                    continue;
                }

                foreach (var rule in matchingRules)
                    JsonPatcher.ApplyPatch(assetJson, rule);

                AssetTypeTemplateField templateField;
                try
                {
                    templateField = _workspace.GetTemplateField(asset);
                }
                catch (Exception ex)
                {
                    errors.Add($"{assetName}: template failed — {ex.Message}");
                    continue;
                }

                var refMan = _workspace.Manager.GetRefTypeManager(asset.FileInstance);
                var jsonText = assetJson.ToString(Formatting.None);

                byte[]? data;
                string? importError;
                using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonText)))
                {
                    var importer = new AssetImport(ms, refMan);
                    data = importer.ImportJsonAsset(templateField, out importError);
                }

                if (data is null)
                {
                    errors.Add($"{assetName}: import failed — {importError}");
                    continue;
                }

                asset.UpdateAssetDataAndRow(_workspace, data);
                applied++;
            }

            var summary = $"Applied to {applied} asset(s), skipped {skipped}.";
            if (errors.Count > 0)
            {
                var errorLines = string.Join("\n", errors.Take(10));
                await _funcs.ShowMessageDialog("Errors during apply", $"{summary}\n\n{errorLines}");
                SetStatus($"{summary} {errors.Count} error(s).", true);
            }
            else
            {
                SetStatus(summary, false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Unexpected error: {ex.Message}", true);
            await _funcs.ShowMessageDialog("Apply failed", ex.ToString());
        }
    }

    public void Close()
    {
        RequestClose?.Invoke(true);
    }

    public void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private ReplaceConfig BuildConfig()
    {
        return new ReplaceConfig
        {
            Rules = Rules.Select(r => r.ToRule()).ToList()
        };
    }

    private static JObject? ExportToJObject(AssetsTools.NET.AssetTypeValueField baseField)
    {
        using var ms = new MemoryStream();
        var exporter = new AssetExport(ms);
        exporter.DumpJsonAsset(baseField);
        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        return JObject.Parse(json);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = message;
        HasError = isError;
    }
}
