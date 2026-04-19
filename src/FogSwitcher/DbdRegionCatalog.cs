namespace FogSwitcher;

internal sealed record DbdRegion(
    string Code,
    string DisplayName,
    string ServiceHost,
    string PingHost,
    string GroupName,
    bool IsTemporary = false)
{
    public IReadOnlyList<string> Hosts => [ServiceHost, PingHost];
}

internal static class DbdRegionCatalog
{
    public static IReadOnlyList<string> GroupOrder { get; } =
    [
        "Europe",
        "North America",
        "South America",
        "Asia",
        "Oceania"
    ];

    public static IReadOnlyList<DbdRegion> VisibleRegions { get; } =
    [
        new("eu-west-2", "Europe (London)", "gamelift.eu-west-2.amazonaws.com", "gamelift-ping.eu-west-2.api.aws", "Europe", true),
        new("eu-west-1", "Europe (Ireland)", "gamelift.eu-west-1.amazonaws.com", "gamelift-ping.eu-west-1.api.aws", "Europe"),
        new("eu-central-1", "Europe (Frankfurt am Main)", "gamelift.eu-central-1.amazonaws.com", "gamelift-ping.eu-central-1.api.aws", "Europe"),
        new("us-east-1", "US East (N. Virginia)", "gamelift.us-east-1.amazonaws.com", "gamelift-ping.us-east-1.api.aws", "North America"),
        new("us-east-2", "US East (Ohio)", "gamelift.us-east-2.amazonaws.com", "gamelift-ping.us-east-2.api.aws", "North America", true),
        new("us-west-1", "US West (N. California)", "gamelift.us-west-1.amazonaws.com", "gamelift-ping.us-west-1.api.aws", "North America"),
        new("us-west-2", "US West (Oregon)", "gamelift.us-west-2.amazonaws.com", "gamelift-ping.us-west-2.api.aws", "North America"),
        new("ca-central-1", "Canada (Central)", "gamelift.ca-central-1.amazonaws.com", "gamelift-ping.ca-central-1.api.aws", "North America", true),
        new("sa-east-1", "South America (Sao Paulo)", "gamelift.sa-east-1.amazonaws.com", "gamelift-ping.sa-east-1.api.aws", "South America"),
        new("ap-northeast-1", "Asia Pacific (Tokyo)", "gamelift.ap-northeast-1.amazonaws.com", "gamelift-ping.ap-northeast-1.api.aws", "Asia"),
        new("ap-northeast-2", "Asia Pacific (Seoul)", "gamelift.ap-northeast-2.amazonaws.com", "gamelift-ping.ap-northeast-2.api.aws", "Asia"),
        new("ap-south-1", "Asia Pacific (Mumbai)", "gamelift.ap-south-1.amazonaws.com", "gamelift-ping.ap-south-1.api.aws", "Asia"),
        new("ap-southeast-1", "Asia Pacific (Singapore)", "gamelift.ap-southeast-1.amazonaws.com", "gamelift-ping.ap-southeast-1.api.aws", "Asia"),
        new("ap-east-1", "Asia Pacific (Hong Kong)", "ec2.ap-east-1.amazonaws.com", "gamelift-ping.ap-east-1.api.aws", "Asia"),
        new("ap-southeast-2", "Asia Pacific (Sydney)", "gamelift.ap-southeast-2.amazonaws.com", "gamelift-ping.ap-southeast-2.api.aws", "Oceania")
    ];

    public static IReadOnlyList<DbdRegion> AlwaysBlockedRegions { get; } =
    [
        new("af-south-1", "Africa (Cape Town)", "gamelift.af-south-1.amazonaws.com", "gamelift-ping.af-south-1.api.aws", "Hidden"),
        new("ap-northeast-3", "Asia Pacific (Osaka)", "gamelift.ap-northeast-3.amazonaws.com", "gamelift-ping.ap-northeast-3.api.aws", "Hidden"),
        new("eu-north-1", "Europe (Stockholm)", "gamelift.eu-north-1.amazonaws.com", "gamelift-ping.eu-north-1.api.aws", "Hidden"),
        new("eu-west-3", "Europe (Paris)", "gamelift.eu-west-3.amazonaws.com", "gamelift-ping.eu-west-3.api.aws", "Hidden"),
        new("eu-south-1", "Europe (Milan)", "gamelift.eu-south-1.amazonaws.com", "gamelift-ping.eu-south-1.api.aws", "Hidden"),
        new("me-south-1", "Middle East (Bahrain)", "gamelift.me-south-1.amazonaws.com", "gamelift-ping.me-south-1.api.aws", "Hidden"),
        new("ap-southeast-5", "Asia Pacific (Malaysia)", "gamelift.ap-southeast-5.amazonaws.com", "gamelift-ping.ap-southeast-5.api.aws", "Hidden"),
        new("ap-southeast-7", "Asia Pacific (Thailand)", "gamelift.ap-southeast-7.amazonaws.com", "gamelift-ping.ap-southeast-7.api.aws", "Hidden"),
        new("cn-north-1", "China (Beijing)", "gamelift.cn-north-1.amazonaws.com.cn", "gamelift-ping.cn-north-1.api.aws", "Hidden"),
        new("cn-northwest-1", "China (Ningxia)", "gamelift.cn-northwest-1.amazonaws.com.cn", "gamelift-ping.cn-northwest-1.api.aws", "Hidden")
    ];
}
