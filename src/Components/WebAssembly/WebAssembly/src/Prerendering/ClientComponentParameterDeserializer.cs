// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Microsoft.AspNetCore.Internal.LinkerFlags;
using Microsoft.AspNetCore.Components.WebAssembly.JsonSourceGeneration;

[assembly: JsonSerializable(typeof(Microsoft.AspNetCore.Components.ComponentParameter[]))]
[assembly: JsonSerializable(typeof(List<JsonElement>))]

namespace Microsoft.AspNetCore.Components
{
    internal class WebAssemblyComponentParameterDeserializer
    {
        private readonly ComponentParametersTypeCache _parametersCache;
        private readonly static JsonContext s_context = new JsonContext(WebAssemblyComponentSerializationSettings.JsonSerializationOptions);

        public WebAssemblyComponentParameterDeserializer(
            ComponentParametersTypeCache parametersCache)
        {
            _parametersCache = parametersCache;
        }

        public static WebAssemblyComponentParameterDeserializer Instance { get; } = new WebAssemblyComponentParameterDeserializer(new ComponentParametersTypeCache());

        public ParameterView DeserializeParameters(IList<ComponentParameter> parametersDefinitions, List<JsonElement> parameterValues)
        {
            var parametersDictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (parameterValues.Count != parametersDefinitions.Count)
            {
                // Mismatched number of definition/parameter values.
                throw new InvalidOperationException($"The number of parameter definitions '{parametersDefinitions.Count}' does not match the number parameter values '{parameterValues.Count}'.");
            }

            for (var i = 0; i < parametersDefinitions.Count; i++)
            {
                var definition = parametersDefinitions[i];
                if (definition.Name == null)
                {
                    throw new InvalidOperationException("The name is missing in a parameter definition.");
                }

                if (definition.TypeName == null && definition.Assembly == null)
                {
                    parametersDictionary[definition.Name] = null;
                }
                else if (definition.TypeName == null || definition.Assembly == null)
                {
                    throw new InvalidOperationException($"The parameter definition for '{definition.Name}' is incomplete: Type='{definition.TypeName}' Assembly='{definition.Assembly}'.");
                }
                else
                {
                    var parameterType = _parametersCache.GetParameterType(definition.Assembly, definition.TypeName);
                    if (parameterType == null)
                    {
                        throw new InvalidOperationException($"The parameter '{definition.Name} with type '{definition.TypeName}' in assembly '{definition.Assembly}' could not be found.");
                    }
                    try
                    {
                        var value = (JsonElement)parameterValues[i];
                        var parameterValue = JsonSerializer.Deserialize(
                            value.GetRawText(),
                            parameterType,
                            s_context);

                        parametersDictionary[definition.Name] = parameterValue;
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Could not parse the parameter value for parameter '{definition.Name}' of type '{definition.TypeName}' and assembly '{definition.Assembly}'.", e);
                    }
                }
            }

            return ParameterView.FromDictionary(parametersDictionary);
        }

        [DynamicDependency(JsonSerialized, typeof(ComponentParameter))]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "The correct members will be preserved by the above DynamicDependency.")]
        public ComponentParameter[] GetParameterDefinitions(string parametersDefinitions)
        {
            return JsonSerializer.Deserialize<ComponentParameter[]>(parametersDefinitions, s_context.ComponentParameterArray)!;
        }

        [RequiresUnreferencedCode("This API attempts to JSON deserialize types which might be trimmed.")]
        public List<JsonElement> GetParameterValues(string parameterValues)
        {
            return JsonSerializer.Deserialize(parameterValues, s_context.ListSystemTextJsonJsonElement)!;
        }
    }
}
