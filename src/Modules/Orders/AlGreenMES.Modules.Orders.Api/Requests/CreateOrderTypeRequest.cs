namespace AlGreenMES.Modules.Orders.Api.Requests;

public class CreateOrderTypeRequest
{
    // Code is now optional — server auto-generates a slug from Name when empty.
    // Kept here for back-compat / advanced clients.
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool AllowsManualProcesses { get; set; }
}
