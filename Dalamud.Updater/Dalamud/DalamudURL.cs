using System;
using System.IO;

namespace XIVLauncher.Common.Dalamud;

public class DalamudConst
{
    public const string ASSET_STORE_URL = "https://gh.atmoomen.top/https://raw.githubusercontent.com/AtmoOmen/DalamudAssets/cn/asset.json";
    public const string REMOTE_VERSION = "https://raw.githubusercontent.com/44451516-ff14/Dalamud.Updater.Action/refs/heads/main/version_info.json";

    public static readonly string ROAMINGPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");

}