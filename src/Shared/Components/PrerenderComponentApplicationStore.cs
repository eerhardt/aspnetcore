// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Lifetime;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.JsonSourceGeneration;

[assembly: JsonSerializable(typeof(Dictionary<string, byte[]>))]

namespace Microsoft.AspNetCore.Components
{
    internal class PrerenderComponentApplicationStore : IComponentApplicationStateStore
    {
        private readonly static JsonContext s_context = new JsonContext(new JsonSerializerOptions());

        public PrerenderComponentApplicationStore()
        {
            ExistingState = new();
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Simple deserialize of primitive types.")]
        public PrerenderComponentApplicationStore(string existingState)
        {
            if (existingState is null)
            {
                throw new ArgumentNullException(nameof(existingState));
            }

            ExistingState = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(Convert.FromBase64String(existingState), s_context.DictionarySystemStringSystemByteArray) ??
                throw new ArgumentException(nameof(existingState));
        }

#nullable enable
        public string? PersistedState { get; private set; }
#nullable disable

        public Dictionary<string, byte[]> ExistingState { get; protected set; }

        public Task<IDictionary<string, byte[]>> GetPersistedStateAsync()
        {
            return Task.FromResult((IDictionary<string, byte[]>)ExistingState);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Simple serialize of primitive types.")]
        protected virtual byte[] SerializeState(IReadOnlyDictionary<string, byte[]> state)
        {
            return JsonSerializer.SerializeToUtf8Bytes(state);
        }

        public Task PersistStateAsync(IReadOnlyDictionary<string, byte[]> state)
        {
            var bytes = SerializeState(state);

            var result = Convert.ToBase64String(bytes);
            PersistedState = result;
            return Task.CompletedTask;
        }
    }
}
