namespace Renewtron.Services;

/// <summary>
/// After a successful change through the renewals or sales admin APIs (retry, requeue, undo,
/// mark BPAY paid…), refreshes the paid-renewal records so every page shows the new status at once.
/// </summary>
public sealed class RenewalOrderRefreshFilter(IRenewalOrderService orders) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
            await orders.RefreshQuietlyAsync(http.RequestAborted);
        return result;
    }
}
