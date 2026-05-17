using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Kernel.Abstractions;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}
