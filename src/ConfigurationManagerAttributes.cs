// Display hints read by BepInEx Configuration Manager (Mefino's Outward fork, F5 in game).
// Config Manager finds this class by name via reflection, so no reference to its DLL is needed,
// and the mod works normally when Config Manager isn't installed.
// Trimmed from https://github.com/BepInEx/BepInEx.ConfigurationManager (ConfigurationManagerAttributes.cs).
#pragma warning disable 0169, 0414, 0649
internal sealed class ConfigurationManagerAttributes
{
    public bool? ShowRangeAsPercent;
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
    public bool? Browsable;
    public string Category;
    public object DefaultValue;
    public bool? HideDefaultButton;
    public bool? HideSettingName;
    public string Description;
    public string DispName;
    public int? Order;
    public bool? ReadOnly;
    public bool? IsAdvanced;
    public System.Func<object, string> ObjToStr;
    public System.Func<string, object> StrToObj;
}
#pragma warning restore 0169, 0414, 0649
