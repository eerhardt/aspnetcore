// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// A function that can process an HTTP request.
/// </summary>
/// <param name="context">The <see cref="HttpContext"/> for the request.</param>
/// <returns>A task that represents the completion of request processing.</returns>
public delegate Task RequestDelegate(HttpContext context);

/// <summary>
/// A function that can create <see cref="RequestDelegate"/> instances.
/// </summary>
/// <param name="handler"></param>
/// <param name="builder"></param>
/// <returns></returns>
public delegate RequestDelegate RequestDelegateFactoryFunc(Delegate handler, EndpointBuilder builder);
