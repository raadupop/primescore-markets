using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PrimeScore.Engine.Host.Api;

/// <summary>
/// Date-time query parameters (<c>as_of</c>, <c>from</c>, <c>to</c>) are read the same on every
/// host: a value without an offset is UTC, never the server's local time (SRS SIG-004 must not
/// depend on where the engine runs). A '+' offset that arrived unescaped as a space is restored.
/// </summary>
internal sealed partial class UtcDateTimeOffsetModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None || string.IsNullOrWhiteSpace(value.FirstValue))
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var text = UnescapedPlus().Replace(value.FirstValue.Trim(), "$1+$2");
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                $"{bindingContext.ModelName}: '{value.FirstValue}' is not an ISO 8601 date-time, e.g. 2026-02-27T21:15:00Z");
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?) (\d{2}:?\d{2})$")]
    private static partial Regex UnescapedPlus();
}

internal sealed class UtcDateTimeOffsetModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Metadata.UnderlyingOrModelType == typeof(DateTimeOffset) && context.BindingInfo.BindingSource != BindingSource.Body
            ? new UtcDateTimeOffsetModelBinder()
            : null;
    }
}

/// <summary>
/// A query parameter that cannot be read is a 400, never silently dropped: an unreadable
/// <c>as_of</c> would otherwise return current data for a point-in-time request.
/// </summary>
internal sealed class RejectUnreadableParametersFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModelState.IsValid)
        {
            return;
        }

        var details = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                string.IsNullOrEmpty(error.ErrorMessage) ? $"{entry.Key}: could not be read" : error.ErrorMessage))
            .ToArray();
        context.Result = ApiResults.BadRequest("The request could not be read.", details);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
