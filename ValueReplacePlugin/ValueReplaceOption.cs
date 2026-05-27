using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;
using ValueReplacePlugin.ViewModels;

namespace ValueReplacePlugin;

public class ValueReplaceOption : IUavPluginOption
{
    public string Name => "Replace Field Values";
    public string Description => "Apply field replacement rules to selected assets";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        return mode == UavPluginMode.Export && selection.Count > 0;
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        var vm = new ValueReplaceViewModel(workspace, funcs, selection);
        var result = await funcs.ShowDialog(vm);
        return result == true;
    }
}
