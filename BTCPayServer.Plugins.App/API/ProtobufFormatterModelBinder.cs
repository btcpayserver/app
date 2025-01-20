using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.App.API;

public class ProtobufFormatterModelBinder(ILoggerFactory loggerFactory, IHttpRequestStreamReaderFactory readerFactory)
    : BodyModelBinder(InputFormatter, readerFactory, loggerFactory)
{
    private static readonly IInputFormatter[] InputFormatter = [new ProtobufInputFormatter()];
}
