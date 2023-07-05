using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public  static class WebApplicationFactoryExtensions
{
    #region Test Logging

    public class XunitLogger : ILogger
    {
        private const string ScopeDelimiter = "=> ";
        private const string Spacer = "      ";

        private const string Trace = "trce";
        private const string Debug = "dbug";
        private const string Info = "info";
        private const string Warn = "warn";
        private const string Error = "fail";
        private const string Critical = "crit";
            
        private readonly string _categoryName;
        private readonly bool _useScopes;
        private readonly ITestOutputHelper _output;
        private readonly IExternalScopeProvider _scopes;

        public XunitLogger(ITestOutputHelper output, IExternalScopeProvider scopes, string categoryName, bool useScopes)
        {
            _output = output;
            _scopes = scopes;
            _categoryName = categoryName;
            _useScopes = useScopes;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _scopes.Push(state);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var sb = new StringBuilder();

            switch (logLevel)
            {
                case LogLevel.Trace:
                    sb.Append(Trace);
                    break;
                case LogLevel.Debug:
                    sb.Append(Debug);
                    break;
                case LogLevel.Information:
                    sb.Append(Info);
                    break;
                case LogLevel.Warning:
                    sb.Append(Warn);
                    break;
                case LogLevel.Error:
                    sb.Append(Error);
                    break;
                case LogLevel.Critical:
                    sb.Append(Critical);
                    break;
                case LogLevel.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
            }

            sb.Append(": ").Append(_categoryName).Append('[').Append(eventId).Append(']').AppendLine();

            if (_useScopes && TryAppendScopes(sb))
                sb.AppendLine();

            sb.Append(Spacer);
            sb.Append(formatter(state, exception));

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(Spacer);
                sb.Append(exception);
            }

            var message = sb.ToString();
            _output.WriteLine(message);
        }

        private bool TryAppendScopes(StringBuilder sb)
        {
            var scopes = false;
            _scopes.ForEachScope((callback, state) =>
            {
                if (!scopes)
                {
                    state.Append(Spacer);
                    scopes = true;
                }
                state.Append(ScopeDelimiter);
                state.Append(callback);
            }, sb);
            return scopes;
        }
    }

    public class XunitLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ITestOutputHelper _output;
        private readonly bool _useScopes;

        private IExternalScopeProvider _scopes;

        public XunitLoggerProvider(ITestOutputHelper output, bool useScopes)
        {
            _output = output;
            _useScopes = useScopes;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XunitLogger(_output, _scopes, categoryName, _useScopes);
        }

        public void Dispose()
        {
        }

        public void SetScopeProvider(IExternalScopeProvider scopes)
        {
            _scopes = scopes;
        }
    }

    public static bool UsesScopes(this ILoggingBuilder builder)
    {
        var serviceProvider = builder.Services.BuildServiceProvider();

        // look for other host builders on this chain calling ConfigureLogging explicitly
        var options = serviceProvider.GetService<SimpleConsoleFormatterOptions>() ??
                      serviceProvider.GetService<JsonConsoleFormatterOptions>() ??
                      serviceProvider.GetService<ConsoleFormatterOptions>();

        if (options != default)
            return options.IncludeScopes;

        // look for other configuration sources
        // See: https://docs.microsoft.com/en-us/dotnet/core/extensions/logging?tabs=command-line#set-log-level-by-command-line-environment-variables-and-other-configuration

        var config = serviceProvider.GetService<IConfigurationRoot>() ?? serviceProvider.GetService<IConfiguration>();
        var logging = config?.GetSection("Logging");
        if (logging == default)
            return false;

        var includeScopes = logging?.GetValue("Console:IncludeScopes", false);
        if (!includeScopes.Value)
            includeScopes = logging?.GetValue("IncludeScopes", false);

        return includeScopes.GetValueOrDefault(false);
    }

    #endregion
}