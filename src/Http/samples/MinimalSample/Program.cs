// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string Plaintext() => "Hello, World!";
app.MapGet("/plaintext", Plaintext);

app.MapGet("/", () => $"""
    Operating System: {Environment.OSVersion}
    .NET version: {Environment.Version}
    Username: {Environment.UserName}
    Date and Time: {DateTime.Now}
    """);
var outer = app.MapGroup("/outer");
var inner = outer.MapGroup("/inner");

inner.AddEndpointFilterFactory((routeContext, next) =>
{
    IReadOnlyList<string>? tags = null;

    return async invocationContext =>
    {
        tags ??= invocationContext.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? Array.Empty<string>();

        Console.WriteLine("Running filter!");
        var result = await next(invocationContext);
        return $"{result} | /inner filter! Tags: {(tags.Count == 0 ? "(null)" : string.Join(", ", tags))}";
    };
});

outer.MapGet("/outerget", () => "I'm nested.");
inner.MapGet("/innerget", () => "I'm more nested.");

inner.AddEndpointFilterFactory((routeContext, next) =>
{
    Console.WriteLine($"Building filter! Num args: {routeContext.MethodInfo.GetParameters().Length}"); ;
    return async invocationContext =>
    {
        Console.WriteLine("Running filter!");
        var result = await next(invocationContext);
        return $"{result} | nested filter!";
    };
});

var superNested = inner.MapGroup("/group/{groupName}")
   .MapGroup("/nested/{nestedName}")
   .WithTags("nested", "more", "tags");

superNested.MapGet("/", (string groupName, string nestedName) =>
{
   return $"Hello from {groupName}:{nestedName}!";
});

object Json() => new { message = "Hello, World!" };
app.MapGet("/json", Json).WithTags("json");

string SayHello(string name) => $"Hello, {name}!";
app.MapGet("/hello/{name}", SayHello, RequestDelegateThunks.Thunk0);
app.MapGet("/helloFiltered/{name}", SayHello, RequestDelegateThunks.Thunk0)
    .AddEndpointFilter((ic, next) =>
    {
        Console.WriteLine(ic.GetArgument<string>(0));
        return next(ic);
    });

app.MapGet("/null-result", IResult () => null!);

app.MapGet("/todo/{id}", Results<Ok<Todo>, NotFound, BadRequest> (int id) => id switch
    {
        <= 0 => TypedResults.BadRequest(),
        >= 1 and <= 10 => TypedResults.Ok(new Todo(id, "Walk the dog")),
        _ => TypedResults.NotFound()
    });

var extensions = new Dictionary<string, object?>() { { "traceId", "traceId123" } };

var errors = new Dictionary<string, string[]>() { { "Title", new[] { "The Title field is required." } } };

app.MapGet("/problem/{problemType}", (string problemType) => problemType switch
    {
        "plain" => Results.Problem(statusCode: 500, extensions: extensions),
        "object" => Results.Problem(new ProblemDetails() { Status = 500, Extensions = { { "traceId", "traceId123" } } }),
        "validation" => Results.ValidationProblem(errors, statusCode: 400, extensions: extensions),
        "objectValidation" => Results.Problem(new HttpValidationProblemDetails(errors) { Status = 400, Extensions = { { "traceId", "traceId123" } } }),
        "validationTyped" => TypedResults.ValidationProblem(errors, extensions: extensions),
        _ => TypedResults.NotFound()

    });

app.MapPost("/todos", (TodoBindable todo) => todo);

app.Run();

internal record Todo(int Id, string Title);
public class TodoBindable : IBindableFromHttpContext<TodoBindable>
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsComplete { get; set; }

    public static ValueTask<TodoBindable?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        return ValueTask.FromResult<TodoBindable?>(new TodoBindable { Id = 1, Title = "I was bound from IBindableFromHttpContext<TodoBindable>.BindAsync!" });
    }
}

static class RequestDelegateThunks
{
    public static RequestDelegateFactoryFunc Thunk0 = (Delegate handler, EndpointBuilder builder) =>
    {
        // Cast to the specific type
        var userDelegate = (Func<string, string>)handler;

        // Filtered invocation
        if (builder.FilterFactories.Count > 0)
        {
            var routeHandlerFilters = builder.FilterFactories;

            EndpointFilterDelegate filtererdInvocation = ic =>
            {
                return ValueTask.FromResult<object?>(userDelegate(ic.GetArgument<string>(0)));
            };

            var context0 = new EndpointFilterFactoryContext
            {
                MethodInfo = userDelegate.Method,
                ApplicationServices = builder.ApplicationServices,
            };

            var initialFilteredInvocation = filtererdInvocation;

            for (var i = routeHandlerFilters.Count - 1; i >= 0; i--)
            {
                var filterFactory = routeHandlerFilters[i];
                filtererdInvocation = filterFactory(context0, filtererdInvocation);
            }

            return async context =>
            {
                string name = (string)context.Request.RouteValues["name"]!;

                string result = (string)(await filtererdInvocation(new EndpointFilterInvocationContext<string>(context, name)))!;
                await context.Response.WriteAsync(result);
            };
        }

        return async context =>
        {
            string name = (string)context.Request.RouteValues["name"]!;

            var result = userDelegate(name);
            await context.Response.WriteAsync(result);
        };
    };
}

internal sealed class EndpointFilterInvocationContext<T0> : EndpointFilterInvocationContext, IList<object?>
{
    internal EndpointFilterInvocationContext(HttpContext httpContext, T0 arg0)
    {
        HttpContext = httpContext;
        Arg0 = arg0;
    }

    public object? this[int index]
    {
        get => index switch
        {
            0 => Arg0,
            _ => new IndexOutOfRangeException()
        };
        set
        {
            switch (index)
            {
                case 0:
                    Arg0 = (T0)(object?)value!;
                    break;
                default:
                    break;
            }
        }
    }

    public override HttpContext HttpContext { get; }

    public override IList<object?> Arguments => this;

    public T0 Arg0 { get; set; }

    public int Count => 1;

    public bool IsReadOnly => false;

    public bool IsFixedSize => true;

    public void Add(object? item)
    {
        throw new NotSupportedException();
    }

    public void Clear()
    {
        throw new NotSupportedException();
    }

    public bool Contains(object? item)
    {
        return IndexOf(item) >= 0;
    }

    public void CopyTo(object?[] array, int arrayIndex)
    {
        for (int i = 0; i < Arguments.Count; i++)
        {
            array[arrayIndex++] = Arguments[i];
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        for (int i = 0; i < Arguments.Count; i++)
        {
            yield return Arguments[i];
        }
    }

    public override T GetArgument<T>(int index)
    {
        return index switch
        {
            0 => (T)(object)Arg0!,
            _ => throw new IndexOutOfRangeException()
        };
    }

    public int IndexOf(object? item)
    {
        return Arguments.IndexOf(item);
    }

    public void Insert(int index, object? item)
    {
        throw new NotSupportedException();
    }

    public bool Remove(object? item)
    {
        throw new NotSupportedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotSupportedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
