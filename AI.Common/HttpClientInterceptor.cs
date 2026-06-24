using System.Net.Http;
using HarmonyLib;

namespace AI.Common;

public static class HttpClientInterceptor
{
    private static Harmony? _harmony;
    private static readonly object _lockObject = new();
    private static Func<LoggingDelegatingHandler>? _handlerFactory;

    public static bool IsInterceptionActive { get; private set; }

    public static void StartInterception(Func<LoggingDelegatingHandler>? handlerFactory = null)
    {
        lock (_lockObject)
        {
            if (IsInterceptionActive)
            {
                return;
            }

            _handlerFactory = handlerFactory ?? (() => new LoggingDelegatingHandler());
            _harmony = new Harmony("AIStudyDemo.Patch");

            PatchHttpClientConstructors();
            IsInterceptionActive = true;
        }
    }

    public static void StopInterception()
    {
        lock (_lockObject)
        {
            if (!IsInterceptionActive || _harmony == null)
            {
                return;
            }

            _harmony.UnpatchAll("AIStudyDemo.Patch");
            _harmony = null;
            _handlerFactory = null;
            IsInterceptionActive = false;

            Console.WriteLine("[AIStudyDemo] HttpClient automatic interception stopped");
        }
    }

    private static void PatchHttpClientConstructors()
    {
        var httpClientType = typeof(HttpClient);

        var fullConstructor = httpClientType.GetConstructor(new[] { typeof(HttpMessageHandler), typeof(bool) });
        if (fullConstructor != null)
        {
            var prefix =
                typeof(HttpClientConstructorPatches).GetMethod(nameof(HttpClientConstructorPatches
                    .HttpClientFullConstructorPrefix));
            _harmony!.Patch(fullConstructor, new HarmonyMethod(prefix));
        }
    }

    internal static LoggingDelegatingHandler CreateHandler(HttpMessageHandler? innerHandler = null)
    {
        var handler = _handlerFactory?.Invoke() ?? new LoggingDelegatingHandler();

        if (innerHandler != null && handler.InnerHandler == null)
        {
            var innerHandlerProperty = typeof(DelegatingHandler).GetProperty("InnerHandler");
            if (innerHandlerProperty?.CanWrite == true)
            {
                innerHandlerProperty.SetValue(handler, innerHandler);
            }
        }

        return handler;
    }
}

internal static class HttpClientConstructorPatches
{
    public static void HttpClientFullConstructorPrefix(ref HttpMessageHandler handler, bool disposeHandler)
    {
        // Only wrap if it's not already a LoggingDelegatingHandler
        if (handler is not LoggingDelegatingHandler)
        {
            handler = HttpClientInterceptor.CreateHandler(handler);
        }
    }
}