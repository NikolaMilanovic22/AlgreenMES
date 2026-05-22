using AlGreenMES.Modules.Orders.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace AlGreenMES.Modules.Orders.Api.Requests;

public class CreateOrderAddItemInput
{
    public Guid ProductCategoryId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

public class CreateOrderRequest
{
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public int Priority { get; set; }
    public OrderType OrderType { get; set; }
    public string? Notes { get; set; }
    public int? CustomWarningDays { get; set; }
    public int? CustomCriticalDays { get; set; }
    public List<CreateOrderAddItemInput>? Items { get; set; }
    public List<IFormFile>? Attachments { get; set; }

    // Manual processes — JSON-stringified because the form is multipart/form-data
    // (file uploads). Format:
    //   ManualProcessesJson: [{"processId":"...","sequenceOrder":1,"defaultComplexity":"S"}, ...]
    //   ManualDependenciesJson: [{"processId":"...","dependsOnProcessId":"..."}, ...]
    public string? ManualProcessesJson { get; set; }
    public string? ManualDependenciesJson { get; set; }
}
