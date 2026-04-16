using System.Reflection;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal static class ResoniteLinkClientCompatibilityExtensions
{
    public static Task<string> AddSlotAsync(
        this IResoniteLinkClient client,
        AddSlot request,
        CancellationToken cancellationToken)
    {
        if (client is ResoniteLinkClient directClient)
        {
            return directClient.AddSlotAsync(request, cancellationToken);
        }

        if (client is RetryingResoniteLinkClient retryingClient)
        {
            return retryingClient.AddSlotAsync(request, cancellationToken);
        }

        if (client is MetricsResoniteLinkClient metricsClient)
        {
            return metricsClient.AddSlotAsync(request, cancellationToken);
        }

        if (client is RoutedResoniteLinkClient routedClient)
        {
            return routedClient.AddSlotAsync(request, cancellationToken);
        }

        if (TryInvokeLegacyMutationAsync(
                client,
                nameof(AddSlotAsync),
                request,
                cancellationToken,
                out Task<string>? legacyTask))
        {
            return legacyTask!;
        }

        return AddEntityViaBatchAsync(client, request, nameof(AddSlotAsync), cancellationToken);
    }

    public static Task<string> AddComponentAsync(
        this IResoniteLinkClient client,
        AddComponent request,
        CancellationToken cancellationToken)
    {
        if (client is ResoniteLinkClient directClient)
        {
            return directClient.AddComponentAsync(request, cancellationToken);
        }

        if (client is RetryingResoniteLinkClient retryingClient)
        {
            return retryingClient.AddComponentAsync(request, cancellationToken);
        }

        if (client is MetricsResoniteLinkClient metricsClient)
        {
            return metricsClient.AddComponentAsync(request, cancellationToken);
        }

        if (client is RoutedResoniteLinkClient routedClient)
        {
            return routedClient.AddComponentAsync(request, cancellationToken);
        }

        if (TryInvokeLegacyMutationAsync(
                client,
                nameof(AddComponentAsync),
                request,
                cancellationToken,
                out Task<string>? legacyTask))
        {
            return legacyTask!;
        }

        return AddEntityViaBatchAsync(client, request, nameof(AddComponentAsync), cancellationToken);
    }

    public static Task<Component?> GetComponentAsync(
        this IResoniteLinkClient client,
        string componentId,
        CancellationToken cancellationToken)
    {
        if (client is ResoniteLinkClient directClient)
        {
            return directClient.GetComponentAsync(componentId, cancellationToken);
        }

        if (client is RetryingResoniteLinkClient retryingClient)
        {
            return retryingClient.GetComponentAsync(componentId, cancellationToken);
        }

        if (client is MetricsResoniteLinkClient metricsClient)
        {
            return metricsClient.GetComponentAsync(componentId, cancellationToken);
        }

        if (client is RoutedResoniteLinkClient routedClient)
        {
            return routedClient.GetComponentAsync(componentId, cancellationToken);
        }

        if (TryInvokeLegacyReadAsync(
                client,
                nameof(GetComponentAsync),
                new object[] { componentId, cancellationToken },
                typeof(Task<Component?>),
                out Task<Component?>? legacyTask))
        {
            return legacyTask!;
        }

        return GetComponentViaBatchAsync(client, componentId, cancellationToken);
    }

    private static Task<string> AddEntityViaBatchAsync(
        IResoniteLinkClient client,
        DataModelOperation operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        return ExtractCreatedEntityIdAsync(client, operation, operationName, cancellationToken);
    }

    private static async Task<string> ExtractCreatedEntityIdAsync(
        IResoniteLinkClient client,
        DataModelOperation operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync([operation], cancellationToken);
        if (batchResponse is null)
        {
            throw new InvalidOperationException($"ResoniteLink batch mutation '{operationName}' returned a null response.");
        }

        if (!batchResponse.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(batchResponse.ErrorInfo)
                    ? $"ResoniteLink batch mutation '{operationName}' failed."
                    : $"ResoniteLink batch mutation '{operationName}' failed: {batchResponse.ErrorInfo}");
        }

        Response response = FindOperationResponse(batchResponse.Responses);
        if (!response.Success)
        {
            if (string.IsNullOrWhiteSpace(response.ErrorInfo))
            {
                throw new InvalidOperationException($"ResoniteLink {operationName} failed.");
            }

            throw new InvalidOperationException($"ResoniteLink {operationName} failed: {response.ErrorInfo}");
        }

        if (response is not NewEntityId createdEntityResponse || string.IsNullOrWhiteSpace(createdEntityResponse.EntityId))
        {
            throw new InvalidOperationException($"ResoniteLink {operationName} returned an invalid response.");
        }

        return createdEntityResponse.EntityId;
    }

    private static async Task<Component?> GetComponentViaBatchAsync(
        IResoniteLinkClient client,
        string componentId,
        CancellationToken cancellationToken)
    {
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(
            [new GetComponent { ComponentID = componentId }],
            cancellationToken);

        if (batchResponse is null || !batchResponse.Success)
        {
            if (batchResponse is null)
            {
                throw new InvalidOperationException("ResoniteLink batch get_component returned a null response.");
            }

            if (string.IsNullOrWhiteSpace(batchResponse.ErrorInfo))
            {
                return null;
            }

            throw new InvalidOperationException($"ResoniteLink get component failed: {batchResponse.ErrorInfo}");
        }

        Response response = FindOperationResponse(batchResponse.Responses);
        if (!response.Success)
        {
            if (string.IsNullOrWhiteSpace(response.ErrorInfo))
            {
                return null;
            }

            throw new InvalidOperationException($"ResoniteLink get component failed: {response.ErrorInfo}");
        }

        return response switch
        {
            ComponentData componentResponse => componentResponse.Data,
            _ => throw new InvalidOperationException(
                $"ResoniteLink get component returned an invalid response type '{response.GetType().Name}'."),
        };
    }

    private static Response FindOperationResponse(IReadOnlyList<Response> responses)
    {
        Response response = responses.Count == 1 ? responses[0] : throw new InvalidOperationException(
            $"ResoniteLink batch returned {responses.Count} responses for a single operation.");
        return response;
    }

    private static bool TryInvokeLegacyMutationAsync<TArg>(
        IResoniteLinkClient client,
        string methodName,
        TArg argument,
        CancellationToken cancellationToken,
        out Task<string>? legacyTask)
    {
        if (TryGetLegacyMethod(client, methodName, [typeof(TArg), typeof(CancellationToken)], out MethodInfo? method)
            && method is not null
            && method.ReturnType == typeof(Task<string>))
        {
            object? result = method.Invoke(client, [argument, cancellationToken]);
            if (result is Task<string> task)
            {
                legacyTask = task;
                return true;
            }
        }

        legacyTask = null;
        return false;
    }

    private static bool TryInvokeLegacyReadAsync(
        IResoniteLinkClient client,
        string methodName,
        object[] arguments,
        Type expectedReturnType,
        out Task<Component?>? legacyTask)
    {
        if (TryGetLegacyMethod(client, methodName, [typeof(string), typeof(CancellationToken)], out MethodInfo? method)
            && method is not null
            && method.ReturnType == expectedReturnType)
        {
            object? result = method.Invoke(client, arguments);
            if (result is Task<Component?> task)
            {
                legacyTask = task;
                return true;
            }
        }

        legacyTask = null;
        return false;
    }

    private static bool TryGetLegacyMethod(
        IResoniteLinkClient client,
        string methodName,
        Type[] parameterTypes,
        out MethodInfo? method)
    {
        method = client.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        return method is not null;
    }
}
