// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace Microsoft.AspNetCore.OpenApi;

internal sealed class OpenApiDocumentService(
    [ServiceKey] string documentName,
    IApiDescriptionGroupCollectionProvider apiDescriptionGroupCollectionProvider,
    IHostEnvironment hostEnvironment,
    IOptionsMonitor<OpenApiOptions> optionsMonitor,
    IServiceProvider serviceProvider,
    IServer? server = null)
{
    private readonly OpenApiOptions _options = optionsMonitor.Get(documentName);
    private readonly OpenApiSchemaService _componentService = serviceProvider.GetRequiredKeyedService<OpenApiSchemaService>(documentName);
    private readonly IOpenApiDocumentTransformer _schemaReferenceTransformer = new OpenApiSchemaReferenceTransformer();

    private static readonly OpenApiEncoding _defaultFormEncoding = new OpenApiEncoding { Style = ParameterStyle.Form, Explode = true };

    /// <summary>
    /// Cache of <see cref="OpenApiOperationTransformerContext"/> instances keyed by the
    /// `ApiDescription.ActionDescriptor.Id` of the associated operation. ActionDescriptor IDs
    /// are unique within the lifetime of an application and serve as helpful associators between
    /// operations, API descriptions, and their respective transformer contexts.
    /// </summary>
    private readonly Dictionary<string, OpenApiOperationTransformerContext> _operationTransformerContextCache = new();
    private static readonly ApiResponseType _defaultApiResponseType = new ApiResponseType { StatusCode = StatusCodes.Status200OK };

    internal bool TryGetCachedOperationTransformerContext(string descriptionId, [NotNullWhen(true)] out OpenApiOperationTransformerContext? context)
        => _operationTransformerContextCache.TryGetValue(descriptionId, out context);

    public async Task<OpenApiDocument> GetOpenApiDocumentAsync(CancellationToken cancellationToken = default)
    {
        // For good hygiene, operation-level tags must also appear in the document-level
        // tags collection. This set captures all tags that have been seen so far.
        HashSet<OpenApiTag> capturedTags = new(OpenApiTagComparer.Instance);
        var document = new OpenApiDocument
        {
            Info = GetOpenApiInfo(),
            Paths = await GetOpenApiPathsAsync(capturedTags, cancellationToken),
            Servers = GetOpenApiServers(),
            Tags = [.. capturedTags]
        };
        await ApplyTransformersAsync(document, cancellationToken);
        return document;
    }

    private async Task ApplyTransformersAsync(OpenApiDocument document, CancellationToken cancellationToken)
    {
        var documentTransformerContext = new OpenApiDocumentTransformerContext
        {
            DocumentName = documentName,
            ApplicationServices = serviceProvider,
            DescriptionGroups = apiDescriptionGroupCollectionProvider.ApiDescriptionGroups.Items,
        };
        // Use index-based for loop to avoid allocating an enumerator with a foreach.
        for (var i = 0; i < _options.DocumentTransformers.Count; i++)
        {
            var transformer = _options.DocumentTransformers[i];
            await transformer.TransformAsync(document, documentTransformerContext, cancellationToken);
        }
        // Move duplicated JSON schemas to the global components.schemas object and map references after all transformers have run.
        await _schemaReferenceTransformer.TransformAsync(document, documentTransformerContext, cancellationToken);
    }

    // Note: Internal for testing.
    internal OpenApiInfo GetOpenApiInfo()
    {
        return new OpenApiInfo
        {
            Title = $"{hostEnvironment.ApplicationName} | {documentName}",
            Version = OpenApiConstants.DefaultOpenApiVersion
        };
    }

    internal List<OpenApiServer> GetOpenApiServers()
    {
        if (hostEnvironment.IsDevelopment() &&
            server?.Features.Get<IServerAddressesFeature>()?.Addresses is { Count: > 0 } addresses)
        {
            return addresses.Select(address => new OpenApiServer { Url = address }).ToList();
        }
        return [];
    }

    /// <summary>
    /// Gets the OpenApiPaths for the document based on the ApiDescriptions.
    /// </summary>
    /// <remarks>
    /// At this point in the construction of the OpenAPI document, we run
    /// each API description through the `ShouldInclude` delegate defined in
    /// the object to support filtering each
    /// description instance into its appropriate document.
    /// </remarks>
    private async Task<OpenApiPaths> GetOpenApiPathsAsync(HashSet<OpenApiTag> capturedTags, CancellationToken cancellationToken)
    {
        var descriptionsByPath = apiDescriptionGroupCollectionProvider.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(_options.ShouldInclude)
            .GroupBy(apiDescription => apiDescription.MapRelativePathToItemPath());
        var paths = new OpenApiPaths();
        foreach (var descriptions in descriptionsByPath)
        {
            Debug.Assert(descriptions.Key != null, "Relative path mapped to OpenApiPath key cannot be null.");
            paths.Add(descriptions.Key, new OpenApiPathItem { Operations = await GetOperationsAsync(descriptions, capturedTags, cancellationToken) });
        }
        return paths;
    }

    private async Task<Dictionary<OperationType, OpenApiOperation>> GetOperationsAsync(IGrouping<string?, ApiDescription> descriptions, HashSet<OpenApiTag> capturedTags, CancellationToken cancellationToken)
    {
        var operations = new Dictionary<OperationType, OpenApiOperation>();
        foreach (var description in descriptions)
        {
            var operation = await GetOperationAsync(description, capturedTags, cancellationToken);
            operation.Extensions.Add(OpenApiConstants.DescriptionId, new ScrubbedOpenApiAny(description.ActionDescriptor.Id));
            _operationTransformerContextCache.TryAdd(description.ActionDescriptor.Id, new OpenApiOperationTransformerContext
            {
                DocumentName = documentName,
                Description = description,
                ApplicationServices = serviceProvider,
            });
            operations[description.GetOperationType()] = operation;
        }
        return operations;
    }

    private async Task<OpenApiOperation> GetOperationAsync(ApiDescription description, HashSet<OpenApiTag> capturedTags, CancellationToken cancellationToken)
    {
        var tags = GetTags(description);
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                capturedTags.Add(tag);
            }
        }
        var operation = new OpenApiOperation
        {
            OperationId = GetOperationId(description),
            Summary = GetSummary(description),
            Description = GetDescription(description),
            Responses = await GetResponsesAsync(description, cancellationToken),
            Parameters = await GetParametersAsync(description, cancellationToken),
            RequestBody = await GetRequestBodyAsync(description, cancellationToken),
            Tags = tags,
        };
        return operation;
    }

    private static string? GetSummary(ApiDescription description)
        => description.ActionDescriptor.EndpointMetadata.OfType<IEndpointSummaryMetadata>().LastOrDefault()?.Summary;

    private static string? GetDescription(ApiDescription description)
        => description.ActionDescriptor.EndpointMetadata.OfType<IEndpointDescriptionMetadata>().LastOrDefault()?.Description;

    private static string? GetOperationId(ApiDescription description)
        => description.ActionDescriptor.AttributeRouteInfo?.Name ??
            description.ActionDescriptor.EndpointMetadata.OfType<IEndpointNameMetadata>().LastOrDefault()?.EndpointName;

    private static List<OpenApiTag>? GetTags(ApiDescription description)
    {
        var actionDescriptor = description.ActionDescriptor;
        if (actionDescriptor.EndpointMetadata?.OfType<ITagsMetadata>().LastOrDefault() is { } tagsMetadata)
        {
            return tagsMetadata.Tags.Select(tag => new OpenApiTag { Name = tag }).ToList();
        }
        // If no tags are specified, use the controller name as the tag. This effectively
        // allows us to group endpoints by the "resource" concept (e.g. users, todos, etc.)
        return [new OpenApiTag { Name = description.ActionDescriptor.RouteValues["controller"] }];
    }

    private async Task<OpenApiResponses> GetResponsesAsync(ApiDescription description, CancellationToken cancellationToken)
    {
        // OpenAPI requires that each operation have a response, usually a successful one.
        // if there are no response types defined, we assume a successful 200 OK response
        // with no content by default.
        if (description.SupportedResponseTypes.Count == 0)
        {
            return new OpenApiResponses
            {
                ["200"] = await GetResponseAsync(description, StatusCodes.Status200OK, _defaultApiResponseType, cancellationToken)
            };
        }

        var responses = new OpenApiResponses();
        foreach (var responseType in description.SupportedResponseTypes)
        {
            // The "default" response type is a special case in OpenAPI used to describe
            // the response for all HTTP status codes that are not explicitly defined
            // for a given operation. This is typically used to describe catch-all scenarios
            // like error responses.
            var responseKey = responseType.IsDefaultResponse
                ? OpenApiConstants.DefaultOpenApiResponseKey
                : responseType.StatusCode.ToString(CultureInfo.InvariantCulture);
            responses.Add(responseKey, await GetResponseAsync(description, responseType.StatusCode, responseType, cancellationToken));
        }
        return responses;
    }

    private async Task<OpenApiResponse> GetResponseAsync(ApiDescription apiDescription, int statusCode, ApiResponseType apiResponseType, CancellationToken cancellationToken)
    {
        var description = ReasonPhrases.GetReasonPhrase(statusCode);
        var response = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>()
        };

        // ApiResponseFormats aggregates information about the supported response content types
        // from different types of Produces metadata. This is handled by ApiExplorer so looking
        // up values in ApiResponseFormats should provide us a complete set of the information
        // encoded in Produces metadata added via attributes or extension methods.
        var apiResponseFormatContentTypes = apiResponseType.ApiResponseFormats
            .Select(responseFormat => responseFormat.MediaType);
        foreach (var contentType in apiResponseFormatContentTypes)
        {
            var schema = apiResponseType.Type is { } type ? await _componentService.GetOrCreateSchemaAsync(type, null, captureSchemaByRef: true, cancellationToken) : new OpenApiSchema();
            response.Content[contentType] = new OpenApiMediaType { Schema = schema };
        }

        // MVC's `ProducesAttribute` doesn't implement the produces metadata that the ApiExplorer
        // looks for when generating ApiResponseFormats above so we need to pull the content
        // types defined there separately.
        var explicitContentTypes = apiDescription.ActionDescriptor.EndpointMetadata
            .OfType<ProducesAttribute>()
            .SelectMany(attr => attr.ContentTypes);
        foreach (var contentType in explicitContentTypes)
        {
            response.Content[contentType] = new OpenApiMediaType();
        }

        return response;
    }

    private async Task<List<OpenApiParameter>?> GetParametersAsync(ApiDescription description, CancellationToken cancellationToken)
    {
        List<OpenApiParameter>? parameters = null;
        foreach (var parameter in description.ParameterDescriptions)
        {
            // Parameters that should be in the request body should not be
            // populated in the parameters list.
            if (parameter.IsRequestBodyParameter())
            {
                continue;
            }

            var openApiParameter = new OpenApiParameter
            {
                Name = parameter.Name,
                In = parameter.Source.Id switch
                {
                    "Query" => ParameterLocation.Query,
                    "Header" => ParameterLocation.Header,
                    "Path" => ParameterLocation.Path,
                    _ => throw new InvalidOperationException($"Unsupported parameter source: {parameter.Source.Id}")
                },
                Required = IsRequired(parameter),
                Schema = await _componentService.GetOrCreateSchemaAsync(parameter.Type, parameter, cancellationToken: cancellationToken),
                Description = GetParameterDescriptionFromAttribute(parameter)
            };

            parameters ??= [];
            parameters.Add(openApiParameter);
        }
        return parameters;
    }

    private static bool IsRequired(ApiParameterDescription parameter)
    {
        var hasRequiredAttribute = parameter.ParameterDescriptor is IParameterInfoParameterDescriptor parameterInfoDescriptor &&
            parameterInfoDescriptor.ParameterInfo.GetCustomAttributes(inherit: true).Any(attr => attr is RequiredAttribute);
        // Per the OpenAPI specification, parameters that are sourced from the path
        // are always required, regardless of the requiredness status of the parameter.
        return parameter.Source == BindingSource.Path || parameter.IsRequired || hasRequiredAttribute;
    }

    // Apply [Description] attributes on the parameter to the top-level OpenApiParameter object and not the schema.
    private static string? GetParameterDescriptionFromAttribute(ApiParameterDescription parameter) =>
        parameter.ParameterDescriptor is IParameterInfoParameterDescriptor { ParameterInfo: { } parameterInfo } &&
        parameterInfo.GetCustomAttributes().OfType<DescriptionAttribute>().LastOrDefault() is { } descriptionAttribute ?
            descriptionAttribute.Description :
            null;

    private async Task<OpenApiRequestBody?> GetRequestBodyAsync(ApiDescription description, CancellationToken cancellationToken)
    {
        // Only one parameter can be bound from the body in each request.
        if (description.TryGetBodyParameter(out var bodyParameter))
        {
            return await GetJsonRequestBody(description.SupportedRequestFormats, bodyParameter, cancellationToken);
        }
        // If there are no body parameters, check for form parameters.
        // Note: Form parameters and body parameters cannot exist simultaneously
        // in the same endpoint.
        if (description.TryGetFormParameters(out var formParameters))
        {
            return await GetFormRequestBody(description.SupportedRequestFormats, formParameters, cancellationToken);
        }
        return null;
    }

    private async Task<OpenApiRequestBody> GetFormRequestBody(IList<ApiRequestFormat> supportedRequestFormats, IEnumerable<ApiParameterDescription> formParameters, CancellationToken cancellationToken)
    {
        if (supportedRequestFormats.Count == 0)
        {
            // Assume "application/x-www-form-urlencoded" as the default media type
            // to match the default assumed in IFormFeature.
            supportedRequestFormats = [new ApiRequestFormat { MediaType = "application/x-www-form-urlencoded" }];
        }

        var requestBody = new OpenApiRequestBody
        {
            Required = formParameters.Any(IsRequired),
            Content = new Dictionary<string, OpenApiMediaType>()
        };

        var schema = new OpenApiSchema { Type = "object", Properties = new Dictionary<string, OpenApiSchema>() };
        // Group form parameters by their name because MVC explodes form parameters that are bound from the
        // same model instance into separate ApiParameterDescriptions in ApiExplorer, while minimal APIs does not.
        //
        // public record Todo(int Id, string Title, bool Completed, DateTime CreatedAt)
        // public void PostMvc([FromForm] Todo person) { }
        // app.MapGet("/form-todo", ([FromForm] Todo todo) => Results.Ok(todo));
        //
        // In the example above, MVC's ApiExplorer will bind four separate arguments to the Todo model while minimal APIs will
        // bind a single Todo model instance to the todo parameter. Grouping by name allows us to handle both cases.
        var groupedFormParameters = formParameters.GroupBy(parameter => parameter.ParameterDescriptor.Name);
        // If there is only one real parameter derived from the form body, then set it directly in the schema.
        var hasMultipleFormParameters = groupedFormParameters.Count() > 1;
        foreach (var parameter in groupedFormParameters)
        {
            // ContainerType is not null when the parameter has been exploded into separate API
            // parameters by ApiExplorer as in the MVC model.
            if (parameter.All(parameter => parameter.ModelMetadata.ContainerType is null))
            {
                var description = parameter.Single();
                var parameterSchema = await _componentService.GetOrCreateSchemaAsync(description.Type, null, cancellationToken: cancellationToken);
                // Form files are keyed by their parameter name so we must capture the parameter name
                // as a property in the schema.
                if (description.Type == typeof(IFormFile) || description.Type == typeof(IFormFileCollection))
                {
                    if (hasMultipleFormParameters)
                    {
                        schema.AllOf.Add(new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                [description.Name] = parameterSchema
                            }
                        });
                    }
                    else
                    {
                        schema.Properties[description.Name] = parameterSchema;
                    }
                }
                else
                {
                    if (hasMultipleFormParameters)
                    {
                        // Here and below: POCOs do not need to be need under their parameter name in the grouping.
                        // The form-binding implementation will capture them implicitly.
                        if (description.ModelMetadata.IsComplexType)
                        {
                            schema.AllOf.Add(parameterSchema);
                        }
                        else
                        {
                            schema.AllOf.Add(new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    [description.Name] = parameterSchema
                                }
                            });
                        }
                    }
                    else
                    {
                        if (description.ModelMetadata.IsComplexType)
                        {
                            schema = parameterSchema;
                        }
                        else
                        {
                            schema.Properties[description.Name] = parameterSchema;
                        }
                    }
                }
            }
            else
            {
                if (hasMultipleFormParameters)
                {
                    var propertySchema = new OpenApiSchema { Type = "object", Properties = new Dictionary<string, OpenApiSchema>() };
                    foreach (var description in parameter)
                    {
                        propertySchema.Properties[description.Name] = await _componentService.GetOrCreateSchemaAsync(description.Type, null, cancellationToken: cancellationToken);
                    }
                    schema.AllOf.Add(propertySchema);
                }
                else
                {
                    foreach (var description in parameter)
                    {
                        schema.Properties[description.Name] = await _componentService.GetOrCreateSchemaAsync(description.Type, null, cancellationToken: cancellationToken);
                    }
                }
            }
        }

        foreach (var requestFormat in supportedRequestFormats)
        {
            var contentType = requestFormat.MediaType;
            requestBody.Content[contentType] = new OpenApiMediaType
            {
                Schema = schema,
                Encoding = new Dictionary<string, OpenApiEncoding>() { [contentType] = _defaultFormEncoding }
            };
        }

        return requestBody;
    }

    private async Task<OpenApiRequestBody> GetJsonRequestBody(IList<ApiRequestFormat> supportedRequestFormats, ApiParameterDescription bodyParameter, CancellationToken cancellationToken)
    {
        if (supportedRequestFormats.Count == 0)
        {
            if (bodyParameter.Type == typeof(Stream) || bodyParameter.Type == typeof(PipeReader))
            {
                // Assume "application/octet-stream" as the default media type
                // for stream-based parameter types.
                supportedRequestFormats = [new ApiRequestFormat { MediaType = "application/octet-stream" }];
            }
            else
            {
                // Assume "application/json" as the default media type
                // for everything else.
                supportedRequestFormats = [new ApiRequestFormat { MediaType = "application/json" }];
            }
        }

        var requestBody = new OpenApiRequestBody
        {
            Required = IsRequired(bodyParameter),
            Content = new Dictionary<string, OpenApiMediaType>(),
            Description = GetParameterDescriptionFromAttribute(bodyParameter)
        };

        foreach (var requestForm in supportedRequestFormats)
        {
            var contentType = requestForm.MediaType;
            requestBody.Content[contentType] = new OpenApiMediaType { Schema = await _componentService.GetOrCreateSchemaAsync(bodyParameter.Type, bodyParameter, captureSchemaByRef: true, cancellationToken: cancellationToken) };
        }

        return requestBody;
    }
}
