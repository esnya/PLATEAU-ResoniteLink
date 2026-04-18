using System.Reflection;

using ResoniteLink;

namespace Plateau.ResoniteLink.Transport.ResoniteLink;

internal static class ResoniteLinkLegacyCompatibility
{
    public static Task<string> AddSlotAsync(
        IResoniteLinkClient client,
        AddSlot request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (TryInvokeMutationAsync(client, nameof(AddSlotAsync), request, cancellationToken, out Task<string>? legacyTask))
        {
            return legacyTask!;
        }

        return AddEntityViaBatchAsync(client, request, "AddSlot", cancellationToken);
    }

    public static Task<string> AddComponentAsync(
        IResoniteLinkClient client,
        AddComponent request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (TryInvokeMutationAsync(client, nameof(AddComponentAsync), request, cancellationToken, out Task<string>? legacyTask))
        {
            return legacyTask!;
        }

        return AddEntityViaBatchAsync(client, request, "AddComponent", cancellationToken);
    }

    public static Task<Component?> GetComponentAsync(
        IResoniteLinkClient client,
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (TryInvokeReadAsync(client, nameof(GetComponentAsync), componentId, cancellationToken, out Task<Component?>? legacyTask))
        {
            return legacyTask!;
        }

        return GetComponentViaBatchAsync(client, componentId, cancellationToken);
    }

    private static async Task<string> AddEntityViaBatchAsync(
        IResoniteLinkClient client,
        DataModelOperation operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync([operation], cancellationToken);
        Response response = ResolveSingleOperationResponse(batchResponse, operationName);
        if (response is not NewEntityId createdEntity || string.IsNullOrWhiteSpace(createdEntity.EntityId))
        {
            throw new InvalidOperationException($"ResoniteLink {operationName} returned an invalid response.");
        }

        return createdEntity.EntityId;
    }

    private static async Task<Component?> GetComponentViaBatchAsync(
        IResoniteLinkClient client,
        string componentId,
        CancellationToken cancellationToken)
    {
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(
            [new GetComponent { ComponentID = componentId }],
            cancellationToken);
        Response response = ResolveSingleOperationResponse(batchResponse, "GetComponent");
        return response switch
        {
            ComponentData componentResponse => componentResponse.Data,
            _ => throw new InvalidOperationException(
                $"ResoniteLink GetComponent returned an invalid response type '{response.GetType().Name}'."),
        };
    }

    private static Response ResolveSingleOperationResponse(BatchResponse batchResponse, string operationName)
    {
        if (batchResponse is null)
        {
            throw new InvalidOperationException($"ResoniteLink {operationName} batch response was null.");
        }

        if (!batchResponse.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(batchResponse.ErrorInfo)
                    ? $"ResoniteLink {operationName} batch failed."
                    : $"ResoniteLink {operationName} batch failed: {batchResponse.ErrorInfo}");
        }

        List<Response> responses = batchResponse.Responses ?? [];
        if (responses.Count != 1)
        {
            throw new InvalidOperationException(
                $"ResoniteLink batch returned {responses.Count} responses for a single {operationName} operation.");
        }

        Response response = responses[0];
        ResoniteLinkClient.EnsureSuccess(response, operationName);
        return response;
    }

    private static bool TryInvokeMutationAsync<TArg>(
        IResoniteLinkClient client,
        string methodName,
        TArg argument,
        CancellationToken cancellationToken,
        out Task<string>? task)
    {
        if (TryGetMethod(client, methodName, [typeof(TArg), typeof(CancellationToken)], out MethodInfo? method)
            && method is not null
            && method.ReturnType == typeof(Task<string>)
            && method.Invoke(client, [argument, cancellationToken]) is Task<string> resolvedTask)
        {
            task = resolvedTask;
            return true;
        }

        task = null;
        return false;
    }

    private static bool TryInvokeReadAsync(
        IResoniteLinkClient client,
        string methodName,
        string componentId,
        CancellationToken cancellationToken,
        out Task<Component?>? task)
    {
        if (TryGetMethod(client, methodName, [typeof(string), typeof(CancellationToken)], out MethodInfo? method)
            && method is not null
            && method.ReturnType == typeof(Task<Component?>)
            && method.Invoke(client, [componentId, cancellationToken]) is Task<Component?> resolvedTask)
        {
            task = resolvedTask;
            return true;
        }

        task = null;
        return false;
    }

    private static bool TryGetMethod(
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
