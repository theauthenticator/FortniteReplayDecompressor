using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports;





[NetFieldExportGroup("/Script/FortniteGame.FortClientObservedStatExport", minimalParseMode: ParseMode.Full)]
public class FortClientObservedStatExport : INetFieldExportGroup
{
    [NetFieldExport("StatName", RepLayoutCmdType.PropertyName)]
    public string StatName { get; set; }

    [NetFieldExport("StatValue", RepLayoutCmdType.PropertyInt)]
    public int? StatValue { get; set; }
}