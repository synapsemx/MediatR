using System.Diagnostics;

namespace MediatR.Internal
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal abstract class RequestHandlerBase
    {
        protected static object GetHandler(Type requestType, SingleInstanceFactory singleInstanceFactory)
        {
            try
            {
                return singleInstanceFactory(requestType);
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected static THandler GetHandler<THandler>(SingleInstanceFactory factory)
        {
            return (THandler)GetHandler(typeof(THandler), factory);
        }

        protected static InvalidOperationException BuildException(object message)
        {
            return new InvalidOperationException("Handler was not found for request of type " + message.GetType() + ".\r\nContainer or service locator not configured properly or handlers not registered with your container.");
        }
    }

    internal abstract class RequestHandler : RequestHandlerBase
    {
        public abstract Task Handle(IRequest request, CancellationToken cancellationToken,
            SingleInstanceFactory singleFactory, MultiInstanceFactory multiFactory);
    }

    internal abstract class RequestHandler<TResponse> : RequestHandlerBase
    {
        public abstract Task<TResponse> Handle(IRequest<TResponse> request, CancellationToken cancellationToken,
            SingleInstanceFactory singleFactory, MultiInstanceFactory multiFactory);
    }

    internal class RequestHandlerImpl<TRequest, TResponse> : RequestHandler<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Handle(IRequest<TResponse> request, CancellationToken cancellationToken,
            SingleInstanceFactory singleFactory, MultiInstanceFactory multiFactory)
        {
            var handler = GetHandler((TRequest)request, cancellationToken, singleFactory);

            var pipeline = GetPipeline((TRequest)request, handler, multiFactory);

            return pipeline;
        }

        private RequestHandlerDelegate<TResponse> GetHandler(TRequest request, CancellationToken cancellationToken, SingleInstanceFactory factory)
        {
            var handler = GetHandler<IRequestHandler<TRequest, TResponse>>(factory);
            if (handler != null)
            {
                return () => Task.FromResult(handler.Handle(request));
            }

            var asyncHandler = GetHandler<IAsyncRequestHandler<TRequest, TResponse>>(factory);
            if (asyncHandler != null)
            {
                return () => asyncHandler.Handle(request);
            }

            var cancellableAsyncHandler = GetHandler<ICancellableAsyncRequestHandler<TRequest, TResponse>>(factory);
            if (cancellableAsyncHandler != null)
            {
                return () => cancellableAsyncHandler.Handle(request, cancellationToken);
            }

            throw BuildException(request);
        }

        private static Task<TResponse> GetPipeline(TRequest request, RequestHandlerDelegate<TResponse> invokeHandler, MultiInstanceFactory factory)
        {
            var behaviors = factory(typeof(IPipelineBehavior<TRequest, TResponse>))
                .Cast<IPipelineBehavior<TRequest, TResponse>>()
                .Reverse();

            var aggregate = behaviors.Aggregate(invokeHandler, (next, pipeline) => () => pipeline.Handle(request, next));

            return aggregate();
        }
    }

    internal class RequestHandlerImpl<TRequest> : RequestHandler
        where TRequest : IRequest
    {
        public override Task Handle(IRequest request, CancellationToken cancellationToken,
            SingleInstanceFactory singleFactory, MultiInstanceFactory multiFactory)
        {
            var handler = GetHandler((TRequest)request, cancellationToken, singleFactory);

            var pipeline = GetPipeline((TRequest)request, handler, multiFactory);

            return pipeline;
        }

        private RequestHandlerDelegate<Unit> GetHandler(TRequest request, CancellationToken cancellationToken, SingleInstanceFactory factory)
        {
            var handler = GetHandler<IRequestHandler<TRequest>>(factory);
            if (handler != null)
            {
                return () =>
                    {
                        handler.Handle(request);
                        return Unit.Task;
                    };
            }

            var asyncHandler = GetHandler<IAsyncRequestHandler<TRequest>>(factory);
            if (asyncHandler != null)
            {
                return async () =>
                    {
                        await asyncHandler.Handle(request).ConfigureAwait(false);
                        return Unit.Value;
                    };
            }

            var cancellableAsyncHandler = GetHandler<ICancellableAsyncRequestHandler<TRequest>>(factory);
            if (cancellableAsyncHandler != null)
            {
                return async () =>
                    {
                        await cancellableAsyncHandler.Handle(request, cancellationToken).ConfigureAwait(false);
                        return Unit.Value;
                    };
            }

            throw BuildException(request);
        }

        private static Task<Unit> GetPipeline(TRequest request, RequestHandlerDelegate<Unit> invokeHandler, MultiInstanceFactory factory)
        {
            var behaviors = factory(typeof(IPipelineBehavior<TRequest, Unit>))
                .Cast<IPipelineBehavior<TRequest, Unit>>()
                .Reverse();

            var aggregate = behaviors.Aggregate(invokeHandler, (next, pipeline) => () => pipeline.Handle(request, next));

            return aggregate();
        }
    }
}