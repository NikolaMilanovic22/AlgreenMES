using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.SetProcessExcludedFromReports;

public record SetProcessExcludedFromReportsCommand(Guid OrderItemProcessId, bool Excluded) : IRequest<Unit>;
