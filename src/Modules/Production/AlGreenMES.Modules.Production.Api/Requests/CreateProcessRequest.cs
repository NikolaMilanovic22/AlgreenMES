namespace AlGreenMES.Modules.Production.Api.Requests;

public record CreateProcessRequest(
    string Code,
    string Name,
    int SequenceOrder,
    List<CreateSubProcessItem>? SubProcesses = null);

public record CreateSubProcessItem(string Name, int SequenceOrder);
