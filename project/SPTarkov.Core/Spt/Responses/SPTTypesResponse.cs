namespace SPTarkov.Core.SPT.Responses;

public record SPTTypesResponse : IResponse<Dictionary<string, string>>
{
    public Dictionary<string, string>? Response { get; set; } = new();
}

public record SPTPasswordRequiredResponse : IResponse<bool>
{
    public bool Response { get; set; }
}
