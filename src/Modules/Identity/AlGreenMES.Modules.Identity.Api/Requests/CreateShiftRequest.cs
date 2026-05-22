namespace AlGreenMES.Modules.Identity.Api.Requests;

public record CreateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime);
