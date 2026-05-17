using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Kernel.Abstractions;

public interface ICommand : IRequest<Result>
{
}

public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}
