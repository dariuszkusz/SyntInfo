using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SyntInfo.Application.Interfaces
{
    public interface IQuery<TResult> { }

    public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
    {
        Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
    }

    public interface ICommand { }

    public interface ICommandHandler<TCommand> where TCommand : ICommand
    {
        Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }

    public interface ICqrsBus
    {
        Task<TResult> SendQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
        Task SendCommandAsync(ICommand command, CancellationToken cancellationToken = default);
    }
}

namespace SyntInfo.Application.Services
{
    public class CqrsBus : SyntInfo.Application.Interfaces.ICqrsBus
    {
        private readonly IServiceProvider _serviceProvider;

        public CqrsBus(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<TResult> SendQueryAsync<TResult>(SyntInfo.Application.Interfaces.IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var handlerType = typeof(SyntInfo.Application.Interfaces.IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
            var handler = _serviceProvider.GetRequiredService(handlerType);

            var method = handlerType.GetMethod("HandleAsync");
            return await (Task<TResult>)method!.Invoke(handler, new object[] { query, cancellationToken })!;
        }

        public async Task SendCommandAsync(SyntInfo.Application.Interfaces.ICommand command, CancellationToken cancellationToken = default)
        {
            var handlerType = typeof(SyntInfo.Application.Interfaces.ICommandHandler<>).MakeGenericType(command.GetType());
            var handler = _serviceProvider.GetRequiredService(handlerType);

            var method = handlerType.GetMethod("HandleAsync");
            await (Task)method!.Invoke(handler, new object[] { command, cancellationToken })!;
        }
    }
}
