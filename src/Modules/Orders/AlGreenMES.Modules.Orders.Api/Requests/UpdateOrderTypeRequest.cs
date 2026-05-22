namespace AlGreenMES.Modules.Orders.Api.Requests;

public class UpdateOrderTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public bool AllowsManualProcesses { get; set; }
    public bool IsActive { get; set; }
}
